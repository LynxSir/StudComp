using System.Text;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;

namespace StudComp.Modules.Cards.Services;

/// <summary>Как раскладывать импортируемые записи по существующим карточкам (new_addons.md §7.6).</summary>
public enum CardImportMode
{
    /// <summary>Каждая валидная запись становится новой карточкой.</summary>
    Add = 0,

    /// <summary>
    /// Существующие карточки явно выбранной колоды отвязываются (не удаляются, §3.5), импортированные
    /// получают эту колоду независимо от того, что написано в колонке/поле файла — иначе режим не
    /// был бы детерминированным.
    /// </summary>
    ReplaceDeck = 1,

    /// <summary>Точное совпадение лицевой стороны обновляет оборот и объединяет метки; иначе — новая карточка.</summary>
    Merge = 2,
}

/// <summary>Что решено сделать со строкой импорта.</summary>
public enum CardImportRowAction
{
    Create = 0,
    UpdateExisting = 1,
    Skip = 2,
}

/// <summary>Откуда взять карточки для экспорта (new_addons.md §7.6). Закрытая иерархия — новый вид области не завести снаружи.</summary>
public abstract record CardExportScope
{
    private CardExportScope()
    {
    }

    /// <summary>Явно выбранные карточки — мультивыбор в библиотеке.</summary>
    public sealed record Selection(IReadOnlyList<Guid> CardIds) : CardExportScope;

    /// <summary>Все карточки колоды, включая содержимое умной подборки.</summary>
    public sealed record Deck(Guid DeckId) : CardExportScope;

    /// <summary>Результат текущего поискового запроса библиотеки.</summary>
    public sealed record SearchResult(string Query) : CardExportScope;

    /// <summary>Вся картотека — кнопка «Экспортировать всю картотеку» в настройках.</summary>
    public sealed record All : CardExportScope;
}

/// <summary>Одна строка плана импорта — то, что видит пользователь до записи (new_addons.md §7.6, DoD §12).</summary>
public sealed record CardImportRowPlan(
    int RowNumber,
    CardImportRowAction Action,
    string Front,
    string BackPreview,
    Guid? ExistingCardId,
    string? Reason);

/// <summary>
/// План импорта: то, что показано пользователю, полностью определяет то, что сделает
/// <see cref="ICardImportExportService.CommitImportAsync"/> — сводка «до записи» буквальна, а не
/// задним числом. Конструктор <c>internal</c> — план собирается только внутри <c>Preview*Async</c>,
/// снаружи с ним можно только ознакомиться и передать обратно в <c>CommitImportAsync</c>.
/// </summary>
public sealed class CardImportPlan
{
    internal CardImportPlan(
        CardImportMode mode,
        Guid? targetDeckId,
        IReadOnlyList<CardImportRowPlan> rows,
        IReadOnlyList<string> warnings,
        IReadOnlyList<PreparedDeck> decksToCreate,
        IReadOnlyList<PreparedTag> tagsToEnsure,
        IReadOnlyList<PreparedCardRow> prepared)
    {
        Mode = mode;
        TargetDeckId = targetDeckId;
        Rows = rows;
        Warnings = warnings;
        DecksToCreate = decksToCreate;
        TagsToEnsure = tagsToEnsure;
        Prepared = prepared;
    }

    public CardImportMode Mode { get; }

    public Guid? TargetDeckId { get; }

    /// <summary>По строке на карточку — ровно то, что покажет диалог импорта.</summary>
    public IReadOnlyList<CardImportRowPlan> Rows { get; }

    /// <summary>Предупреждения уровня всего файла (не найден предмет/колода и т.п.).</summary>
    public IReadOnlyList<string> Warnings { get; }

    internal IReadOnlyList<PreparedDeck> DecksToCreate { get; }

    internal IReadOnlyList<PreparedTag> TagsToEnsure { get; }

    internal IReadOnlyList<PreparedCardRow> Prepared { get; }
}

/// <summary>Колода, которую нужно завести при <c>Commit</c> — её <see cref="Id"/> уже выдан на этапе Preview.</summary>
internal sealed record PreparedDeck(
    Guid Id,
    Guid? SubjectId,
    string Name,
    string? Description,
    string? ColorHex,
    int SortOrder,
    string? QueryExpression);

/// <summary>Метка, которую нужно завести с сохранением оформления, прежде чем её встретят карточки.</summary>
internal sealed record PreparedTag(string NormalizedName, string DisplayName, string? ColorHex);

