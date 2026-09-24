using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Напоминание о повторении (new_addons.md §11, Phase 12.8): рубильники, тихие часы и пустая очередь.
/// </summary>
public sealed class CardsReminderTests : CardsDatabaseTestBase
{
    private static readonly DateTimeOffset Evening = new(2026, 9, 7, 19, 30, 0, TimeSpan.FromHours(3));
    private static readonly DateTimeOffset Night = new(2026, 9, 7, 23, 30, 0, TimeSpan.FromHours(3));

    private IToastService _toasts = null!;
    private TestOptionsMonitor<NotificationOptions> _notifications = null!;

    [Fact]
    public async Task A_ripe_queue_earns_a_reminder()
    {
        await SeedReviewedCardAsync("Созрела", dueInDays: -1);

        var service = Reminder(enabled: true);

        await service.EvaluateOnceAsync(Evening, CancellationToken.None);

        _toasts.Received(1).ShowAction(
            "Пора повторить",
            Arg.Is<string>(m => m.Contains("1")),
            "Повторить",
            Arg.Any<Func<Task>>(),
            Arg.Any<ToastKind>());
    }

    [Fact]
    public async Task An_empty_queue_earns_no_reminder()
    {
        await SeedReviewedCardAsync("Ещё рано", dueInDays: 5);

        var service = Reminder(enabled: true);

        await service.EvaluateOnceAsync(Evening, CancellationToken.None);

        // Звать к несуществующей работе нельзя — это тот же принцип, что у бейджа сайдбара.
        _toasts.DidNotReceiveWithAnyArgs().ShowAction(default!, default!, default!, default!);
    }

    [Fact]
    public async Task The_reminder_is_shown_once_a_day()
    {
        await SeedReviewedCardAsync("Созрела", dueInDays: -1);

        var service = Reminder(enabled: true);

        await service.EvaluateOnceAsync(Evening, CancellationToken.None);
        await service.EvaluateOnceAsync(Evening.AddMinutes(30), CancellationToken.None);
        await service.EvaluateOnceAsync(Evening.AddHours(2), CancellationToken.None);

        _toasts.Received(1).ShowAction(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<Task>>(), Arg.Any<ToastKind>());
    }

    [Theory]
    [InlineData(false, true)]     // выключен раздел
    [InlineData(true, false)]     // выключены уведомления вообще
    [InlineData(false, false)]
    public async Task Either_switch_silences_the_reminder(bool cardsEnabled, bool notificationsEnabled)
    {
        await SeedReviewedCardAsync("Созрела", dueInDays: -1);

        var service = Reminder(cardsEnabled, notificationsEnabled);

        await service.EvaluateOnceAsync(Evening, CancellationToken.None);

        _toasts.DidNotReceiveWithAnyArgs().ShowAction(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Quiet_hours_postpone_the_reminder_instead_of_cancelling_it()
    {
        await SeedReviewedCardAsync("Созрела", dueInDays: -1);

        var service = Reminder(enabled: true, reminderAt: new TimeOnly(23, 0));
        _notifications.CurrentValue = new NotificationOptions
        {
            RemindersEnabled = true,
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(8, 0),
        };

        var wake = await service.EvaluateOnceAsync(Night, CancellationToken.None);

        // Иначе пользователь с окном 22:00–08:00 и напоминанием в 23:00 не получал бы его никогда.
        _toasts.DidNotReceiveWithAnyArgs().ShowAction(default!, default!, default!, default!);
        Assert.Equal(8, wake.Hour);
        Assert.True(wake > Night);
    }

    [Fact]
    public async Task Outside_the_quiet_window_the_reminder_goes_through()
    {
        await SeedReviewedCardAsync("Созрела", dueInDays: -1);

        var service = Reminder(enabled: true);
        _notifications.CurrentValue = new NotificationOptions
        {
            RemindersEnabled = true,
            QuietHoursEnabled = true,
            QuietHoursStart = new TimeOnly(22, 0),
            QuietHoursEnd = new TimeOnly(8, 0),
        };

        await service.EvaluateOnceAsync(Evening, CancellationToken.None);

        _toasts.ReceivedWithAnyArgs(1).ShowAction(default!, default!, default!, default!);
    }

    [Fact]
    public async Task Before_the_reminder_time_the_service_just_waits()
    {
        await SeedReviewedCardAsync("Созрела", dueInDays: -1);

        var service = Reminder(enabled: true, reminderAt: new TimeOnly(19, 0));

        var wake = await service.EvaluateOnceAsync(Evening.AddHours(-5), CancellationToken.None);

        _toasts.DidNotReceiveWithAnyArgs().ShowAction(default!, default!, default!, default!);
        Assert.Equal(19, wake.Hour);
    }

    [Fact]
    public async Task The_action_of_the_toast_asks_the_shell_to_open_the_review_tab()
    {
        await SeedReviewedCardAsync("Созрела", dueInDays: -1);

        Func<Task>? action = null;
        var service = Reminder(enabled: true);
        _toasts
            .When(t => t.ShowAction(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Func<Task>>(), Arg.Any<ToastKind>()))
            .Do(call => action = call.Arg<Func<Task>>());

        await service.EvaluateOnceAsync(Evening, CancellationToken.None);

        Assert.NotNull(action);
        await action!();       // модуль не знает про навигацию — он отправляет сообщение в шину
    }

    [Fact]
    public async Task Rescheduling_does_not_throw_when_nothing_is_waiting()
    {
        var service = Reminder(enabled: true);

        service.RequestReschedule();
        service.RequestReschedule();

        await service.EvaluateOnceAsync(Evening, CancellationToken.None);
    }

    private CardsReminderHostedService Reminder(
        bool enabled,
        bool notificationsEnabled = true,
        TimeOnly? reminderAt = null)
    {
        _toasts = Substitute.For<IToastService>();
        _notifications = new TestOptionsMonitor<NotificationOptions>(new NotificationOptions
        {
            RemindersEnabled = notificationsEnabled,
        });

        Options.CurrentValue = new CardsOptionsBuilder()
            .WithReminder(enabled, reminderAt ?? new TimeOnly(19, 0))
            .Build();

        return new CardsReminderHostedService(
            Queue,
            _toasts,
            new WeakReferenceMessenger(),
            Options,
            _notifications,
            NullLogger<CardsReminderHostedService>.Instance);
    }
}
