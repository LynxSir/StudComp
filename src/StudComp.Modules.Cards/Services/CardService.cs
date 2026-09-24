using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.Cards.Services;

/// <summary>
/// Строка выдачи поиска: сама карточка плюс то, что о ней знает индекс (new_addons.md §4.5).
/// </summary>
/// <param name="Card">Найденная карточка.</param>
/// <param name="Snippet">
/// Отрывок оборота с маркерами <see cref="CardHighlight"/> вокруг совпадений; пусто, если индекс
/// отрывка не дал (запрос без полнотекстовой части либо деградированный режим §4.3).
/// </param>
/// <param name="Score">Балл релевантности: меньше — релевантнее, как у bm25.</param>
public sealed record CardSearchResult(Card Card, string Snippet, double Score);

/// <summary>
/// Результат разрешения wiki-ссылки <c>[[Название]]</c> (new_addons.md §7). Точное совпадение — сразу
/// переход; иначе <see cref="Candidates"/> отдаёт похожие для маленького меню выбора в UI (пусто — ни
/// одной похожей).
/// </summary>
public sealed record CardLinkResolution(Guid? ExactCardId, IReadOnlyList<Card> Candidates);

/// <summary>
/// Карточки знаний (new_addons.md §3.1, §3.5, §7.1): CRUD, корзина, массовые операции и проверка
/// на дубликат.
/// </summary>
/// <remarks>
/// «Удалить» здесь <b>никогда</b> не сносит строку: карточка это невосполнимый пользовательский
/// текст, поэтому она уезжает в «Корзину» и возвращается оттуда без потерь. Единственная
/// необратимая операция — <see cref="EmptyTrashAsync"/>, и подтверждение перед ней спрашивает UI.
/// </remarks>
public interface ICardService
{
    Task<Card?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Карточки по списку идентификаторов — итоги сессии, очередь дня, «слабые места».</summary>
    Task<IReadOnlyList<Card>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);

    Task<IReadOnlyList<Card>> GetRecentAsync(int take, CancellationToken ct = default);

    Task<IReadOnlyList<Card>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    Task<IReadOnlyList<Card>> GetDeletedAsync(int take, CancellationToken ct = default);

    Task<int> CountBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    Task<int> CountDeletedAsync(CancellationToken ct = default);

    /// <summary>
    /// Поиск по строке запроса: разбор — чистой функцией <see cref="CardQuery"/>, выполнение —
    /// поисковым индексом. Возвращает карточки в порядке релевантности.
    /// </summary>
    Task<IReadOnlyList<Card>> SearchAsync(
        string query,
        CardSearchOptions options,
        CancellationToken ct = default);

    /// <summary>
    /// То же, что <see cref="SearchAsync"/>, но с отрывками и баллами: библиотеке нужна подсветка
    /// совпадений, а не только сами карточки.
    /// </summary>
    Task<IReadOnlyList<CardSearchResult>> SearchDetailedAsync(
        string query,
        CardSearchOptions options,
        CancellationToken ct = default);

    /// <summary>Работает ли полнотекстовый индекс (new_addons.md §4.3).</summary>
    Task<bool> IsSearchIndexHealthyAsync(CancellationToken ct = default);

    /// <summary>Сколько карточек пора повторить — бейдж сайдбара и зона Дашборда.</summary>
    Task<int> CountDueAsync(CancellationToken ct = default);

    /// <summary>Живые карточки по предметам — счётчики рельса фильтров, одним запросом.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountsBySubjectAsync(CancellationToken ct = default);

    /// <summary>Живые карточки по колодам — тем же одним запросом.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountsByDeckAsync(CancellationToken ct = default);

    /// <summary>
    /// «Карточка дня» для Дашборда: сначала из тех, что уже забывали, иначе из недавних. Выбор
    /// детерминирован датой — за один день карточка не меняется, и это не случайность, а правило.
    /// </summary>
    Task<Card?> GetCardOfTheDayAsync(DateOnly day, CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(
        Card card,
        IReadOnlyList<string>? tagNames = null,
        CancellationToken ct = default);

    Task<Result> UpdateAsync(
        Card card,
        IReadOnlyList<string>? tagNames = null,
        CancellationToken ct = default);

    /// <summary>
    /// Сохранить только содержимое карточки — под автосохранение панели просмотра. Метки не
    /// трогаются: их пишет <see cref="SetTagsAsync"/>, когда меняются чипы, а не каждое нажатие
    /// клавиши (зеркало <c>INoteService.UpdateContentAsync</c> из 12.3).
    /// </summary>
    Task<Result> UpdateContentAsync(
        Guid id,
        string front,
        string back,
        string? hint,
        string? source,
        CardKind kind,
        CardDifficulty difficulty,
        Guid? subjectId,
        Guid? deckId,
        CancellationToken ct = default);

    /// <summary>Заменить набор меток карточки — недостающие метки заводятся на лету.</summary>
    Task<Result> SetTagsAsync(Guid id, IReadOnlyList<string> tagNames, CancellationToken ct = default);

    Task<Result> SetPinnedAsync(Guid id, bool pinned, CancellationToken ct = default);

    Task<Result> SetSuspendedAsync(Guid id, bool suspended, CancellationToken ct = default);

    /// <summary>Массово закрепить или открепить — панель массовых операций.</summary>
    Task<Result<int>> SetPinnedManyAsync(IReadOnlyList<Guid> ids, bool pinned, CancellationToken ct = default);

    /// <summary>Массово отложить или вернуть в тренировки.</summary>
    Task<Result<int>> SetSuspendedManyAsync(IReadOnlyList<Guid> ids, bool suspended, CancellationToken ct = default);

    /// <summary>Навесить метки на пачку карточек; уже висящие пропускаются.</summary>
    Task<Result<int>> AddTagsAsync(
        IReadOnlyList<Guid> ids,
        IReadOnlyList<string> tagNames,
        CancellationToken ct = default);

    /// <summary>Снять метки с пачки карточек. Сами метки при этом остаются в картотеке.</summary>
    Task<Result<int>> RemoveTagsAsync(
        IReadOnlyList<Guid> ids,
        IReadOnlyList<string> tagNames,
        CancellationToken ct = default);

    /// <summary>Отправить в «Корзину». Возвращает, сколько карточек уехало.</summary>
    Task<Result<int>> MoveToTrashAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);

    /// <summary>Вернуть из «Корзины».</summary>
    Task<Result<int>> RestoreAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);

    /// <summary>Очистить «Корзину» — единственная необратимая операция над карточками.</summary>
    Task<Result<int>> EmptyTrashAsync(CancellationToken ct = default);

    /// <summary>
    /// Ретеншн корзины (new_addons.md §3.5, §11): вычистить только то, что удалено раньше
    /// <paramref name="retention"/> назад — в отличие от <see cref="EmptyTrashAsync"/>, который чистит
    /// корзину целиком по явному нажатию.
    /// </summary>
    Task<int> PurgeExpiredTrashAsync(TimeSpan retention, CancellationToken ct = default);

    /// <summary>
    /// Найти карточку по названию для wiki-ссылки <c>[[…]]</c> (new_addons.md §7, объём Phase 12.9):
    /// сначала точное совпадение лицевой стороны, иначе — похожие через <see cref="FindSimilarAsync"/>.
    /// </summary>
    Task<CardLinkResolution> ResolveWikiLinkAsync(string targetFront, CancellationToken ct = default);

    /// <summary>Массово сменить предмет; <see langword="null"/> — снять привязку.</summary>
    Task<Result<int>> AssignSubjectAsync(
        IReadOnlyList<Guid> ids,
        Guid? subjectId,
        CancellationToken ct = default);

    /// <summary>Массово сменить колоду; <see langword="null"/> — вынуть из колоды.</summary>
    Task<Result<int>> AssignDeckAsync(
        IReadOnlyList<Guid> ids,
        Guid? deckId,
        CancellationToken ct = default);

    /// <summary>
    /// Похожие карточки — плашка «такая уже есть» в форме. Ищет по индексу и сравнивает лицевые
    /// стороны коэффициентом Дайса на уже существующем <see cref="TokenSimilarity"/>.
    /// </summary>
    Task<IReadOnlyList<Card>> FindSimilarAsync(
        string front,
        Guid? excludeId = null,
        CancellationToken ct = default);
}