/// <summary>Полностью разобранная строка — тот же смысл, что и <see cref="CardImportRowPlan"/>, но с данными для записи.</summary>
internal sealed record PreparedCardRow(
    CardImportRowAction Action,
    Guid? ExistingCardId,
    Guid? SubjectId,
    Guid? DeckId,
    CardKind Kind,
    string Front,
    string Back,
    string? Hint,
    string? Source,
    CardDifficulty Difficulty,
    bool IsPinned,
    bool IsSuspended,
    DateTimeOffset? CreatedAt,
    IReadOnlyList<string> TagNames);

/// <summary>Итог применения плана — то же самое, что показывал Preview, плюс фактические числа.</summary>
public sealed record CardImportSummary(int Created, int Updated, int Skipped, IReadOnlyList<string> Warnings);

/// <summary>Прогресс применения плана — на границе элементов (new_addons.md §7.6).</summary>
public sealed record CardImportProgress(int Processed, int Total);

/// <summary>Результат «снифа» CSV-файла: с чем предзаполнить диалог импорта.</summary>
public sealed record CardCsvSniffResult(
    Encoding Encoding,
    char Delimiter,
    bool HasHeader,
    IReadOnlyList<IReadOnlyList<string>> PreviewRows);

/// <summary>
/// Обмен картотекой (new_addons.md §7.6): JSON и CSV/TSV, экспорт и двухфазный (Preview → Commit)
/// импорт.
/// </summary>
/// <remarks>
/// «Двухфазность» здесь буквальна: <c>Preview*Async</c> только читает и строит план, ни одной записи
/// в базу; <see cref="CommitImportAsync"/> не открывает файл заново и не переоценивает совпадения —
/// он проигрывает ровно тот план, который был показан пользователю. Иначе «сводка до записи» из
/// §12 DoD была бы фигурой речи, а не гарантией.
/// </remarks>
public interface ICardImportExportService
{
    Task<Result<int>> ExportJsonAsync(string path, CardExportScope scope, CancellationToken ct = default);

    Task<Result<int>> ExportCsvAsync(
        string path,
        CardExportScope scope,
        char delimiter,
        Encoding encoding,
        CancellationToken ct = default);

    Task<Result<CardImportPlan>> PreviewJsonImportAsync(
        string path,
        CardImportMode mode,
        Guid? targetDeckId,
        CancellationToken ct = default);

    /// <summary>
    /// Определить кодировку, разделитель и показать первые строки — до того как пользователь выберет
    /// режим импорта.
    /// </summary>
    Task<Result<CardCsvSniffResult>> SniffCsvAsync(string path, CancellationToken ct = default);

    Task<Result<CardImportPlan>> PreviewCsvImportAsync(
        string path,
        char delimiter,
        Encoding encoding,
        bool hasHeader,
        CardImportMode mode,
        Guid? targetDeckId,
        CancellationToken ct = default);

    /// <summary>Применить план. Отмена — на границе строк: уже применённые остаются, недописанных не бывает.</summary>
    Task<Result<CardImportSummary>> CommitImportAsync(
        CardImportPlan plan,
        IProgress<CardImportProgress>? progress = null,
        CancellationToken ct = default);
}

