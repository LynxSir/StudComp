namespace StudComp.Core.Domain;

/// <summary>
/// Оценка, которую пользователь ставит себе после раскрытия ответа (new_addons.md §6.1) — те самые
/// четыре кнопки и клавиши <c>1</c>–<c>4</c>.
/// </summary>
public enum ReviewGrade
{
    /// <summary>Не помню — карточка возвращается в этой же сессии.</summary>
    Again = 0,

    /// <summary>Вспомнил с трудом.</summary>
    Hard = 1,

    /// <summary>Вспомнил.</summary>
    Good = 2,

    /// <summary>Вспомнил сразу — интервал растёт быстрее обычного.</summary>
    Easy = 3,
}

/// <summary>
/// Одна запись журнала ответов (new_addons.md §3.1). Только он даёт статистику и разбор ошибок,
/// поэтому пишется сразу после ответа, а не в конце сессии: закрытое посреди работы окно не должно
/// стирать сделанное.
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class CardReviewLog
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    public Guid CardId { get; set; }

    /// <summary>Сессия, в которой дан ответ; <see langword="null"/> — одиночный ответ вне сессии.</summary>
    public Guid? SessionId { get; set; }

    public DateTimeOffset ReviewedAt { get; set; }

    public ReviewGrade Grade { get; set; }

    public StudyMode Mode { get; set; }

    /// <summary>Сколько пользователь думал над карточкой, в миллисекундах.</summary>
    public int ElapsedMs { get; set; }

    /// <summary>Интервал до ответа, в днях.</summary>
    public double IntervalBeforeDays { get; set; }

    /// <summary>Интервал, назначенный ответом, в днях.</summary>
    public double IntervalAfterDays { get; set; }

    public double EaseBefore { get; set; }

    public double EaseAfter { get; set; }

    /// <summary>
    /// Верен ли ответ — для режимов с автопроверкой (тест, ввод ответа).
    /// <see langword="null"/> при самооценке: там «верно» определяет сам пользователь оценкой.
    /// </summary>
    public bool? WasCorrect { get; set; }
}
