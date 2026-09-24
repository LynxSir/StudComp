using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Единственная точка, где Органайзер слушает Архивариуса (ARCHITECTURE §9.3, §5.1): после успешной
/// сортировки предлагает привязать файл к похожему дедлайну — «Похоже, это ЛР4 по Матану — привязать
/// к дедлайну 10.09?».
/// </summary>
/// <remarks>
/// Реализует <see cref="IHostedService"/> не ради фонового цикла, а ради момента создания: ленивый
/// singleton никто бы не сконструировал, и подписка на шину не случилась бы вовсе. Ссылки на
/// <c>Modules.Archivist</c> здесь нет — только запись-сообщение из <c>Core</c>.
/// </remarks>
internal sealed class DeadlineLinkSuggestionService(
    IMessenger messenger,
    IDeadlineService deadlines,
    IToastService toasts,
    IOptionsMonitor<ArchivistOptions> options,
    ILogger<DeadlineLinkSuggestionService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        messenger.Register<DeadlineLinkSuggestionService, FileSortedMessage>(
            this,
            static (recipient, message) => recipient.OnFileSorted(message));

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        messenger.UnregisterAll(this);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Обработчик шины синхронный (таково устройство <c>IMessenger</c>), а работа асинхронная —
    /// поэтому запускаем её отдельной задачей и гасим любые исключения: уронить отправителя,
    /// то есть конвейер сортировки, предложение о привязке права не имеет.
    /// </summary>
    private void OnFileSorted(FileSortedMessage message) =>
        _ = Task.Run(async () =>
        {
            try
            {
                await SuggestLinkAsync(message, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Подбор дедлайна для файла {File} завершился ошибкой", message.FileName);
            }
        });

    /// <summary><c>internal</c> — тесты зовут напрямую, без гонок с фоновой задачей.</summary>
    internal async Task<Deadline?> SuggestLinkAsync(FileSortedMessage message, CancellationToken ct)
    {
        var current = options.CurrentValue;

        // Без предмета кандидатов не выбрать: дедлайн всегда принадлежит предмету (§7.1).
        if (!current.DeadlineLinkSuggestionEnabled || message.SubjectId is not { } subjectId)
        {
            return null;
        }

        var windowDays = Math.Max(1, current.DeadlineLinkWindowDays);
        var window = TimeSpan.FromDays(windowDays);

        var candidates = (await deadlines.GetBySubjectAsync(subjectId, ct).ConfigureAwait(false))
            .Where(d => d.Status == DeadlineStatus.Pending
                && d.LinkedFileRecordId is null
                && (d.DueDate - message.SortedAt).Duration() <= window)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        var best = candidates
            .Select(d => (Deadline: d, Score: DeadlineMatching.Score(
                message.FileName, d.Title, d.DueDate, message.SortedAt, windowDays)))
            .OrderByDescending(x => x.Score)
            .First();

        if (best.Score < current.DeadlineLinkMinScore)
        {
            logger.LogDebug(
                "Дедлайн для {File} не подобран: лучший счёт {Score:F2} ниже порога {Threshold:F2}",
                message.FileName,
                best.Score,
                current.DeadlineLinkMinScore);
            return null;
        }

        var deadline = best.Deadline;
        var deadlineId = deadline.Id;
        var fileRecordId = message.FileRecordId;

        logger.LogInformation(
            "Предлагаем привязать {File} к дедлайну «{Title}» (счёт {Score:F2})",
            message.FileName,
            deadline.Title,
            best.Score);

        toasts.ShowAction(
            "Похоже, это работа по дедлайну",
            $"«{message.FileName}» → «{deadline.Title}» до {deadline.DueDate.LocalDateTime:dd.MM}",
            "Привязать",
            () => LinkAsync(deadlineId, fileRecordId, deadline.Title),
            ToastKind.Info);

        return deadline;
    }

    private async Task LinkAsync(Guid deadlineId, Guid fileRecordId, string deadlineTitle)
    {
        var result = await deadlines.LinkFileAsync(deadlineId, fileRecordId).ConfigureAwait(false);

        if (result.IsFailure)
        {
            logger.LogWarning("Привязка файла к дедлайну {DeadlineId} не удалась: {Error}",
                deadlineId, result.Error.Message);
            toasts.Show("Не удалось привязать", result.Error.Message, ToastKind.Error);
            return;
        }

        toasts.Show("Файл привязан", $"Теперь он виден в дедлайне «{deadlineTitle}».", ToastKind.Success);
    }
}
