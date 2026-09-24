namespace StudComp.Core.Domain;

/// <summary>Способ проверки себя на карточке (new_addons.md §5.3).</summary>
public enum StudyCheckMode
{
    /// <summary>Самооценка: подумал, раскрыл ответ, честно поставил себе одну из четырёх оценок.</summary>
    SelfAssessment = 0,

    /// <summary>Выбор варианта: четыре ответа, один верный, проверка автоматическая.</summary>
    MultipleChoice = 1,

    /// <summary>Ввод ответа: сравнение с эталоном с допуском на опечатку.</summary>
    TypedAnswer = 2,

    /// <summary>Пропуски: фрагменты оборота закрыты и раскрываются по одному.</summary>
    Cloze = 3,
}

/// <summary>
/// Жёсткий автомат состояния карточки в сессии (new_addons.md §5.1). Ответ физически недоступен в
/// состоянии <see cref="Question"/>, оценка — до <see cref="Revealed"/>.
/// </summary>
public enum StudyCardState
{
    /// <summary>Виден только вопрос.</summary>
    Question = 0,

    /// <summary>Ответ раскрыт, можно оценивать.</summary>
    Revealed = 1,

    /// <summary>Оценка поставлена, карточка отработана.</summary>
    Graded = 2,
}

/// <summary>
/// Снимок фильтра сессии — то, что сериализуется в <see cref="StudySession.FilterJson"/>, чтобы
/// «пройти такую же ещё раз» не пришлось собирать заново.
/// </summary>
/// <remarks>
/// Метки и предметы хранятся идентификаторами, а не именами: повтор сессии обязан пережить
/// переименование метки. Сама сериализация живёт в модуле — <c>Core</c> остаётся без неё.
/// </remarks>
public sealed record StudySessionFilter(
    IReadOnlyList<Guid> SubjectIds,
    IReadOnlyList<Guid> DeckIds,
    IReadOnlyList<Guid> TagIds,
    IReadOnlyList<CardKind> Kinds,
    IReadOnlyList<Guid> CardIds,
    string? QueryExpression = null,
    bool IncludeSuspended = false,
    bool ReverseSides = false,
    StudyCheckMode CheckMode = StudyCheckMode.SelfAssessment,
    StudyOrder Order = StudyOrder.Random,
    int MaxCards = 0,
    int? TimeLimitSeconds = null,
    int? PerCardLimitSeconds = null,
    bool AffectsScheduling = true)
{
    /// <summary>Пустой фильтр — «вся живая картотека».</summary>
    public static StudySessionFilter Empty { get; } = new([], [], [], [], []);

    /// <summary>Явный список карточек: работа над ошибками, «гонять слабые», аврал.</summary>
    public bool HasExplicitCards => CardIds.Count > 0;
}
