using Microsoft.EntityFrameworkCore;
using StudComp.Core.Abstractions.Cards;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к карточкам знаний (new_addons.md §3.1, §3.5).</summary>
/// <remarks>
/// «Живой» здесь всюду означает <c>DeletedAt IS NULL</c>: удалённая карточка не исчезает, а уходит
/// в «Корзину» (мягкое удаление, §3.5). Физически строку сносит только <see cref="PurgeDeletedAsync"/>.
/// </remarks>
public interface ICardRepository
{
    Task<Card?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Карточки по списку идентификаторов — этим слой поиска «оживляет» выдачу индекса. Порядок
    /// результата не гарантируется: ранжирование знает только вызывающий.
    /// </summary>
    Task<IReadOnlyList<Card>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);

    /// <summary>Живые карточки предмета: закреплённые сверху, дальше по свежести правки.</summary>
    Task<IReadOnlyList<Card>> GetBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    /// <summary>Живые карточки колоды.</summary>
    Task<IReadOnlyList<Card>> GetByDeckAsync(Guid deckId, CancellationToken ct = default);

    /// <summary>Последние живые карточки — стартовая выдача библиотеки при пустом запросе.</summary>
    Task<IReadOnlyList<Card>> GetRecentAsync(int take, CancellationToken ct = default);

    /// <summary>
    /// Все живые карточки без лимита — экспорт всей картотеки (new_addons.md §7.6, область «Всё»).
    /// Для остальных сценариев (библиотека, поиск, тренировка) есть более узкие выборки — этим
    /// методом кроме экспорта пользоваться незачем.
    /// </summary>
    Task<IReadOnlyList<Card>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Содержимое «Корзины»: свежеудалённые сверху.</summary>
    Task<IReadOnlyList<Card>> GetDeletedAsync(int take, CancellationToken ct = default);

    /// <summary>Сколько живых карточек всего.</summary>
    Task<int> CountAsync(CancellationToken ct = default);

    /// <summary>Сколько живых карточек у предмета — счётчик вкладки Хаба.</summary>
    Task<int> CountBySubjectAsync(Guid subjectId, CancellationToken ct = default);

    /// <summary>Сколько карточек лежит в «Корзине».</summary>
    Task<int> CountDeletedAsync(CancellationToken ct = default);

    /// <summary>
    /// Сколько живых карточек пора повторить — бейдж сайдбара и зона Дашборда. Отложенные не
    /// считаются: они и в тренировку не попадут, а бейдж, зовущий к недоступной работе, врёт.
    /// </summary>
    Task<int> CountDueAsync(DateTimeOffset now, CancellationToken ct = default);

    /// <summary>
    /// Сколько живых карточек у каждого предмета — счётчики в рельсе фильтров. Одним запросом:
    /// счётчик на пункт превратил бы открытие раздела в N+1 (урок «Неразобранного», Phase 12.2).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> CountsBySubjectAsync(CancellationToken ct = default);

    /// <summary>Сколько живых карточек в каждой колоде — тем же одним запросом.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountsByDeckAsync(CancellationToken ct = default);

    Task AddAsync(Card card, CancellationToken ct = default);

    /// <summary>Пачкой, одной транзакцией — импорт и разбор заметки на карточки.</summary>
    Task AddManyAsync(IReadOnlyList<Card> cards, CancellationToken ct = default);

    Task UpdateAsync(Card card, CancellationToken ct = default);

    Task UpdateManyAsync(IReadOnlyList<Card> cards, CancellationToken ct = default);

    /// <summary>Отправить в «Корзину». Возвращает, сколько карточек затронуто.</summary>
    Task<int> SoftDeleteAsync(
        IReadOnlyList<Guid> ids,
        DateTimeOffset deletedAt,
        CancellationToken ct = default);

    /// <summary>Вернуть из «Корзины». Возвращает, сколько карточек затронуто.</summary>
    Task<int> RestoreAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default);

    /// <summary>
    /// Физически удалить содержимое «Корзины». <paramref name="deletedBefore"/> ограничивает выборку
    /// по возрасту (ретеншн); <see langword="null"/> — «очистить корзину целиком».
    /// Единственная необратимая операция над карточками.
    /// </summary>
    Task<int> PurgeDeletedAsync(DateTimeOffset? deletedBefore, CancellationToken ct = default);

    /// <summary>
    /// Пул карточек для планировщика сессии — лёгкая проекция без <c>Front</c> и <c>Back</c>.
    /// </summary>
    /// <remarks>
    /// Именно запрос, а не фильтрация в памяти: отбор по меткам — это join к <c>CardTagLinks</c>, а
    /// лимит без серверной сортировки вернул бы <b>не те</b> карточки: очередь дня обязана
    /// обрезаться по сроку в базе. Проекция экономит больше самого лимита — <c>Back</c> это Markdown
    /// с листингами, и тащить его ради упорядочивания сорока карточек незачем.
    /// </remarks>
    Task<IReadOnlyList<StudyCandidate>> GetStudyCandidatesAsync(
        StudyPoolFilter filter,
        CancellationToken ct = default);

    /// <summary>Сколько живых новых карточек ждёт первого показа.</summary>
    Task<int> CountNewAvailableAsync(CancellationToken ct = default);

    /// <summary>
    /// Кривая нагрузки вперёд: сколько карточек придёт на повторение по дням (new_addons.md §6.5).
    /// </summary>
    /// <param name="offsetMinutes">
    /// Сдвиг, приводящий хранимое UTC-время к «учебным суткам» пользователя: местное смещение минус
    /// час начала суток. Без него календарь уезжает на часовой пояс.
    /// </param>
    Task<IReadOnlyList<DueForecastBucket>> GetDueForecastAsync(
        DateTimeOffset from,
        int days,
        int offsetMinutes,
        CancellationToken ct = default);

    /// <summary>
    /// Записать новое состояние повторения — шесть числовых колонок одним запросом.
    /// </summary>
    /// <remarks>
    /// Отдельный метод вместо <see cref="UpdateAsync"/> нужен не ради экономии, а ради корректности:
    /// панель просмотра автосохраняет текст карточки по дебаунсу, и запись целой сущности из сессии
    /// затёрла бы правку, сделанную секундой раньше. <c>UpdatedAt</c> сознательно не трогается —
    /// повторение не является правкой карточки, иначе поехала бы сортировка «недавно изменённые».
    /// </remarks>
    Task<int> ApplyReviewAsync(
        Guid cardId,
        ReviewOutcome outcome,
        DateTimeOffset reviewedAt,
        CancellationToken ct = default);
}