internal sealed class CardService(
    ICardRepository cards,
    ICardDeckRepository decks,
    ICardTagRepository tagLinks,
    ICardTagService tagService,
    ICardSearchRepository search,
    ISubjectRepository subjects) : ICardService
{
    private const int MaxFrontLength = 500;
    private const int MaxHintLength = 1000;
    private const int MaxSourceLength = 300;

    /// <summary>Порог «это та же карточка» — тот же, что у проверки дубликатов в §7.1.</summary>
    private const double DuplicateThreshold = 0.8;

    /// <summary>Пресет «трудные» из языка запросов — из него набирается «карточка дня».</summary>
    private const string HardPreset = "трудные";

    /// <summary>Размер выборки, из которой выбирается «карточка дня».</summary>
    private const int CardOfTheDayPool = 50;

    public Task<Card?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        cards.GetByIdAsync(id, ct);

    public Task<IReadOnlyList<Card>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default) =>
        cards.GetByIdsAsync(ids, ct);

    public Task<IReadOnlyList<Card>> GetRecentAsync(int take, CancellationToken ct = default) =>
        cards.GetRecentAsync(take <= 0 ? 1 : take, ct);

    public Task<IReadOnlyList<Card>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default) =>
        cards.GetBySubjectAsync(subjectId, ct);

    public Task<IReadOnlyList<Card>> GetDeletedAsync(int take, CancellationToken ct = default) =>
        cards.GetDeletedAsync(take <= 0 ? 1 : take, ct);

    public Task<int> CountBySubjectAsync(Guid subjectId, CancellationToken ct = default) =>
        cards.CountBySubjectAsync(subjectId, ct);

    public Task<int> CountDeletedAsync(CancellationToken ct = default) =>
        cards.CountDeletedAsync(ct);

    public async Task<IReadOnlyList<Card>> SearchAsync(
        string query,
        CardSearchOptions options,
        CancellationToken ct = default)
    {
        var detailed = await SearchDetailedAsync(query, options, ct).ConfigureAwait(false);
        return detailed.Select(x => x.Card).ToList();
    }

    public async Task<IReadOnlyList<CardSearchResult>> SearchDetailedAsync(
        string query,
        CardSearchOptions options,
        CancellationToken ct = default)
    {
        var spec = CardQuery.Parse(query);
        var hits = await search.SearchAsync(spec, options, ct).ConfigureAwait(false);
        if (hits.Count == 0)
        {
            return [];
        }

        // Индекс отдаёт порядок, репозиторий — содержимое; собираем обратно, не потеряв ранжирование.
        var ids = hits.Select(x => x.CardId).ToList();
        var found = await cards.GetByIdsAsync(ids, ct).ConfigureAwait(false);
        var byId = found.ToDictionary(x => x.Id);

        var result = new List<CardSearchResult>(hits.Count);
        foreach (var hit in hits)
        {
            if (byId.TryGetValue(hit.CardId, out var card))
            {
                result.Add(new CardSearchResult(card, hit.Snippet, hit.Score));
            }
        }

        return result;
    }

    public Task<int> CountDueAsync(CancellationToken ct = default) =>
        cards.CountDueAsync(DateTimeOffset.Now, ct);

    public Task<IReadOnlyDictionary<Guid, int>> CountsBySubjectAsync(CancellationToken ct = default) =>
        cards.CountsBySubjectAsync(ct);

    public Task<IReadOnlyDictionary<Guid, int>> CountsByDeckAsync(CancellationToken ct = default) =>
        cards.CountsByDeckAsync(ct);

    public async Task<Card?> GetCardOfTheDayAsync(DateOnly day, CancellationToken ct = default)
    {
        // Кандидатов подбирает поиск: пресет «трудные» — это карточки, которые уже забывали.
        var weak = await SearchAsync(HardPreset, new CardSearchOptions(Limit: CardOfTheDayPool), ct)
            .ConfigureAwait(false);

        var pool = weak.Count > 0
            ? weak
            : await cards.GetRecentAsync(CardOfTheDayPool, ct).ConfigureAwait(false);

        if (pool.Count == 0)
        {
            return null;
        }

        // Номер дня по модулю: за сутки карточка не меняется, а назавтра честно сменится.
        return pool[Math.Abs(day.DayNumber) % pool.Count];
    }

    public Task<bool> IsSearchIndexHealthyAsync(CancellationToken ct = default) =>
        search.IsFullTextAvailableAsync(ct);

    public async Task<Result<Guid>> CreateAsync(
        Card card,
        IReadOnlyList<string>? tagNames = null,
        CancellationToken ct = default)
    {
        Guard.NotNull(card);

        var validation = await ValidateAsync(card, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return Result<Guid>.Failure(validation.Error);
        }

        card.Id = card.Id == Guid.Empty ? Guid.NewGuid() : card.Id;
        Normalize(card);

        var now = DateTimeOffset.Now;
        card.CreatedAt = card.CreatedAt == default ? now : card.CreatedAt;
        card.UpdatedAt = now;

        await cards.AddAsync(card, ct).ConfigureAwait(false);
        await ApplyTagsAsync(card.Id, tagNames, ct).ConfigureAwait(false);

        return Result<Guid>.Success(card.Id);
    }

    public async Task<Result> UpdateAsync(
        Card card,
        IReadOnlyList<string>? tagNames = null,
        CancellationToken ct = default)
    {
        Guard.NotNull(card);

        var existing = await cards.GetByIdAsync(card.Id, ct).ConfigureAwait(false);
        if (existing is null)
        {
            return Result.Failure("cards.card_not_found", "Карточка не найдена.");
        }

        var validation = await ValidateAsync(card, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return validation;
        }

        Normalize(card);
        card.CreatedAt = existing.CreatedAt;
        card.UpdatedAt = DateTimeOffset.Now;

        await cards.UpdateAsync(card, ct).ConfigureAwait(false);
        await ApplyTagsAsync(card.Id, tagNames, ct).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> UpdateContentAsync(
        Guid id,
        string front,
        string back,
        string? hint,
        string? source,
        CardKind kind,
        CardDifficulty difficulty,
        Guid? subjectId,
        Guid? deckId,
        CancellationToken ct = default)
    {
        var card = await cards.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (card is null)
        {
            return Result.Failure("cards.card_not_found", "Карточка не найдена.");
        }

        card.Front = front ?? string.Empty;
        card.Back = back ?? string.Empty;
        card.Hint = hint;
        card.Source = source;
        card.Kind = kind;
        card.Difficulty = difficulty;
        card.SubjectId = subjectId;
        card.DeckId = deckId;

        var validation = await ValidateAsync(card, ct).ConfigureAwait(false);
        if (validation.IsFailure)
        {
            return validation;
        }

        Normalize(card);
        card.UpdatedAt = DateTimeOffset.Now;

        await cards.UpdateAsync(card, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> SetTagsAsync(
        Guid id,
        IReadOnlyList<string> tagNames,
        CancellationToken ct = default)
    {
        Guard.NotNull(tagNames);

        if (await cards.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("cards.card_not_found", "Карточка не найдена.");
        }

        await ApplyTagsAsync(id, tagNames, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<Result> SetPinnedAsync(Guid id, bool pinned, CancellationToken ct = default) =>
        MutateAsync(id, card => card.IsPinned = pinned, ct);

    public Task<Result> SetSuspendedAsync(Guid id, bool suspended, CancellationToken ct = default) =>
        MutateAsync(id, card => card.IsSuspended = suspended, ct);

    public Task<Result<int>> SetPinnedManyAsync(
        IReadOnlyList<Guid> ids,
        bool pinned,
        CancellationToken ct = default)
    {
        Guard.NotNull(ids);
        return MutateManyAsync(ids, card => card.IsPinned = pinned, ct);
    }

    public Task<Result<int>> SetSuspendedManyAsync(
        IReadOnlyList<Guid> ids,
        bool suspended,
        CancellationToken ct = default)
    {
        Guard.NotNull(ids);
        return MutateManyAsync(ids, card => card.IsSuspended = suspended, ct);
    }

    public async Task<Result<int>> AddTagsAsync(
        IReadOnlyList<Guid> ids,
        IReadOnlyList<string> tagNames,
        CancellationToken ct = default)
    {
        Guard.NotNull(ids);
        Guard.NotNull(tagNames);

        if (ids.Count == 0 || tagNames.Count == 0)
        {
            return Result<int>.Success(0);
        }

        var tags = await tagService.EnsureManyAsync(tagNames, ct).ConfigureAwait(false);
        foreach (var tag in tags)
        {
            await tagLinks.AddLinksAsync(ids, tag.Id, ct).ConfigureAwait(false);
        }

        await tagService.RecalculateUsageAsync(ct).ConfigureAwait(false);
        return Result<int>.Success(ids.Count);
    }

    public async Task<Result<int>> RemoveTagsAsync(
        IReadOnlyList<Guid> ids,
        IReadOnlyList<string> tagNames,
        CancellationToken ct = default)
    {
        Guard.NotNull(ids);
        Guard.NotNull(tagNames);

        if (ids.Count == 0 || tagNames.Count == 0)
        {
            return Result<int>.Success(0);
        }

        // Снятие метки её не создаёт: ищем существующие по нормализованным именам и молчим об
        // остальных — просить снять то, чего нет, не ошибка.
        var normalized = tagNames
            .Select(ICardTagService.Normalize)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (normalized.Count == 0)
        {
            return Result<int>.Success(0);
        }

        var tags = await tagLinks.GetByNamesAsync(normalized, ct).ConfigureAwait(false);
        foreach (var tag in tags)
        {
            await tagLinks.RemoveLinksAsync(ids, tag.Id, ct).ConfigureAwait(false);
        }

        await tagService.RecalculateUsageAsync(ct).ConfigureAwait(false);
        return Result<int>.Success(ids.Count);
    }

    public async Task<Result<int>> MoveToTrashAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default)
    {
        Guard.NotNull(ids);
        var moved = await cards.SoftDeleteAsync(ids, DateTimeOffset.Now, ct).ConfigureAwait(false);
        return Result<int>.Success(moved);
    }

    public async Task<Result<int>> RestoreAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        Guard.NotNull(ids);
        var restored = await cards.RestoreAsync(ids, ct).ConfigureAwait(false);
        return Result<int>.Success(restored);
    }

    public async Task<Result<int>> EmptyTrashAsync(CancellationToken ct = default)
    {
        var purged = await cards.PurgeDeletedAsync(null, ct).ConfigureAwait(false);
        return Result<int>.Success(purged);
    }

    public async Task<Result<int>> AssignSubjectAsync(
        IReadOnlyList<Guid> ids,
        Guid? subjectId,
        CancellationToken ct = default)
    {
        Guard.NotNull(ids);

        if (subjectId is { } id && await subjects.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result<int>.Failure("cards.subject_not_found", "Предмет не найден.");
        }

        return await MutateManyAsync(ids, card => card.SubjectId = subjectId, ct).ConfigureAwait(false);
    }

    public async Task<Result<int>> AssignDeckAsync(
        IReadOnlyList<Guid> ids,
        Guid? deckId,
        CancellationToken ct = default)
    {
        Guard.NotNull(ids);

        if (deckId is { } id && await decks.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result<int>.Failure("cards.deck_not_found", "Колода не найдена.");
        }

        return await MutateManyAsync(ids, card => card.DeckId = deckId, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Card>> FindSimilarAsync(
        string front,
        Guid? excludeId = null,
        CancellationToken ct = default)
    {
        var trimmed = (front ?? string.Empty).Trim();
        if (trimmed.Length < 2)
        {
            return [];
        }

        // Кандидатов подбирает индекс — перебирать всю картотеку ради плашки в форме незачем.
        var hits = await search
            .SearchAsync(CardQuery.Parse(trimmed), new CardSearchOptions(Limit: 20), ct)
            .ConfigureAwait(false);
        if (hits.Count == 0)
        {
            return [];
        }

        var candidates = await cards
            .GetByIdsAsync(hits.Select(x => x.CardId).ToList(), ct)
            .ConfigureAwait(false);

        var needle = TokenSimilarity.Tokenize(trimmed);

        return candidates
            .Where(card => card.Id != excludeId)
            .Select(card => (Card: card, Score: TokenSimilarity.Dice(needle, TokenSimilarity.Tokenize(card.Front))))
            .Where(x => x.Score >= DuplicateThreshold)
            .OrderByDescending(x => x.Score)
            .Select(x => x.Card)
            .ToList();
    }

    public async Task<int> PurgeExpiredTrashAsync(TimeSpan retention, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.Now - retention;
        return await cards.PurgeDeletedAsync(cutoff, ct).ConfigureAwait(false);
    }

    public async Task<CardLinkResolution> ResolveWikiLinkAsync(string targetFront, CancellationToken ct = default)
    {
        var trimmed = (targetFront ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return new CardLinkResolution(null, []);
        }

        var similar = await FindSimilarAsync(trimmed, excludeId: null, ct).ConfigureAwait(false);
        if (similar.Count == 0)
        {
            return new CardLinkResolution(null, []);
        }

        var exact = similar.FirstOrDefault(
            c => string.Equals(FoldForLink(c.Front), FoldForLink(trimmed), StringComparison.Ordinal));

        return exact is not null
            ? new CardLinkResolution(exact.Id, [exact])
            : new CardLinkResolution(null, similar);
    }

    /// <summary>Регистр и «ё»/«е» — то же складывание, что и везде в картотеке (см. <see cref="CardQuery"/>).</summary>
    private static string FoldForLink(string value)
    {
        var collapsed = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.ToLowerInvariant().Replace('ё', 'е');
    }

    private async Task<Result> ValidateAsync(Card card, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(card.Front))
        {
            return Result.Failure("cards.front_required", "Лицевая сторона карточки не может быть пустой.");
        }

        if (card.SubjectId is { } subjectId
            && await subjects.GetByIdAsync(subjectId, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("cards.subject_not_found", "Предмет не найден.");
        }

        if (card.DeckId is { } deckId
            && await decks.GetByIdAsync(deckId, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("cards.deck_not_found", "Колода не найдена.");
        }

        return Result.Success();
    }

    /// <summary>Обрезка по длине колонок — форма и импорт не обязаны следить за этим сами.</summary>
    private static void Normalize(Card card)
    {
        var front = card.Front.Trim();
        card.Front = front.Length > MaxFrontLength ? front[..MaxFrontLength] : front;
        card.Back ??= string.Empty;
        card.Hint = ClampOptional(card.Hint, MaxHintLength);
        card.Source = ClampOptional(card.Source, MaxSourceLength);
        card.SchedulerName ??= string.Empty;
    }

    /// <summary>Пустая строка и отсутствие значения — одно и то же: в колонке остаётся NULL.</summary>
    private static string? ClampOptional(string? value, int max)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        return trimmed.Length > max ? trimmed[..max] : trimmed;
    }

    private async Task ApplyTagsAsync(Guid cardId, IReadOnlyList<string>? tagNames, CancellationToken ct)
    {
        if (tagNames is null)
        {
            return;
        }

        var tags = await tagService.EnsureManyAsync(tagNames, ct).ConfigureAwait(false);
        await tagLinks.SetLinksAsync(cardId, tags.Select(x => x.Id).ToList(), ct).ConfigureAwait(false);
        await tagService.RecalculateUsageAsync(ct).ConfigureAwait(false);
    }

    private async Task<Result> MutateAsync(Guid id, Action<Card> mutate, CancellationToken ct)
    {
        var card = await cards.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (card is null)
        {
            return Result.Failure("cards.card_not_found", "Карточка не найдена.");
        }

        mutate(card);
        card.UpdatedAt = DateTimeOffset.Now;
        await cards.UpdateAsync(card, ct).ConfigureAwait(false);
        return Result.Success();
    }

    private async Task<Result<int>> MutateManyAsync(
        IReadOnlyList<Guid> ids,
        Action<Card> mutate,
        CancellationToken ct)
    {
        var found = await cards.GetByIdsAsync(ids, ct).ConfigureAwait(false);
        if (found.Count == 0)
        {
            return Result<int>.Success(0);
        }

        var now = DateTimeOffset.Now;
        foreach (var card in found)
        {
            mutate(card);
            card.UpdatedAt = now;
        }

        await cards.UpdateManyAsync(found, ct).ConfigureAwait(false);
        return Result<int>.Success(found.Count);
    }
}
