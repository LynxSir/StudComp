using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Organizer.Services;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Связь Архивариус → Органайзер (ARCHITECTURE §9.3): после сортировки файла всплывает предложение
/// привязать его к похожему дедлайну. Единственная точка, где модули соприкасаются семантически, —
/// и она идёт через <c>IMessenger</c>, а не через прямой вызов (§5.1).
/// </summary>
public sealed class DeadlineLinkSuggestionTests : OrganizerDatabaseTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    private readonly IToastService _toasts = Substitute.For<IToastService>();
    private readonly WeakReferenceMessenger _messenger = new();
    private readonly TestOptionsMonitor<ArchivistOptions> _options = new(new ArchivistOptions());

    private DeadlineLinkSuggestionService CreateService() => new(
        _messenger,
        Deadlines,
        _toasts,
        _options,
        NullLogger<DeadlineLinkSuggestionService>.Instance);

    [Fact]
    public async Task Similar_deadline_triggers_a_link_offer()
    {
        var subjectId = await SeedSubjectAsync();
        var deadlineId = await SeedDeadlineAsync(subjectId, "ЛР4 по матанализу", Now.AddDays(2));
        var fileRecordId = await SeedFileRecordAsync();

        var suggested = await CreateService().SuggestLinkAsync(
            Message(fileRecordId, subjectId, "ЛР4_Матан.docx"), CancellationToken.None);

        Assert.Equal(deadlineId, suggested!.Id);
        _toasts.Received(1).ShowAction(
            Arg.Any<string>(), Arg.Any<string>(), "Привязать", Arg.Any<Func<Task>>(), ToastKind.Info);
    }

    [Fact]
    public async Task Pressing_the_button_actually_links_the_file()
    {
        var subjectId = await SeedSubjectAsync();
        var deadlineId = await SeedDeadlineAsync(subjectId, "ЛР4 по матанализу", Now.AddDays(2));
        var fileRecordId = await SeedFileRecordAsync();

        Func<Task>? action = null;
        _toasts
            .When(t => t.ShowAction(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<Task>>(), Arg.Any<ToastKind>()))
            .Do(call => action = call.Arg<Func<Task>>());

        await CreateService().SuggestLinkAsync(
            Message(fileRecordId, subjectId, "ЛР4_Матан.docx"), CancellationToken.None);

        Assert.NotNull(action);
        await action();

        var deadline = await DeadlineRepo.GetByIdAsync(deadlineId);
        Assert.Equal(fileRecordId, deadline!.LinkedFileRecordId);
        _toasts.Received(1).Show("Файл привязан", Arg.Any<string>(), ToastKind.Success);
    }

    [Fact]
    public async Task Unrelated_file_produces_no_offer()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedDeadlineAsync(subjectId, "ЛР4 по матанализу", Now.AddDays(2));
        var fileRecordId = await SeedFileRecordAsync();

        var suggested = await CreateService().SuggestLinkAsync(
            Message(fileRecordId, subjectId, "отпускные_фото.zip"), CancellationToken.None);

        Assert.Null(suggested);
        AssertNoOffer();
    }

    [Fact]
    public async Task Already_linked_deadline_is_not_offered_again()
    {
        var subjectId = await SeedSubjectAsync();
        var deadlineId = await SeedDeadlineAsync(subjectId, "ЛР4 по матанализу", Now.AddDays(2));
        var firstFile = await SeedFileRecordAsync(@"C:\Архив\ЛР4_старый.docx");
        Assert.True((await Deadlines.LinkFileAsync(deadlineId, firstFile)).IsSuccess);

        var secondFile = await SeedFileRecordAsync(@"C:\Архив\ЛР4_новый.docx");
        var suggested = await CreateService().SuggestLinkAsync(
            Message(secondFile, subjectId, "ЛР4_Матан.docx"), CancellationToken.None);

        Assert.Null(suggested);
        AssertNoOffer();
    }

    [Fact]
    public async Task Closed_deadline_is_not_offered()
    {
        var subjectId = await SeedSubjectAsync();
        var deadlineId = await SeedDeadlineAsync(subjectId, "ЛР4 по матанализу", Now.AddDays(2));
        Assert.True((await Deadlines.SetStatusAsync(deadlineId, DeadlineStatus.Done)).IsSuccess);
        var fileRecordId = await SeedFileRecordAsync();

        var suggested = await CreateService().SuggestLinkAsync(
            Message(fileRecordId, subjectId, "ЛР4_Матан.docx"), CancellationToken.None);

        Assert.Null(suggested);
        AssertNoOffer();
    }

    [Fact]
    public async Task Deadline_far_outside_the_window_is_not_offered()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedDeadlineAsync(subjectId, "ЛР4 по матанализу", Now.AddDays(200));
        var fileRecordId = await SeedFileRecordAsync();

        var suggested = await CreateService().SuggestLinkAsync(
            Message(fileRecordId, subjectId, "ЛР4_Матан.docx"), CancellationToken.None);

        Assert.Null(suggested);
        AssertNoOffer();
    }

    [Fact]
    public async Task File_without_a_subject_is_ignored()
    {
        // Дедлайн всегда принадлежит предмету (§7.1) — без предмета кандидатов не выбрать.
        var subjectId = await SeedSubjectAsync();
        await SeedDeadlineAsync(subjectId, "ЛР4 по матанализу", Now.AddDays(2));
        var fileRecordId = await SeedFileRecordAsync();

        var suggested = await CreateService().SuggestLinkAsync(
            Message(fileRecordId, subjectId: null, "ЛР4_Матан.docx"), CancellationToken.None);

        Assert.Null(suggested);
        AssertNoOffer();
    }

    [Fact]
    public async Task Suggestion_can_be_switched_off()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedDeadlineAsync(subjectId, "ЛР4 по матанализу", Now.AddDays(2));
        var fileRecordId = await SeedFileRecordAsync();
        _options.CurrentValue.DeadlineLinkSuggestionEnabled = false;

        var suggested = await CreateService().SuggestLinkAsync(
            Message(fileRecordId, subjectId, "ЛР4_Матан.docx"), CancellationToken.None);

        Assert.Null(suggested);
        AssertNoOffer();
    }

    [Fact]
    public async Task The_closest_match_wins_among_several_candidates()
    {
        var subjectId = await SeedSubjectAsync();
        await SeedDeadlineAsync(subjectId, "ЛР3 по матанализу", Now.AddDays(1));
        var expected = await SeedDeadlineAsync(subjectId, "ЛР4 по матанализу", Now.AddDays(5));
        await SeedDeadlineAsync(subjectId, "Экзамен по матанализу", Now.AddDays(3));
        var fileRecordId = await SeedFileRecordAsync();

        var suggested = await CreateService().SuggestLinkAsync(
            Message(fileRecordId, subjectId, "ЛР4_Матан.docx"), CancellationToken.None);

        Assert.Equal(expected, suggested!.Id);
    }

    [Fact]
    public async Task Subscription_is_wired_through_the_message_bus()
    {
        // StartAsync/StopAsync — единственное, ради чего сервис вообще IHostedService.
        var subjectId = await SeedSubjectAsync();
        var deadlineId = await SeedDeadlineAsync(subjectId, "ЛР4 по матанализу", Now.AddDays(2));
        var fileRecordId = await SeedFileRecordAsync();

        var service = CreateService();
        await service.StartAsync(CancellationToken.None);

        _messenger.Send(Message(fileRecordId, subjectId, "ЛР4_Матан.docx"));

        // Обработчик уходит в отдельную задачу, чтобы не блокировать конвейер сортировки.
        await WaitForOfferAsync();
        _toasts.Received(1).ShowAction(
            Arg.Any<string>(), Arg.Any<string>(), "Привязать", Arg.Any<Func<Task>>(), ToastKind.Info);

        await service.StopAsync(CancellationToken.None);
        _toasts.ClearReceivedCalls();

        _messenger.Send(Message(fileRecordId, subjectId, "ЛР4_Матан.docx"));
        await Task.Delay(200);

        AssertNoOffer();
        GC.KeepAlive(deadlineId);
    }

    private async Task WaitForOfferAsync()
    {
        for (var i = 0; i < 50; i++)
        {
            if (_toasts.ReceivedCalls().Any(c => c.GetMethodInfo().Name == nameof(IToastService.ShowAction)))
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    private void AssertNoOffer() =>
        _toasts.DidNotReceive().ShowAction(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<Task>>(), Arg.Any<ToastKind>());

    private static FileSortedMessage Message(Guid fileRecordId, Guid? subjectId, string fileName) =>
        new(fileRecordId, subjectId, $@"C:\Архив\{fileName}", fileName, Now);

    private async Task<Guid> SeedDeadlineAsync(Guid subjectId, string title, DateTimeOffset dueDate)
    {
        var result = await Deadlines.CreateAsync(new Deadline
        {
            SubjectId = subjectId,
            Title = title,
            DueDate = dueDate,
            Type = DeadlineType.Homework,
            Priority = DeadlinePriority.Normal,
            Status = DeadlineStatus.Pending,
        });

        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
