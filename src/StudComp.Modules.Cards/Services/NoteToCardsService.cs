using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.Cards.Services;

/// <summary>Кандидат в карточку вместе с отметкой «уже есть» — для диалога предпросмотра (new_addons.md §7.3).</summary>
/// <param name="Front">Лицевая сторона будущей карточки.</param>
/// <param name="Back">Оборот будущей карточки.</param>
/// <param name="Kind">Тип, подсказанный шаблоном.</param>
/// <param name="IsLikelyDuplicate">
/// Похожая карточка с тем же источником уже существует — чекбокс в диалоге приходит снятым.
/// </param>
/// <param name="ExistingCardId">Карточка-дубликат, если найдена.</param>
public sealed record NoteCardCandidateResult(
    string Front,
    string Back,
    CardKind Kind,
    bool IsLikelyDuplicate,
    Guid? ExistingCardId);

/// <summary>
/// Разбор заметки на карточки (new_addons.md §7.3): кнопка «Разобрать на карточки» в шапке заметки.
/// </summary>
/// <remarks>
/// Сам разбор — чистая функция <see cref="NoteToCardParser"/> в <c>Core</c>; этот сервис только
/// оркеструет: строит блочную модель через уже подключённый <see cref="IMarkdownDocumentModelBuilder"/>
/// (тот же приём переиспользования, что у предпросмотра заметки в 12.3) и отмечает кандидатов,
/// для которых уже есть карточка с тем же источником — повторный разбор той же заметки не плодит дубли.
/// </remarks>
public interface INoteToCardsService
{
    Task<IReadOnlyList<NoteCardCandidateResult>> BuildCandidatesAsync(
        Guid noteId,
        string markdown,
        CancellationToken ct = default);

    /// <summary>Создать карточки из принятых пользователем кандидатов. Возвращает, сколько заведено.</summary>
    Task<int> CreateFromCandidatesAsync(
        Guid noteId,
        Guid? subjectId,
        IReadOnlyList<NoteCardCandidateResult> accepted,
        CancellationToken ct = default);
}

internal sealed class NoteToCardsService(
    IMarkdownDocumentModelBuilder markdownBuilder,
    ICardService cards,
    ICardRepository cardRepository) : INoteToCardsService
{
    public async Task<IReadOnlyList<NoteCardCandidateResult>> BuildCandidatesAsync(
        Guid noteId,
        string markdown,
        CancellationToken ct = default)
    {
        var model = markdownBuilder.Build(markdown ?? string.Empty);
        var parsed = NoteToCardParser.Parse(model);
        if (parsed.Count == 0)
        {
            return [];
        }

        var results = new List<NoteCardCandidateResult>(parsed.Count);
        foreach (var candidate in parsed)
        {
            ct.ThrowIfCancellationRequested();

            // Похожие ищет уже существующий FindSimilarAsync (Dice ≥ 0.8); дубликатом эту заметку
            // делает не просто похожий Front где-то в картотеке, а именно карточка, рождённая из
            // этой же заметки раньше.
            var similar = await cards.FindSimilarAsync(candidate.Front, excludeId: null, ct).ConfigureAwait(false);
            var existing = similar.FirstOrDefault(c => c.SourceNoteId == noteId);

            results.Add(new NoteCardCandidateResult(
                candidate.Front, candidate.Back, candidate.Kind, existing is not null, existing?.Id));
        }

        return results;
    }

    public async Task<int> CreateFromCandidatesAsync(
        Guid noteId,
        Guid? subjectId,
        IReadOnlyList<NoteCardCandidateResult> accepted,
        CancellationToken ct = default)
    {
        var toCreate = accepted.Where(c => !c.IsLikelyDuplicate).ToList();
        if (toCreate.Count == 0)
        {
            return 0;
        }

        var now = DateTimeOffset.Now;
        var newCards = toCreate
            .Select(c => new Card
            {
                Id = Guid.NewGuid(),
                SubjectId = subjectId,
                Kind = c.Kind,
                Front = c.Front,
                Back = c.Back,
                SourceNoteId = noteId,
                CreatedAt = now,
                UpdatedAt = now,
            })
            .ToList();

        await cardRepository.AddManyAsync(newCards, ct).ConfigureAwait(false);
        return newCards.Count;
    }
}