internal sealed class CardImportExportService(
    ICardRepository cards,
    ICardDeckRepository decks,
    ICardTagRepository tagLinks,
    ICardTagService tagService,
    ISubjectRepository subjects,
    ICardService cardService,
    IFileSystem fileSystem) : ICardImportExportService
{
    private const int SearchScopeLimit = 5000;
    private const int BackPreviewLength = 80;
    private const int FrontMentionLength = 60;

    static CardImportExportService()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public async Task<Result<int>> ExportJsonAsync(
        string path,
        CardExportScope scope,
        CancellationToken ct = default)
    {
        Guard.NotNullOrWhiteSpace(path);

        var resolved = await ResolveScopeAsync(scope, ct).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return Result<int>.Failure(resolved.Error);
        }

        var (liveCards, explicitDeckId) = resolved.Value;
        var payload = await BuildPayloadAsync(liveCards, explicitDeckId, ct).ConfigureAwait(false);
        var json = CardsJson.Serialize(payload);

        try
        {
            await using var stream = fileSystem.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(json.AsMemory(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<int>.Failure("cards.export_failed", $"Не удалось записать файл: {ex.Message}");
        }

        return Result<int>.Success(payload.Cards.Count);
    }

    public async Task<Result<int>> ExportCsvAsync(
        string path,
        CardExportScope scope,
        char delimiter,
        Encoding encoding,
        CancellationToken ct = default)
    {
        Guard.NotNullOrWhiteSpace(path);
        Guard.NotNull(encoding);

        var resolved = await ResolveScopeAsync(scope, ct).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return Result<int>.Failure(resolved.Error);
        }

        var (liveCards, _) = resolved.Value;
        var allDecks = (await decks.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(d => d.Id);
        var allTags = (await tagLinks.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(t => t.Id);
        var cardIds = liveCards.Select(c => c.Id).ToList();
        var links = cardIds.Count == 0
            ? []
            : await tagLinks.GetLinksAsync(cardIds, ct).ConfigureAwait(false);
        var tagsByCard = links.GroupBy(l => l.CardId).ToDictionary(g => g.Key, g => g.Select(l => l.TagId).ToList());

        var text = new StringBuilder();
        text.Append(string.Join(delimiter, "Front", "Back", "Tags", "Deck")).Append("\r\n");

        foreach (var card in liveCards)
        {
            var tagNames = tagsByCard.TryGetValue(card.Id, out var tagIds)
                ? string.Join(',', tagIds.Select(id => allTags.TryGetValue(id, out var tag) ? tag.DisplayName : null).OfType<string>())
                : string.Empty;
            var deckName = card.DeckId is { } deckId && allDecks.TryGetValue(deckId, out var deck) ? deck.Name : string.Empty;

            text.Append(CsvField(card.Front, delimiter)).Append(delimiter)
                .Append(CsvField(card.Back, delimiter)).Append(delimiter)
                .Append(CsvField(tagNames, delimiter)).Append(delimiter)
                .Append(CsvField(deckName, delimiter))
                .Append("\r\n");
        }

        try
        {
            await using var stream = fileSystem.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var writer = new StreamWriter(stream, encoding);
            await writer.WriteAsync(text.ToString().AsMemory(), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<int>.Failure("cards.export_failed", $"Не удалось записать файл: {ex.Message}");
        }

        return Result<int>.Success(liveCards.Count);
    }

    public async Task<Result<CardImportPlan>> PreviewJsonImportAsync(
        string path,
        CardImportMode mode,
        Guid? targetDeckId,
        CancellationToken ct = default)
    {
        Guard.NotNullOrWhiteSpace(path);

        var deckCheck = CheckTargetDeck(mode, targetDeckId);
        if (deckCheck.IsFailure)
        {
            return Result<CardImportPlan>.Failure(deckCheck.Error);
        }

        string json;
        try
        {
            using var stream = fileSystem.OpenRead(path);
            using var reader = new StreamReader(stream);
            json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<CardImportPlan>.Failure("cards.import_failed", $"Не удалось прочитать файл: {ex.Message}");
        }

        var parsed = CardsJson.Deserialize(json);
        if (parsed.IsFailure)
        {
            return Result<CardImportPlan>.Failure(parsed.Error);
        }

        var candidates = parsed.Value.Cards
            .Select(dto => new ImportCandidate(
                dto.SubjectName,
                dto.DeckName,
                dto.Kind,
                dto.Front,
                dto.Back,
                dto.Hint,
                dto.Source,
                dto.IsPinned,
                dto.IsSuspended,
                dto.Difficulty,
                dto.CreatedAt,
                dto.Tags,
                SkipReason: null))
            .ToList();

        var plan = await BuildPlanAsync(candidates, parsed.Value.Decks, parsed.Value.Tags, mode, targetDeckId, ct)
            .ConfigureAwait(false);

        return Result<CardImportPlan>.Success(plan);
    }

    public async Task<Result<CardCsvSniffResult>> SniffCsvAsync(string path, CancellationToken ct = default)
    {
        Guard.NotNullOrWhiteSpace(path);

        var read = await ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        if (read.IsFailure)
        {
            return Result<CardCsvSniffResult>.Failure(read.Error);
        }

        var bytes = read.Value;
        var encoding = DetectEncoding(bytes, out var preambleLength);
        var text = encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
        var delimiter = DetectDelimiter(text);

        var parsed = CardCsvParser.Parse(text, delimiter, hasHeader: false);
        var previewRows = parsed.Rows.Take(10).Select(r => r.Fields).ToList();
        var hasHeader = previewRows.Count > 0 && LooksLikeHeader(previewRows[0]);

        return Result<CardCsvSniffResult>.Success(
            new CardCsvSniffResult(encoding, delimiter, hasHeader, previewRows));
    }

    public async Task<Result<CardImportPlan>> PreviewCsvImportAsync(
        string path,
        char delimiter,
        Encoding encoding,
        bool hasHeader,
        CardImportMode mode,
        Guid? targetDeckId,
        CancellationToken ct = default)
    {
        Guard.NotNullOrWhiteSpace(path);
        Guard.NotNull(encoding);

        var deckCheck = CheckTargetDeck(mode, targetDeckId);
        if (deckCheck.IsFailure)
        {
            return Result<CardImportPlan>.Failure(deckCheck.Error);
        }

        var read = await ReadAllBytesAsync(path, ct).ConfigureAwait(false);
        if (read.IsFailure)
        {
            return Result<CardImportPlan>.Failure(read.Error);
        }

        var bytes = read.Value;
        var skip = HasPreamble(bytes, encoding) ? encoding.GetPreamble().Length : 0;
        var text = encoding.GetString(bytes, skip, bytes.Length - skip);

        var parsedCsv = CardCsvParser.Parse(text, delimiter, hasHeader);
        var columns = ResolveColumns(parsedCsv.Header);
        var candidates = new List<ImportCandidate>();

        foreach (var row in parsedCsv.Rows)
        {
            if (row.HasUnterminatedQuote)
            {
                candidates.Add(new ImportCandidate(
                    null, null, CardKind.Term, Field(row, columns.Front), string.Empty, null, null,
                    false, false, CardDifficulty.Normal, null, [],
                    SkipReason: "Не закрыта кавычка — остаток файла не разобран."));
                break;
            }

            var tags = Field(row, columns.Tags)
                .Split(',')
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .ToList();

            var deckField = Field(row, columns.Deck);

            candidates.Add(new ImportCandidate(
                SubjectName: null,
                DeckName: deckField.Length > 0 ? deckField : null,
                Kind: CardKind.Term,
                Front: Field(row, columns.Front),
                Back: Field(row, columns.Back),
                Hint: null,
                Source: null,
                IsPinned: false,
                IsSuspended: false,
                Difficulty: CardDifficulty.Normal,
                CreatedAt: null,
                Tags: tags,
                SkipReason: null));
        }

        var plan = await BuildPlanAsync(candidates, [], [], mode, targetDeckId, ct).ConfigureAwait(false);
        return Result<CardImportPlan>.Success(plan);
    }

    public async Task<Result<CardImportSummary>> CommitImportAsync(
        CardImportPlan plan,
        IProgress<CardImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        Guard.NotNull(plan);

        foreach (var deck in plan.DecksToCreate)
        {
            var now = DateTimeOffset.Now;
            await decks.AddAsync(
                new CardDeck
                {
                    Id = deck.Id,
                    SubjectId = deck.SubjectId,
                    Name = deck.Name,
                    Description = deck.Description,
                    ColorHex = deck.ColorHex,
                    SortOrder = deck.SortOrder,
                    QueryExpression = deck.QueryExpression,
                    CreatedAt = now,
                    UpdatedAt = now,
                },
                ct).ConfigureAwait(false);
        }

        foreach (var tag in plan.TagsToEnsure)
        {
            if (await tagLinks.GetByNameAsync(tag.NormalizedName, ct).ConfigureAwait(false) is not null)
            {
                continue;
            }

            await tagLinks.AddAsync(
                new CardTag
                {
                    Id = Guid.NewGuid(),
                    Name = tag.NormalizedName,
                    DisplayName = string.IsNullOrWhiteSpace(tag.DisplayName) ? tag.NormalizedName : tag.DisplayName,
                    ColorHex = tag.ColorHex,
                    CreatedAt = DateTimeOffset.Now,
                },
                ct).ConfigureAwait(false);
        }

        if (plan.Mode == CardImportMode.ReplaceDeck && plan.TargetDeckId is { } targetDeckId)
        {
            // Существующие карточки целевой колоды отвязываются, а не удаляются (дух §3.5) — место
            // им освобождает импорт, а не необратимая зачистка.
            var existingInTarget = await cards.GetByDeckAsync(targetDeckId, ct).ConfigureAwait(false);
            if (existingInTarget.Count > 0)
            {
                await cardService
                    .AssignDeckAsync(existingInTarget.Select(c => c.Id).ToList(), null, ct)
                    .ConfigureAwait(false);
            }
        }

        var created = 0;
        var updated = 0;
        var skipped = plan.Rows.Count(r => r.Action == CardImportRowAction.Skip);
        var total = plan.Prepared.Count;
        var processed = 0;

        foreach (var row in plan.Prepared)
        {
            ct.ThrowIfCancellationRequested();

            if (row.Action == CardImportRowAction.Create)
            {
                var card = new Card
                {
                    SubjectId = row.SubjectId,
                    DeckId = row.DeckId,
                    Kind = row.Kind,
                    Front = row.Front,
                    Back = row.Back,
                    Hint = row.Hint,
                    Source = row.Source,
                    Difficulty = row.Difficulty,
                    IsPinned = row.IsPinned,
                    IsSuspended = row.IsSuspended,
                    CreatedAt = row.CreatedAt ?? default,
                };

                var result = await cardService.CreateAsync(card, row.TagNames, ct).ConfigureAwait(false);
                if (result.IsSuccess)
                {
                    created++;
                }
            }
            else if (row.Action == CardImportRowAction.UpdateExisting && row.ExistingCardId is { } existingId)
            {
                // «Объединить» трогает только оборот и метки — лицо, предмет, колода и вид карточки
                // остаются как есть у существующей записи (new_addons.md §7.6).
                var existing = await cards.GetByIdAsync(existingId, ct).ConfigureAwait(false);
                if (existing is not null)
                {
                    await cardService.UpdateContentAsync(
                        existingId,
                        existing.Front,
                        row.Back,
                        existing.Hint,
                        existing.Source,
                        existing.Kind,
                        existing.Difficulty,
                        existing.SubjectId,
                        existing.DeckId,
                        ct).ConfigureAwait(false);

                    if (row.TagNames.Count > 0)
                    {
                        var ensured = await tagService.EnsureManyAsync(row.TagNames, ct).ConfigureAwait(false);
                        foreach (var tag in ensured)
                        {
                            await tagLinks.AddLinksAsync([existingId], tag.Id, ct).ConfigureAwait(false);
                        }

                        await tagService.RecalculateUsageAsync(ct).ConfigureAwait(false);
                    }

                    updated++;
                }
            }

            processed++;
            progress?.Report(new CardImportProgress(processed, total));
        }

        return Result<CardImportSummary>.Success(new CardImportSummary(created, updated, skipped, plan.Warnings));
    }

    private static Result CheckTargetDeck(CardImportMode mode, Guid? targetDeckId) =>
        mode == CardImportMode.ReplaceDeck && targetDeckId is null
            ? Result.Failure("cards.import_target_deck_required", "Выберите колоду, которую нужно заменить.")
            : Result.Success();

    private async Task<Result<(IReadOnlyList<Card> Cards, Guid? ExplicitDeckId)>> ResolveScopeAsync(
        CardExportScope scope,
        CancellationToken ct)
    {
        switch (scope)
        {
            case CardExportScope.Selection selection:
                return Result<(IReadOnlyList<Card>, Guid?)>.Success(
                    (await cards.GetByIdsAsync(selection.CardIds, ct).ConfigureAwait(false), null));

            case CardExportScope.Deck deckScope:
            {
                var deck = await decks.GetByIdAsync(deckScope.DeckId, ct).ConfigureAwait(false);
                if (deck is null)
                {
                    return Result<(IReadOnlyList<Card>, Guid?)>.Failure("cards.deck_not_found", "Колода не найдена.");
                }

                var deckCards = string.IsNullOrWhiteSpace(deck.QueryExpression)
                    ? await cards.GetByDeckAsync(deckScope.DeckId, ct).ConfigureAwait(false)
                    : await cardService
                        .SearchAsync(deck.QueryExpression, new CardSearchOptions(Limit: SearchScopeLimit), ct)
                        .ConfigureAwait(false);

                return Result<(IReadOnlyList<Card>, Guid?)>.Success((deckCards, deckScope.DeckId));
            }

            case CardExportScope.SearchResult search:
                return Result<(IReadOnlyList<Card>, Guid?)>.Success(
                    (await cardService
                        .SearchAsync(search.Query, new CardSearchOptions(Limit: SearchScopeLimit), ct)
                        .ConfigureAwait(false), null));

            case CardExportScope.All:
                return Result<(IReadOnlyList<Card>, Guid?)>.Success(
                    (await cards.GetAllAsync(ct).ConfigureAwait(false), null));

            default:
                return Result<(IReadOnlyList<Card>, Guid?)>.Failure(
                    "cards.export_scope_invalid", "Неизвестная область экспорта.");
        }
    }

    private async Task<CardsExportPayload> BuildPayloadAsync(
        IReadOnlyList<Card> liveCards,
        Guid? explicitDeckId,
        CancellationToken ct)
    {
        var subjectNames = (await subjects.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(s => s.Id, s => s.Name);
        var allDecks = (await decks.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(d => d.Id);
        var allTags = (await tagLinks.GetAllAsync(ct).ConfigureAwait(false)).ToDictionary(t => t.Id);

        var cardIds = liveCards.Select(c => c.Id).ToList();
        var links = cardIds.Count == 0
            ? []
            : await tagLinks.GetLinksAsync(cardIds, ct).ConfigureAwait(false);
        var tagsByCard = links.GroupBy(l => l.CardId).ToDictionary(g => g.Key, g => g.Select(l => l.TagId).ToList());

        var deckIds = new HashSet<Guid>(liveCards.Where(c => c.DeckId is not null).Select(c => c.DeckId!.Value));
        if (explicitDeckId is { } id)
        {
            deckIds.Add(id);
        }

        var tagIds = new HashSet<Guid>(links.Select(l => l.TagId));

        var cardDtos = liveCards
            .Select(card => new CardExportDto
            {
                Id = card.Id,
                SubjectName = card.SubjectId is { } subjectId && subjectNames.TryGetValue(subjectId, out var subjectName)
                    ? subjectName
                    : null,
                DeckName = card.DeckId is { } deckId && allDecks.TryGetValue(deckId, out var deck) ? deck.Name : null,
                Kind = card.Kind,
                Front = card.Front,
                Back = card.Back,
                Hint = card.Hint,
                Source = card.Source,
                IsPinned = card.IsPinned,
                IsSuspended = card.IsSuspended,
                Difficulty = card.Difficulty,
                Tags = tagsByCard.TryGetValue(card.Id, out var tagIdsForCard)
                    ? tagIdsForCard
                        .Select(tagId => allTags.TryGetValue(tagId, out var tag) ? tag.Name : null)
                        .OfType<string>()
                        .OrderBy(x => x, StringComparer.Ordinal)
                        .ToList()
                    : [],
                CreatedAt = card.CreatedAt,
                UpdatedAt = card.UpdatedAt,
            })
            .ToList();

        var deckDtos = deckIds
            .Select(allDecks.GetValueOrDefault)
            .OfType<CardDeck>()
            .Select(deck => new DeckExportDto
            {
                Name = deck.Name,
                SubjectName = deck.SubjectId is { } subjectId && subjectNames.TryGetValue(subjectId, out var subjectName)
                    ? subjectName
                    : null,
                Description = deck.Description,
                ColorHex = deck.ColorHex,
                SortOrder = deck.SortOrder,
                QueryExpression = deck.QueryExpression,
            })
            .ToList();

        var tagDtos = tagIds
            .Select(allTags.GetValueOrDefault)
            .OfType<CardTag>()
            .Select(tag => new TagExportDto { Name = tag.Name, DisplayName = tag.DisplayName, ColorHex = tag.ColorHex })
            .ToList();

        return new CardsExportPayload(cardDtos, deckDtos, tagDtos);
    }

    /// <summary>
    /// Общее ядро Preview для JSON и CSV: резолв предметов/колод/меток, поиск совпадений для
    /// «Объединить» — всё только на чтение, план собирается в памяти.
    /// </summary>
    private async Task<CardImportPlan> BuildPlanAsync(
        IReadOnlyList<ImportCandidate> candidates,
        IReadOnlyList<DeckExportDto> deckDtos,
        IReadOnlyList<TagExportDto> tagDtos,
        CardImportMode mode,
        Guid? targetDeckId,
        CancellationToken ct)
    {
        var warnings = new List<string>();

        var subjectByName = (await subjects.GetAllAsync(ct).ConfigureAwait(false))
            .GroupBy(s => s.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Id, StringComparer.OrdinalIgnoreCase);

        var existingDecks = await decks.GetAllAsync(ct).ConfigureAwait(false);
        var deckIdsByName = existingDecks
            .GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        // Колоды из конверта заводим при Commit, если такой ещё нет — так «Объединить»/«Добавить»
        // восстанавливают структуру колод на чистой базе (round-trip). «Заменить колоду» игнорирует
        // список конверта целиком: цель уже выбрана явно.
        var decksToCreate = new List<PreparedDeck>();
        var decksToCreateByName = new Dictionary<string, PreparedDeck>(StringComparer.OrdinalIgnoreCase);
        if (mode != CardImportMode.ReplaceDeck)
        {
            foreach (var dto in deckDtos)
            {
                var name = dto.Name.Trim();
                if (name.Length == 0 || deckIdsByName.ContainsKey(name) || decksToCreateByName.ContainsKey(name))
                {
                    continue;
                }

                var subjectId = string.IsNullOrWhiteSpace(dto.SubjectName)
                    ? (Guid?)null
                    : subjectByName.GetValueOrDefault(dto.SubjectName.Trim());

                var preparedDeck = new PreparedDeck(
                    Guid.NewGuid(), subjectId, name, dto.Description, dto.ColorHex, dto.SortOrder, dto.QueryExpression);
                decksToCreate.Add(preparedDeck);
                decksToCreateByName[name] = preparedDeck;
            }
        }

        Guid? ResolveDeck(string? deckName)
        {
            if (mode == CardImportMode.ReplaceDeck)
            {
                return targetDeckId;
            }

            var trimmed = deckName?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return null;
            }

            if (decksToCreateByName.TryGetValue(trimmed, out var toCreate))
            {
                return toCreate.Id;
            }

            return deckIdsByName.TryGetValue(trimmed, out var found) && found.Count > 0 ? found[0].Id : null;
        }

        // Метки конверта — ensure с исходным оформлением на Commit, до того как их встретят карточки
        // (обычный EnsureManyAsync создал бы их без цвета).
        var tagsToEnsure = tagDtos
            .Select(dto => (Normalized: ICardTagService.Normalize(dto.Name), Dto: dto))
            .Where(x => x.Normalized.Length > 0)
            .GroupBy(x => x.Normalized, StringComparer.Ordinal)
            .Select(g => new PreparedTag(g.Key, g.First().Dto.DisplayName, g.First().Dto.ColorHex))
            .ToList();

        IReadOnlyList<Card> mergePool = [];
        if (mode == CardImportMode.Merge)
        {
            mergePool = targetDeckId is { } deckScope
                ? await cards.GetByDeckAsync(deckScope, ct).ConfigureAwait(false)
                : await cards.GetAllAsync(ct).ConfigureAwait(false);
        }

        var mergeByFront = mergePool
            .GroupBy(c => NormalizeFront(c.Front), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        if (mode == CardImportMode.ReplaceDeck)
        {
            warnings.Add(
                "Импортированные карточки получат выбранную колоду независимо от того, что указано в файле.");
        }

        var rows = new List<CardImportRowPlan>();
        var prepared = new List<PreparedCardRow>();
        var rowNumber = 0;

        foreach (var candidate in candidates)
        {
            rowNumber++;

            if (candidate.SkipReason is { } badReason)
            {
                rows.Add(new CardImportRowPlan(rowNumber, CardImportRowAction.Skip, candidate.Front, string.Empty, null, badReason));
                continue;
            }

            if (string.IsNullOrWhiteSpace(candidate.Front))
            {
                rows.Add(new CardImportRowPlan(rowNumber, CardImportRowAction.Skip, candidate.Front, string.Empty, null, "Пустая лицевая сторона."));
                continue;
            }

            Guid? subjectId = null;
            if (!string.IsNullOrWhiteSpace(candidate.SubjectName))
            {
                if (subjectByName.TryGetValue(candidate.SubjectName.Trim(), out var foundSubjectId))
                {
                    subjectId = foundSubjectId;
                }
                else
                {
                    warnings.Add($"Предмет «{candidate.SubjectName}» не найден — карточка «{Truncate(candidate.Front, FrontMentionLength)}» без предмета.");
                }
            }

            var deckId = ResolveDeck(candidate.DeckName);
            if (deckId is null && mode != CardImportMode.ReplaceDeck && !string.IsNullOrWhiteSpace(candidate.DeckName))
            {
                warnings.Add($"Колода «{candidate.DeckName}» не найдена — карточка «{Truncate(candidate.Front, FrontMentionLength)}» без колоды.");
            }

            var backPreview = Truncate(candidate.Back, BackPreviewLength);

            if (mode == CardImportMode.Merge && mergeByFront.TryGetValue(NormalizeFront(candidate.Front), out var existing))
            {
                rows.Add(new CardImportRowPlan(
                    rowNumber, CardImportRowAction.UpdateExisting, candidate.Front, backPreview, existing.Id,
                    "Совпала лицевая сторона — оборот и метки будут объединены с уже существующей карточкой."));
                prepared.Add(new PreparedCardRow(
                    CardImportRowAction.UpdateExisting, existing.Id, subjectId, deckId, candidate.Kind,
                    candidate.Front, candidate.Back, candidate.Hint, candidate.Source, candidate.Difficulty,
                    candidate.IsPinned, candidate.IsSuspended, candidate.CreatedAt, candidate.Tags));
                continue;
            }

            rows.Add(new CardImportRowPlan(rowNumber, CardImportRowAction.Create, candidate.Front, backPreview, null, null));
            prepared.Add(new PreparedCardRow(
                CardImportRowAction.Create, null, subjectId, deckId, candidate.Kind,
                candidate.Front, candidate.Back, candidate.Hint, candidate.Source, candidate.Difficulty,
                candidate.IsPinned, candidate.IsSuspended, candidate.CreatedAt, candidate.Tags));
        }

        return new CardImportPlan(mode, targetDeckId, rows, warnings, decksToCreate, tagsToEnsure, prepared);
    }

    private async Task<Result<byte[]>> ReadAllBytesAsync(string path, CancellationToken ct)
    {
        try
        {
            using var stream = fileSystem.OpenRead(path);
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, ct).ConfigureAwait(false);
            return Result<byte[]>.Success(buffer.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Result<byte[]>.Failure("cards.import_failed", $"Не удалось прочитать файл: {ex.Message}");
        }
    }

    private static bool HasPreamble(byte[] bytes, Encoding encoding)
    {
        var preamble = encoding.GetPreamble();
        if (preamble.Length == 0 || bytes.Length < preamble.Length)
        {
            return false;
        }

        for (var i = 0; i < preamble.Length; i++)
        {
            if (bytes[i] != preamble[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// BOM → однозначно; без BOM — строгая попытка UTF-8, а при первом же неразборчивом байте откат
    /// на Windows-1251 (new_addons.md §7.6). Ни разу не бросает наружу.
    /// </summary>
    private static Encoding DetectEncoding(byte[] bytes, out int preambleLength)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            preambleLength = 3;

            // «true» здесь не «писать BOM при экспорте», а «эта кодировка сама знает свой преамбул»:
            // от неё зависит HasPreamble/GetPreamble() при повторном чтении того же файла в
            // Preview*Async — с UTF8Encoding(false) он вернул бы пустой преамбул, и три байта BOM
            // остались бы в начале первого поля вместо того, чтобы быть пропущенными.
            return new UTF8Encoding(true);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            preambleLength = 2;
            return Encoding.Unicode;
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            preambleLength = 2;
            return Encoding.BigEndianUnicode;
        }

        preambleLength = 0;
        try
        {
            new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return new UTF8Encoding(false);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1251);
        }
    }

    private static char DetectDelimiter(string text)
    {
        var firstLineEnd = text.IndexOfAny(['\r', '\n']);
        var firstLine = firstLineEnd < 0 ? text : text[..firstLineEnd];

        var best = ';';
        var bestCount = -1;
        foreach (var candidate in new[] { ';', '\t', ',' })
        {
            var count = firstLine.Count(c => c == candidate);
            if (count > bestCount)
            {
                bestCount = count;
                best = candidate;
            }
        }

        return best;
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> fields) =>
        fields.Any(f => string.Equals(f.Trim(), "Front", StringComparison.OrdinalIgnoreCase));

    private readonly record struct CsvColumns(int Front, int Back, int Tags, int Deck);

    private static CsvColumns ResolveColumns(IReadOnlyList<string>? header)
    {
        if (header is null)
        {
            return new CsvColumns(0, 1, 2, 3);
        }

        int IndexOf(string name) => header
            .Select((h, i) => (h, i))
            .Where(x => string.Equals(x.h.Trim(), name, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.i)
            .DefaultIfEmpty(-1)
            .First();

        return new CsvColumns(IndexOf("Front"), IndexOf("Back"), IndexOf("Tags"), IndexOf("Deck"));
    }

    private static string Field(CardCsvRow row, int index) =>
        index >= 0 && index < row.Fields.Count ? row.Fields[index].Trim() : string.Empty;

    private static string CsvField(string? value, char delimiter)
    {
        var text = value ?? string.Empty;
        var needsQuote = text.IndexOfAny([delimiter, '"', '\r', '\n']) >= 0;
        return needsQuote ? $"\"{text.Replace("\"", "\"\"")}\"" : text;
    }

    /// <summary>Складывание регистра, «ё» с «е» и схлопывание пробелов — точное совпадение для «Объединить».</summary>
    private static string NormalizeFront(string front)
    {
        var trimmed = string.Join(
            ' ',
            (front ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return trimmed.ToLowerInvariant().Replace('ё', 'е');
    }

    private static string Truncate(string? text, int max)
    {
        var value = (text ?? string.Empty).Trim();
        return value.Length > max ? value[..max] + "…" : value;
    }

    /// <summary>Промежуточная строка импорта — общий вид для JSON- и CSV-источников.</summary>
    private sealed record ImportCandidate(
        string? SubjectName,
        string? DeckName,
        CardKind Kind,
        string Front,
        string Back,
        string? Hint,
        string? Source,
        bool IsPinned,
        bool IsSuspended,
        CardDifficulty Difficulty,
        DateTimeOffset? CreatedAt,
        IReadOnlyList<string> Tags,
        string? SkipReason);
}
