using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Organizer.Services;

namespace StudComp.Modules.Organizer.Tests;

public sealed class NotificationSchedulerTests : OrganizerDatabaseTestBase
{
    private readonly IToastService _toasts = Substitute.For<IToastService>();
    private readonly TestOptionsMonitor<NotificationOptions> _notifications =
        new(new NotificationOptions { RemindersEnabled = true, RemindMinutesBefore = 10 });

    private NotificationSchedulerHostedService CreateScheduler() => new(
        ScheduleRepo,
        DeadlineRepo,
        SubjectRepo,
        _toasts,
        Semesters,
        _notifications,
        NullLogger<NotificationSchedulerHostedService>.Instance);

    [Fact]
    public async Task Ripe_deadline_reminder_fires_once()
    {
        var subjectId = await SeedSubjectAsync();
        var now = DateTimeOffset.Now;
        // Срок через 5 минут, напоминать за 10 → момент напоминания уже наступил.
        await Deadlines.CreateAsync(new Deadline
        {
            SubjectId = subjectId,
            Title = "Сдать отчёт",
            DueDate = now.AddMinutes(5),
            Type = DeadlineType.Homework,
            Priority = DeadlinePriority.High,
        });

        var scheduler = CreateScheduler();

        await scheduler.EvaluateOnceAsync(now, CancellationToken.None);
        await scheduler.EvaluateOnceAsync(now, CancellationToken.None);

        _toasts.Received(1).Show(
            Arg.Any<string>(),
            Arg.Is<string>(s => s.Contains("Сдать отчёт")),
            Arg.Any<ToastKind>());
    }

    [Fact]
    public async Task Quiet_hours_hold_the_reminder_back_until_morning()
    {
        // Тихие часы жили в настройках с Phase 12.5, но их до сих пор никто не читал. Показ
        // откладывается до конца окна, а не отменяется: иначе ночное напоминание пропадало бы вовсе.
        var subjectId = await SeedSubjectAsync();
        var night = new DateTimeOffset(2026, 9, 8, 23, 0, 0, TimeSpan.Zero);

        await Deadlines.CreateAsync(new Deadline
        {
            SubjectId = subjectId,
            Title = "Сдать отчёт",
            DueDate = night.AddMinutes(5),
            Type = DeadlineType.Homework,
            Priority = DeadlinePriority.High,
        });

        _notifications.CurrentValue = new NotificationOptions
        {
            RemindersEnabled = true,
            RemindMinutesBefore = 10,
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(8, 0),
        };

        var wake = await CreateScheduler().EvaluateOnceAsync(night, CancellationToken.None);

        _toasts.DidNotReceive().Show(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ToastKind>());
        Assert.True(wake > night);
    }

    [Fact]
    public async Task Outside_the_quiet_window_the_reminder_still_fires()
    {
        var subjectId = await SeedSubjectAsync();
        var midday = new DateTimeOffset(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

        await Deadlines.CreateAsync(new Deadline
        {
            SubjectId = subjectId,
            Title = "Сдать отчёт",
            DueDate = midday.AddMinutes(5),
            Type = DeadlineType.Homework,
            Priority = DeadlinePriority.High,
        });

        _notifications.CurrentValue = new NotificationOptions
        {
            RemindersEnabled = true,
            RemindMinutesBefore = 10,
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(8, 0),
        };

        await CreateScheduler().EvaluateOnceAsync(midday, CancellationToken.None);

        _toasts.Received(1).Show(
            Arg.Any<string>(),
            Arg.Is<string>(s => s.Contains("Сдать отчёт")),
            Arg.Any<ToastKind>());
    }

    [Fact]
    public async Task No_reminders_when_disabled()
    {
        var subjectId = await SeedSubjectAsync();
        var now = DateTimeOffset.Now;
        await Deadlines.CreateAsync(new Deadline
        {
            SubjectId = subjectId,
            Title = "Сдать отчёт",
            DueDate = now.AddMinutes(5),
            Type = DeadlineType.Homework,
            Priority = DeadlinePriority.High,
        });
        _notifications.CurrentValue = new NotificationOptions { RemindersEnabled = false };

        await CreateScheduler().EvaluateOnceAsync(now, CancellationToken.None);

        _toasts.DidNotReceive().Show(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ToastKind>());
    }

    [Fact]
    public async Task Future_reminder_is_not_fired_and_sets_next_wakeup()
    {
        var subjectId = await SeedSubjectAsync();
        var now = DateTimeOffset.Now;
        // Срок через 2 часа, напоминать за 10 минут → момент напоминания ещё впереди.
        await Deadlines.CreateAsync(new Deadline
        {
            SubjectId = subjectId,
            Title = "Экзамен",
            DueDate = now.AddHours(2),
            Type = DeadlineType.Exam,
            Priority = DeadlinePriority.High,
        });

        var next = await CreateScheduler().EvaluateOnceAsync(now, CancellationToken.None);

        _toasts.DidNotReceive().Show(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<ToastKind>());
        Assert.True(next > now);
        Assert.True(next <= now.AddHours(1)); // потолок сна — час
    }
}
