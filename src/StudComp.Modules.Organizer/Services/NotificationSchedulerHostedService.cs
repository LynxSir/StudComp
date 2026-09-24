using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Фоновый планировщик напоминаний (ARCHITECTURE §9.2, §9.5). Один <see cref="BackgroundService"/> с
/// динамическим сном до ближайшего события; напоминания уходят в <see cref="IToastService"/>
/// (в Phase 4 — balloon трея).
/// </summary>
internal sealed class NotificationSchedulerHostedService(
    IScheduleRepository schedule,
    IDeadlineRepository deadlines,
    ISubjectRepository subjects,
    IToastService toasts,
    ISemesterService semesters,
    IOptionsMonitor<NotificationOptions> notificationOptions,
    ILogger<NotificationSchedulerHostedService> logger)
    : BackgroundService, INotificationScheduler
{
    private static readonly TimeSpan MaxSleep = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinSleep = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(7);

    // Ключ события -> момент самого события; чтобы не слать один тост дважды и подчищать старьё.
    private readonly Dictionary<string, DateTimeOffset> _notified = [];
    private readonly SemaphoreSlim _wake = new(0);

    public void RequestReschedule()
    {
        // Release не бросает, если уже «поднят» до 1 — просто гарантируем, что цикл проснётся.
        if (_wake.CurrentCount == 0)
        {
            _wake.Release();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Дать хосту закончить старт и миграцию БД (IDbInitializer вызывается после StartAsync).
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset next;
            try
            {
                next = await EvaluateOnceAsync(DateTimeOffset.Now, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Сбой в цикле планировщика напоминаний — повтор через 5 минут");
                next = DateTimeOffset.Now.AddMinutes(5);
            }

            var delay = next - DateTimeOffset.Now;
            if (delay < MinSleep)
            {
                delay = MinSleep;
            }
            else if (delay > MaxSleep)
            {
                delay = MaxSleep;
            }

            try
            {
                // Проснуться либо по таймеру, либо по RequestReschedule().
                await Task.WhenAny(
                        Task.Delay(delay, stoppingToken),
                        _wake.WaitAsync(stoppingToken))
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Один проход: показать «дозревшие» напоминания и вернуть момент, к которому стоит проснуться
    /// в следующий раз (не позже чем через <see cref="MaxSleep"/> — чтобы подхватывать новые данные
    /// и изменения настроек).
    /// </summary>
    internal async Task<DateTimeOffset> EvaluateOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        var notification = notificationOptions.CurrentValue;
        if (!notification.RemindersEnabled)
        {
            return now.AddMinutes(15);
        }

        var lead = TimeSpan.FromMinutes(Math.Max(0, notification.RemindMinutesBefore));
        var semester = await semesters.GetActiveAsync(ct).ConfigureAwait(false);

        var subjectNames = (await subjects.GetAllAsync(ct).ConfigureAwait(false))
            .ToDictionary(s => s.Id, s => s.Name);

        var reminders = new List<Reminder>();
        reminders.AddRange(await BuildDeadlineRemindersAsync(now, lead, subjectNames, ct).ConfigureAwait(false));
        reminders.AddRange(await BuildClassRemindersAsync(now, lead, semester, subjectNames, ct).ConfigureAwait(false));

        // «Тихие часы» существовали в настройках с 12.5, но их до сих пор никто не читал. Показ в
        // тихое окно откладывается до его конца, а не отменяется: иначе напоминание, назначенное
        // внутри окна, не приходило бы вовсе.
        var quiet = notification.QuietHoursEnabled
            && QuietHours.IsQuiet(now, notification.QuietHoursStart, notification.QuietHoursEnd);

        DateTimeOffset? nextFuture = null;
        foreach (var reminder in reminders)
        {
            if (reminder.FireAt <= now)
            {
                if (quiet)
                {
                    continue;
                }

                if (_notified.TryAdd(reminder.Key, reminder.EventAt))
                {
                    toasts.Show(reminder.Title, reminder.Message, reminder.Kind);
                }
            }
            else if (nextFuture is null || reminder.FireAt < nextFuture)
            {
                nextFuture = reminder.FireAt;
            }
        }

        PruneNotified(now);

        if (quiet)
        {
            var wakeUp = QuietHours.NextAllowedMoment(
                now, notification.QuietHoursStart, notification.QuietHoursEnd);

            if (nextFuture is null || wakeUp < nextFuture)
            {
                nextFuture = wakeUp;
            }
        }

        var ceiling = now.Add(MaxSleep);
        return nextFuture is { } future && future < ceiling ? future : ceiling;
    }

    private async Task<IEnumerable<Reminder>> BuildDeadlineRemindersAsync(
        DateTimeOffset now,
        TimeSpan lead,
        IReadOnlyDictionary<Guid, string> subjectNames,
        CancellationToken ct)
    {
        var upcoming = await deadlines.GetUpcomingAsync(Horizon, ct).ConfigureAwait(false);
        return upcoming.Select(d =>
        {
            var subject = subjectNames.GetValueOrDefault(d.SubjectId, "предмет");
            return new Reminder(
                Key: $"deadline:{d.Id}:{d.DueDate.UtcTicks}",
                FireAt: d.DueDate - lead,
                EventAt: d.DueDate,
                Title: "Скоро дедлайн",
                Message: $"{subject}: {d.Title} — до {d.DueDate.LocalDateTime:dd.MM HH:mm}",
                Kind: ToastKind.Warning);
        });
    }

    private async Task<IEnumerable<Reminder>> BuildClassRemindersAsync(
        DateTimeOffset now,
        TimeSpan lead,
        Semester? semester,
        IReadOnlyDictionary<Guid, string> subjectNames,
        CancellationToken ct)
    {
        var entries = await schedule.GetAllAsync(ct).ConfigureAwait(false);
        if (entries.Count == 0)
        {
            return [];
        }

        var result = new List<Reminder>();
        var firstDay = DateOnly.FromDateTime(now.LocalDateTime);
        for (var offset = 0; offset <= Horizon.Days; offset++)
        {
            var day = firstDay.AddDays(offset);
            // Семестр не настроен — чётность посчитать не от чего, показываем все пары дня.
            var parity = semester is not null
                ? WeekParityCalculator.GetParity(semester.StartDate, day, semester.FirstWeekIsOdd)
                : (WeekParity?)null;

            foreach (var entry in entries)
            {
                if (entry.DayOfWeek != day.DayOfWeek)
                {
                    continue;
                }

                if (parity is not null && entry.WeekParity != WeekParity.Any && entry.WeekParity != parity)
                {
                    continue;
                }

                var startAt = new DateTimeOffset(day.ToDateTime(entry.StartTime), now.Offset);
                if (startAt < now)
                {
                    continue;
                }

                var subject = subjectNames.GetValueOrDefault(entry.SubjectId, "предмет");
                result.Add(new Reminder(
                    Key: $"class:{entry.Id}:{startAt.UtcTicks}",
                    FireAt: startAt - lead,
                    EventAt: startAt,
                    Title: "Скоро пара",
                    Message: $"{subject} в {entry.StartTime:HH\\:mm}"
                        + (string.IsNullOrWhiteSpace(entry.Room) ? string.Empty : $", ауд. {entry.Room}"),
                    Kind: ToastKind.Info));
            }
        }

        return result;
    }

    private void PruneNotified(DateTimeOffset now)
    {
        var cutoff = now.AddDays(-1);
        var stale = _notified.Where(kv => kv.Value < cutoff).Select(kv => kv.Key).ToList();
        foreach (var key in stale)
        {
            _notified.Remove(key);
        }
    }

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }

    private readonly record struct Reminder(
        string Key,
        DateTimeOffset FireAt,
        DateTimeOffset EventAt,
        string Title,
        string Message,
        ToastKind Kind);
}
