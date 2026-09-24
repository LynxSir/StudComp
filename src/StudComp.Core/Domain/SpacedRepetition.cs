using StudComp.Core.Abstractions.Cards;

namespace StudComp.Core.Domain;

/// <summary>
/// SM-2 — классический алгоритм интервального повторения (new_addons.md §6.1). Чистая функция на
/// голом BCL: ни времени «изнутри», ни случайности, ни I/O — поэтому она полностью покрывается
/// табличными тестами. Прецеденты такого размещения математики в <c>Core</c> —
/// <see cref="WeekParityCalculator"/>, <see cref="LinearRegression"/>,
/// <see cref="AcademicHoursCalculator"/>.
/// </summary>
public static class SpacedRepetition
{
    /// <summary>Пол коэффициента лёгкости: ниже карточка превращалась бы в вечный ежедневник.</summary>
    public const double MinEaseFactor = 1.3;

    /// <summary>Стартовый коэффициент лёгкости новой карточки.</summary>
    public const double DefaultEaseFactor = 2.5;

    /// <summary>Сколько минут в сутках — интервал «переучить» дробный, и это единственное место, где важно.</summary>
    private const double MinutesPerDay = 1440.0;

    /// <summary>
    /// Назначить следующий срок повторения.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Множитель берёт коэффициент лёгкости <b>до</b> правки, а сохраняет уже новый — так устроен
    /// Anki, и только при таком порядке числа в табличных тестах однозначны.
    /// </para>
    /// <para>
    /// Правило «10 минут внутри сессии, потом 1 день» получается само: <see cref="ReviewGrade.Again"/>
    /// сбрасывает <c>Repetitions</c> в ноль, и следующий успех попадает на первую ступень лестницы.
    /// Отдельного состояния «переучивание» хранить не надо.
    /// </para>
    /// <para>
    /// Разброс (fuzz) применяется <b>до</b> потолка: потолок обязан быть жёстким, а не «365 дней ± 5 %».
    /// </para>
    /// </remarks>
    public static ReviewOutcome Next(
        ReviewState state,
        ReviewGrade grade,
        DateTimeOffset now,
        ReviewTuning tuning)
    {
        ArgumentNullException.ThrowIfNull(tuning);

        // Карточка могла прийти из импорта или из старой версии схемы — не доверяем её числам.
        var ease0 = double.IsFinite(state.EaseFactor) && state.EaseFactor > 0
            ? state.EaseFactor
            : DefaultEaseFactor;
        var interval0 = double.IsFinite(state.IntervalDays) && state.IntervalDays > 0
            ? state.IntervalDays
            : 0;

        var maxInterval = double.IsFinite(tuning.MaxIntervalDays) && tuning.MaxIntervalDays > 0
            ? tuning.MaxIntervalDays
            : ReviewTuning.Default.MaxIntervalDays;

        if (grade == ReviewGrade.Again)
        {
            var relearnDays = tuning.RelearnInSession
                ? Math.Max(tuning.RelearnMinutes, 1) / MinutesPerDay
                : 1.0;

            return new ReviewOutcome(
                IntervalDays: relearnDays,
                EaseFactor: Math.Max(MinEaseFactor, ease0 - 0.20),
                Repetitions: 0,
                Lapses: state.Lapses + 1,
                DueAt: now.AddDays(relearnDays),
                IsRelearning: tuning.RelearnInSession);
        }

        var repetitions = state.Repetitions + 1;

        // Лестница «первый показ — 1 день, второй — 6 дней, дальше умножаем на лёгкость».
        var ladder = repetitions switch
        {
            1 => 1.0,
            2 => 6.0,
            _ => interval0 * ease0,
        };
        ladder = Math.Max(ladder, 1.0);

        var (ease, interval) = grade switch
        {
            ReviewGrade.Hard => (Math.Max(MinEaseFactor, ease0 - 0.15), Math.Max(interval0, 1.0) * 1.2),
            ReviewGrade.Easy => (ease0 + 0.15, ladder * 1.3),
            _ => (ease0, ladder),
        };

        interval = ApplyFuzz(state.CardId, interval, tuning);
        interval = Math.Min(interval, maxInterval);

        return new ReviewOutcome(
            IntervalDays: interval,
            EaseFactor: ease,
            Repetitions: repetitions,
            Lapses: state.Lapses,
            DueAt: now.AddDays(interval),
            IsRelearning: false);
    }

    /// <summary>
    /// Разброс ±<see cref="ReviewTuning.FuzzRatio"/> на длинных интервалах: без него все карточки,
    /// заведённые в один день, через месяц сходятся в один пик.
    /// </summary>
    /// <remarks>
    /// Смещение детерминировано от идентификатора карточки <b>и</b> от округлённого интервала.
    /// Только от идентификатора карточку всю жизнь сдвигало бы в одну сторону, и разброс превратился
    /// бы в постоянную поправку. <c>Guid.GetHashCode()</c> здесь не годится — он не обязан совпадать
    /// между запусками процесса, а функция должна оставаться воспроизводимой.
    /// </remarks>
    private static double ApplyFuzz(Guid cardId, double interval, ReviewTuning tuning)
    {
        if (tuning.FuzzRatio <= 0 || interval <= tuning.FuzzMinIntervalDays)
        {
            return interval;
        }

        Span<byte> bytes = stackalloc byte[16];
        cardId.TryWriteBytes(bytes);

        // FNV-1a: короткий, стабильный и не зависящий от версии рантайма.
        const ulong offsetBasis = 14695981039346656037;
        const ulong prime = 1099511628211;

        var hash = offsetBasis;
        foreach (var b in bytes)
        {
            hash = (hash ^ b) * prime;
        }

        var intervalKey = (ulong)Math.Abs(Math.Round(interval * 100));
        for (var shift = 0; shift < 64; shift += 8)
        {
            hash = (hash ^ ((intervalKey >> shift) & 0xFF)) * prime;
        }

        var t = hash % 10001 / 10000.0;
        var multiplier = 1 - tuning.FuzzRatio + (2 * tuning.FuzzRatio * t);
        return interval * multiplier;
    }
}
