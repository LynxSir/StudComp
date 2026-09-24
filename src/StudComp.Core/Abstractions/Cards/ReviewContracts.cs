using StudComp.Core.Domain;

namespace StudComp.Core.Abstractions.Cards;

/// <summary>
/// Состояние повторения карточки на входе алгоритма (new_addons.md §3.2). Идентификатор нужен не для
/// поиска, а для разброса интервалов: он детерминирован от карточки, иначе функция перестала бы быть
/// чистой.
/// </summary>
public readonly record struct ReviewState(
    Guid CardId,
    double IntervalDays,
    double EaseFactor,
    int Repetitions,
    int Lapses)
{
    /// <summary>Снять состояние с карточки.</summary>
    public static ReviewState From(Card card)
    {
        ArgumentNullException.ThrowIfNull(card);

        return new ReviewState(
            card.Id,
            card.IntervalDays,
            card.EaseFactor,
            card.Repetitions,
            card.Lapses);
    }
}

/// <summary>
/// Что алгоритм назначил карточке после ответа. <see cref="IsRelearning"/> — карточку надо вернуть
/// в этой же сессии, а не через день (new_addons.md §6.1).
/// </summary>
public readonly record struct ReviewOutcome(
    double IntervalDays,
    double EaseFactor,
    int Repetitions,
    int Lapses,
    DateTimeOffset DueAt,
    bool IsRelearning);

/// <summary>
/// Настройка алгоритма, приходящая из <c>CardsOptions</c>.
/// </summary>
/// <remarks>
/// Именно <see langword="record"/>-класс, а не <c>record struct</c>: у структуры <c>new()</c>
/// игнорирует значения по умолчанию первичного конструктора и молча обнуляет всё — потолок интервала
/// стал бы нулём, и каждая карточка приходила бы на повторение немедленно.
/// </remarks>
public sealed record ReviewTuning(
    double MaxIntervalDays = 365,
    double RelearnMinutes = 10,
    bool RelearnInSession = true,
    double FuzzRatio = 0.05,
    double FuzzMinIntervalDays = 3)
{
    /// <summary>Заводские значения — ими пользуются тесты и вызовы без настроек.</summary>
    public static ReviewTuning Default { get; } = new();
}

/// <summary>
/// Подсказка на кнопке оценки: «Хорошо · через 10 дней» (new_addons.md §8.3). Решение должно быть
/// осознанным, поэтому цена каждого варианта видна до нажатия.
/// </summary>
public readonly record struct ReviewPreview(ReviewGrade Grade, double IntervalDays, DateTimeOffset DueAt);
