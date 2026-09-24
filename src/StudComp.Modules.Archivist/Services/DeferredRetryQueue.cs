using System.Collections.Concurrent;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Отложенная очередь ретрая для залоченных файлов (ARCHITECTURE §8.6). Живёт в памяти: переживание
/// рестарта обеспечивает разовый скан папки (запись со статусом <c>Deferred</c> снова проходит разбор).
/// Расписание попыток — экспоненциальный бэкофф с потолком; после исчерпания попыток путь выпадает из
/// очереди, а вызывающий помечает файл как проблемный.
/// </summary>
internal sealed class DeferredRetryQueue
{
    // Множители к DeferredRetryInitialSeconds: при 30 с даёт 30 с, 2 мин, 5 мин, 15 мин, 30 мин.
    private static readonly int[] BackoffMultipliers = [1, 4, 10, 30, 60];

    private readonly ConcurrentDictionary<string, State> _entries = new(StringComparer.OrdinalIgnoreCase);

    private sealed record State(int Attempts, DateTimeOffset DueUtc);

    public int Count => _entries.Count;

    /// <summary>Сколько попыток уже сделано по этому пути (0 — путь не отслеживается).</summary>
    public int AttemptsFor(string path) => _entries.TryGetValue(path, out var state) ? state.Attempts : 0;

    /// <summary>
    /// Взять файл под наблюдение после первого <c>Deferred</c>. Повторный вызов существующее
    /// расписание не сбрасывает.
    /// </summary>
    public void Track(string path, ArchivistOptions options, DateTimeOffset nowUtc) =>
        _entries.GetOrAdd(path, _ => new State(1, nowUtc + Backoff(1, options)));

    /// <summary>Снять с наблюдения — файл разобрался либо ушёл.</summary>
    public void Forget(string path) => _entries.TryRemove(path, out _);

    /// <summary>
    /// Пути, чьё время пришло: <c>Due</c> — можно пробовать снова (расписание сдвинуто на следующее
    /// окно), <c>Exhausted</c> — попытки исчерпаны, из очереди удалены.
    /// </summary>
    public (IReadOnlyList<string> Due, IReadOnlyList<string> Exhausted) TakeDue(
        ArchivistOptions options, DateTimeOffset nowUtc)
    {
        var due = new List<string>();
        var exhausted = new List<string>();
        var maxAttempts = Math.Max(1, options.DeferredRetryMaxAttempts);

        foreach (var (path, state) in _entries.ToArray())
        {
            if (state.DueUtc > nowUtc)
            {
                continue;
            }

            var nextAttempt = state.Attempts + 1;
            if (nextAttempt > maxAttempts)
            {
                if (_entries.TryRemove(path, out _))
                {
                    exhausted.Add(path);
                }

                continue;
            }

            _entries[path] = new State(nextAttempt, nowUtc + Backoff(nextAttempt, options));
            due.Add(path);
        }

        return (due, exhausted);
    }

    private static TimeSpan Backoff(int attempt, ArchivistOptions options)
    {
        var initial = Math.Max(1, options.DeferredRetryInitialSeconds);
        var cap = Math.Max(initial, options.DeferredRetryMaxIntervalSeconds);
        var multiplier = BackoffMultipliers[Math.Clamp(attempt - 1, 0, BackoffMultipliers.Length - 1)];
        return TimeSpan.FromSeconds(Math.Min((long)initial * multiplier, cap));
    }
}
