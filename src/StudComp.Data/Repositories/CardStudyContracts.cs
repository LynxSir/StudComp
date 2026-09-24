using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Порядок, в котором база отдаёт пул карточек для тренажёра.</summary>
public enum StudyPoolSort
{
    /// <summary>Без явного порядка. При заданном лимите вырождается в порядок по идентификатору.</summary>
    None = 0,

    /// <summary>По сроку повторения: самые просроченные первыми.</summary>
    DueAsc = 1,

    /// <summary>Сначала давно созданные — так вводятся новые карточки.</summary>
    CreatedAsc = 2,

    /// <summary>Сначала недавно созданные.</summary>
    CreatedDesc = 3,

    /// <summary>Сначала недавно изменённые.</summary>
    UpdatedDesc = 4,
}

/// <summary>
/// Описание пула карточек для тренажёра (new_addons.md §5.5). Пустые коллекции и <see langword="null"/>
/// означают «условие не задано».
/// </summary>
/// <remarks>
/// Удалённые карточки не попадают в пул никогда — это не настройка, а инвариант корзины (§3.5).
/// </remarks>
public sealed record StudyPoolFilter(
    IReadOnlyList<Guid>? SubjectIds = null,
    IReadOnlyList<Guid>? DeckIds = null,
    IReadOnlyList<Guid>? TagIds = null,
    IReadOnlyList<Guid>? CardIds = null,
    IReadOnlyList<CardKind>? Kinds = null,
    bool IncludeSuspended = false,
    DateTimeOffset? DueBefore = null,
    bool OnlyNew = false,
    bool ExcludeNew = false,
    StudyPoolSort Sort = StudyPoolSort.None,
    int Limit = 0);

/// <summary>Сколько карточек придёт на повторение в один день — точка кривой нагрузки (§6.5).</summary>
public sealed record DueForecastBucket(DateOnly Day, int Count);

/// <summary>Сколько ответов и сколько верных за один учебный день — точка календаря активности (§6.5).</summary>
public sealed record DailyReviewCount(DateOnly Day, int Answers, int Correct);

/// <summary>
/// Точность по какому-то срезу: предмет, метка или карточка. <see cref="Id"/> — идентификатор среза,
/// <see langword="null"/> для карточек без предмета.
/// </summary>
public sealed record ReviewAccuracyRow(Guid? Id, int Answers, int Correct);

/// <summary>Прогресс аврала по предмету: сколько показов сделано и когда начали (§6.4).</summary>
public sealed record CramProgressRow(int Shows, DateTimeOffset? FirstAt);