internal sealed class CardRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), ICardRepository
{
    public async Task<Card?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Card>> GetByIdsAsync(
        IReadOnlyList<Guid> ids,
        CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Card>> GetBySubjectAsync(
        Guid subjectId,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .AsNoTracking()
            .Where(x => x.SubjectId == subjectId && x.DeletedAt == null)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Card>> GetByDeckAsync(Guid deckId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .AsNoTracking()
            .Where(x => x.DeckId == deckId && x.DeletedAt == null)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Card>> GetRecentAsync(int take, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .AsNoTracking()
            .Where(x => x.DeletedAt == null)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Card>> GetAllAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .AsNoTracking()
            .Where(x => x.DeletedAt == null)
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.UpdatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Card>> GetDeletedAsync(int take, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .AsNoTracking()
            .Where(x => x.DeletedAt != null)
            .OrderByDescending(x => x.DeletedAt)
            .Take(take)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .AsNoTracking()
            .CountAsync(x => x.DeletedAt == null, ct)
            .ConfigureAwait(false);
    }

    public async Task<int> CountBySubjectAsync(Guid subjectId, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .AsNoTracking()
            .CountAsync(x => x.SubjectId == subjectId && x.DeletedAt == null, ct)
            .ConfigureAwait(false);
    }

    public async Task<int> CountDeletedAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .AsNoTracking()
            .CountAsync(x => x.DeletedAt != null, ct)
            .ConfigureAwait(false);
    }

    public async Task<int> CountDueAsync(DateTimeOffset now, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .AsNoTracking()
            .CountAsync(x => x.DeletedAt == null && !x.IsSuspended && x.DueAt != null && x.DueAt <= now, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountsBySubjectAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        var rows = await context.Cards
            .AsNoTracking()
            .Where(x => x.DeletedAt == null && x.SubjectId != null)
            .GroupBy(x => x.SubjectId!.Value)
            .Select(group => new { SubjectId = group.Key, Count = group.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.ToDictionary(row => row.SubjectId, row => row.Count);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountsByDeckAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        var rows = await context.Cards
            .AsNoTracking()
            .Where(x => x.DeletedAt == null && x.DeckId != null)
            .GroupBy(x => x.DeckId!.Value)
            .Select(group => new { DeckId = group.Key, Count = group.Count() })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return rows.ToDictionary(row => row.DeckId, row => row.Count);
    }

    public async Task AddAsync(Card card, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.Cards.Add(card);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task AddManyAsync(IReadOnlyList<Card> cards, CancellationToken ct = default)
    {
        if (cards.Count == 0)
        {
            return;
        }

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.Cards.AddRange(cards);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(Card card, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.Cards.Update(card);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateManyAsync(IReadOnlyList<Card> cards, CancellationToken ct = default)
    {
        if (cards.Count == 0)
        {
            return;
        }

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.Cards.UpdateRange(cards);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<int> SoftDeleteAsync(
        IReadOnlyList<Guid> ids,
        DateTimeOffset deletedAt,
        CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        // Массовая правка одним запросом. Поисковый индекс от этого не отстаёт: он держится на
        // триггерах уровня SQLite, которым безразлично, пришла запись через SaveChanges или мимо
        // него (new_addons.md §4.2).
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .Where(x => ids.Contains(x.Id) && x.DeletedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DeletedAt, deletedAt), ct)
            .ConfigureAwait(false);
    }

    public async Task<int> RestoreAsync(IReadOnlyList<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return 0;
        }

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .Where(x => ids.Contains(x.Id) && x.DeletedAt != null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(x => x.DeletedAt, (DateTimeOffset?)null),
                ct)
            .ConfigureAwait(false);
    }

    public async Task<int> PurgeDeletedAsync(
        DateTimeOffset? deletedBefore,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        var query = context.Cards.Where(x => x.DeletedAt != null);

        if (deletedBefore is { } cutoff)
        {
            query = query.Where(x => x.DeletedAt < cutoff);
        }

        return await query.ExecuteDeleteAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<StudyCandidate>> GetStudyCandidatesAsync(
        StudyPoolFilter filter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        var query = context.Cards.AsNoTracking().Where(x => x.DeletedAt == null);

        if (!filter.IncludeSuspended)
        {
            query = query.Where(x => !x.IsSuspended);
        }

        if (filter.CardIds is { Count: > 0 } cardIds)
        {
            query = query.Where(x => cardIds.Contains(x.Id));
        }

        if (filter.SubjectIds is { Count: > 0 } subjectIds)
        {
            query = query.Where(x => x.SubjectId != null && subjectIds.Contains(x.SubjectId.Value));
        }

        if (filter.DeckIds is { Count: > 0 } deckIds)
        {
            query = query.Where(x => x.DeckId != null && deckIds.Contains(x.DeckId.Value));
        }

        if (filter.Kinds is { Count: > 0 } kinds)
        {
            query = query.Where(x => kinds.Contains(x.Kind));
        }

        if (filter.TagIds is { Count: > 0 } tagIds)
        {
            query = query.Where(x => context.CardTagLinks.Any(l => l.CardId == x.Id && tagIds.Contains(l.TagId)));
        }

        if (filter.OnlyNew)
        {
            query = query.Where(x => x.DueAt == null);
        }

        if (filter.ExcludeNew)
        {
            query = query.Where(x => x.DueAt != null);
        }

        if (filter.DueBefore is { } due)
        {
            query = query.Where(x => x.DueAt != null && x.DueAt <= due);
        }

        // Лимит без порядка вернул бы произвольные строки, а очередь дня обязана быть той самой.
        query = filter.Sort switch
        {
            StudyPoolSort.DueAsc => query.OrderBy(x => x.DueAt).ThenBy(x => x.Id),
            StudyPoolSort.CreatedAsc => query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id),
            StudyPoolSort.CreatedDesc => query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id),
            StudyPoolSort.UpdatedDesc => query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id),
            _ when filter.Limit > 0 => query.OrderBy(x => x.Id),
            _ => query,
        };

        if (filter.Limit > 0)
        {
            query = query.Take(filter.Limit);
        }

        var rows = await query
            .Select(x => new
            {
                x.Id,
                x.DeckId,
                x.SubjectId,
                x.Lapses,
                x.EaseFactor,
                x.LastReviewedAt,
                x.DueAt,
                x.CreatedAt,
                DeckSortOrder = x.DeckId == null
                    ? 0
                    : context.CardDecks.Where(d => d.Id == x.DeckId).Select(d => d.SortOrder).FirstOrDefault(),
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return
        [
            .. rows.Select(r => new StudyCandidate(
                r.Id,
                r.DeckId,
                r.SubjectId,
                r.DeckSortOrder,
                r.Lapses,
                r.EaseFactor,
                r.LastReviewedAt,
                r.DueAt,
                r.CreatedAt)),
        ];
    }

    public async Task<int> CountNewAvailableAsync(CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .AsNoTracking()
            .CountAsync(x => x.DeletedAt == null && !x.IsSuspended && x.DueAt == null, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<DueForecastBucket>> GetDueForecastAsync(
        DateTimeOffset from,
        int days,
        int offsetMinutes,
        CancellationToken ct = default)
    {
        if (days <= 0)
        {
            return [];
        }

        var to = from.AddDays(days);

        // Группировка по дню пишется сырым SQL: DueAt объявлен с конвертером значения, а EF не
        // транслирует обращение к членам конвертированного свойства — «x.DueAt.Value.Date» падает
        // с «member access cannot be translated». Прецедент сырого SQL здесь же — CardSearchRepository.
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);

        return await SqliteDayGrouping
            .ReadAsync(
                context,
                """
                SELECT date(DueAt, $shift) AS Day, COUNT(*) AS Total
                  FROM Cards
                 WHERE DeletedAt IS NULL AND IsSuspended = 0
                   AND DueAt IS NOT NULL AND DueAt >= $from AND DueAt < $to
                 GROUP BY 1
                 ORDER BY 1
                """,
                command =>
                {
                    SqliteDayGrouping.AddShift(command, offsetMinutes);
                    SqliteDayGrouping.AddInstant(command, "$from", from);
                    SqliteDayGrouping.AddInstant(command, "$to", to);
                },
                reader => new DueForecastBucket(SqliteDayGrouping.ReadDay(reader, 0), reader.GetInt32(1)),
                ct)
            .ConfigureAwait(false);
    }

    public async Task<int> ApplyReviewAsync(
        Guid cardId,
        ReviewOutcome outcome,
        DateTimeOffset reviewedAt,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.Cards
            .Where(x => x.Id == cardId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(x => x.DueAt, (DateTimeOffset?)outcome.DueAt)
                    .SetProperty(x => x.IntervalDays, outcome.IntervalDays)
                    .SetProperty(x => x.EaseFactor, outcome.EaseFactor)
                    .SetProperty(x => x.Repetitions, outcome.Repetitions)
                    .SetProperty(x => x.Lapses, outcome.Lapses)
                    .SetProperty(x => x.LastReviewedAt, (DateTimeOffset?)reviewedAt),
                ct)
            .ConfigureAwait(false);
    }
}
