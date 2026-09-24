using NSubstitute;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Разбор заметки на карточки (new_addons.md §7.3, Phase 12.9): диалог предпросмотра должен видеть,
/// что уже когда-то было создано из этой же заметки — DoD «дважды подряд не плодит дубли».
/// </summary>
/// <remarks>
/// Сам разбор блочной модели покрыт таблично в <c>NoteToCardParserTests</c> (Core); здесь —
/// оркестрация: подставной <see cref="IMarkdownDocumentModelBuilder"/> отдаёт фиксированную модель,
/// а проверяется поведение сервиса поверх настоящей базы.
/// </remarks>
public sealed class NoteToCardsServiceTests : CardsDatabaseTestBase
{
    private static ReportDocumentModel OneCandidateModel() => new(
        null,
        [
            new HeadingBlock(2, "Семафор"),
            new ParagraphBlock([new InlineRun("Примитив синхронизации потоков.")]),
        ],
        false);

    private static ReportDocumentModel NoCandidatesModel() => new(
        null,
        [new ParagraphBlock([new InlineRun("Просто абзац без узнаваемой структуры.")])],
        false);

    private INoteToCardsService CreateService(ReportDocumentModel model)
    {
        var builder = Substitute.For<IMarkdownDocumentModelBuilder>();
        builder.Build(Arg.Any<string>()).Returns(model);
        return new NoteToCardsService(builder, Cards, CardRepo);
    }

    /// <summary>
    /// <c>Card.SourceNoteId</c> — настоящий внешний ключ на <c>Notes</c> (new_addons.md §3.3), поэтому
    /// тесту нужна реальная строка заметки, а не произвольный <see cref="Guid"/>.
    /// </summary>
    private async Task<Guid> SeedNoteAsync()
    {
        await using var context = CreateContext();
        var note = new Note
        {
            Id = Guid.NewGuid(),
            Kind = NoteKind.Free,
            Title = "Заметка",
            ContentMarkdown = "неважно — модель подставная",
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now,
        };

        context.Notes.Add(note);
        await context.SaveChangesAsync();
        return note.Id;
    }

    [Fact]
    public async Task A_recognized_pattern_becomes_a_fresh_candidate_the_first_time()
    {
        var service = CreateService(OneCandidateModel());
        var noteId = await SeedNoteAsync();

        var candidates = await service.BuildCandidatesAsync(noteId, "неважно — модель подставная");

        var candidate = Assert.Single(candidates);
        Assert.Equal("Семафор", candidate.Front);
        Assert.Equal("Примитив синхронизации потоков.", candidate.Back);
        Assert.False(candidate.IsLikelyDuplicate);
        Assert.Null(candidate.ExistingCardId);
    }

    [Fact]
    public async Task Parsing_the_same_note_twice_does_not_create_duplicates()
    {
        var service = CreateService(OneCandidateModel());
        var noteId = await SeedNoteAsync();

        var first = await service.BuildCandidatesAsync(noteId, "текст");
        var created = await service.CreateFromCandidatesAsync(noteId, subjectId: null, first);
        Assert.Equal(1, created);

        var second = await service.BuildCandidatesAsync(noteId, "текст");

        var candidate = Assert.Single(second);
        Assert.True(candidate.IsLikelyDuplicate);
        Assert.NotNull(candidate.ExistingCardId);

        // И явная попытка создать снова — тоже без эффекта: дубликаты отфильтровываются на стороне сервиса.
        var createdAgain = await service.CreateFromCandidatesAsync(noteId, subjectId: null, second);
        Assert.Equal(0, createdAgain);
        Assert.Single(await CardRepo.GetAllAsync());
    }

    [Fact]
    public async Task A_similar_card_from_a_different_note_does_not_count_as_a_duplicate()
    {
        // Похожая карточка есть, но у неё другой источник — значит это не «уже разобрано отсюда».
        await SeedCardAsync("Семафор", "Из другой заметки.");

        var service = CreateService(OneCandidateModel());
        var candidates = await service.BuildCandidatesAsync(await SeedNoteAsync(), "текст");

        Assert.False(Assert.Single(candidates).IsLikelyDuplicate);
    }

    [Fact]
    public async Task Created_cards_carry_the_source_note_and_chosen_subject()
    {
        var subjectId = await SeedSubjectAsync();
        var service = CreateService(OneCandidateModel());
        var noteId = await SeedNoteAsync();

        var candidates = await service.BuildCandidatesAsync(noteId, "текст");
        await service.CreateFromCandidatesAsync(noteId, subjectId, candidates);

        var card = Assert.Single(await CardRepo.GetAllAsync());
        Assert.Equal(noteId, card.SourceNoteId);
        Assert.Equal(subjectId, card.SubjectId);
        Assert.Equal(CardKind.Term, card.Kind);
    }

    [Fact]
    public async Task A_document_without_a_recognizable_pattern_yields_no_candidates()
    {
        var service = CreateService(NoCandidatesModel());

        var candidates = await service.BuildCandidatesAsync(Guid.NewGuid(), "текст");

        Assert.Empty(candidates);
    }

    [Fact]
    public async Task Creating_from_an_empty_accepted_list_writes_nothing()
    {
        var service = CreateService(OneCandidateModel());

        var created = await service.CreateFromCandidatesAsync(Guid.NewGuid(), null, []);

        Assert.Equal(0, created);
        Assert.Empty(await CardRepo.GetAllAsync());
    }

    [Fact]
    public async Task Only_the_accepted_non_duplicate_candidates_are_created()
    {
        var service = CreateService(OneCandidateModel());
        var noteId = await SeedNoteAsync();
        var first = await service.BuildCandidatesAsync(noteId, "текст");
        await service.CreateFromCandidatesAsync(noteId, null, first);

        // Пользователь мог не снять чекбокс с уже помеченного дублем кандидата — сервис всё равно
        // не заводит вторую копию.
        var second = await service.BuildCandidatesAsync(noteId, "текст");
        var created = await service.CreateFromCandidatesAsync(noteId, null, second);

        Assert.Equal(0, created);
    }
}
