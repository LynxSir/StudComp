using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Core.Abstractions.Cards;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;

namespace StudComp.Modules.Cards.Services;

/// <summary>Напоминание о повторении (new_addons.md §11).</summary>
public interface ICardReminderScheduler
{
    /// <summary>Пересчитать ближайшее напоминание досрочно — после правки настроек.</summary>
    void RequestReschedule();
}

/// <summary>
/// Раз в день напоминает, что подошла очередь повторения.
/// </summary>
/// <remarks>
/// <para>
/// Каркас дословно повторяет <c>NotificationSchedulerHostedService</c> Органайзера: один
/// <see cref="BackgroundService"/>, сон до ближайшего момента с потолком в час, досрочное
/// пробуждение по <see cref="RequestReschedule"/> и <see cref="EvaluateOnceAsync"/> как шов для теста.
/// </para>
/// <para>
/// Тихие часы напоминание <b>переносят, а не отменяют</b>: у пользователя с окном 22:00–08:00 и
/// временем напоминания 23:00 оно иначе не приходило бы никогда, и это выглядело бы как поломка.
/// Пустая очередь тоста не даёт вовсе — звать к несуществующей работе нельзя.
/// </para>
/// </remarks>
internal sealed class CardsReminderHostedService(
    IReviewQueueService queue,
    IToastService toasts,
    IMessenger messenger,
    IOptionsMonitor<CardsOptions> cardsOptions,
    IOptionsMonitor<NotificationOptions> notificationOptions,
    ILogger<CardsReminderHostedService> logger) : BackgroundService, ICardReminderScheduler
{
    private static readonly TimeSpan MaxSleep = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinSleep = TimeSpan.FromSeconds(1);

    /// <summary>Дать хосту домигрировать базу, прежде чем задавать ей вопросы.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _wake = new(0);

    /// <summary>За какой учебный день напоминание уже показано.</summary>
    private DateOnly? _notifiedDay;

    /// <inheritdoc />
    public void RequestReschedule()
    {
        if (_wake.CurrentCount == 0)
        {
            _wake.Release();
        }
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset nextWake;

            try
            {
                nextWake = await EvaluateOnceAsync(DateTimeOffset.Now, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                // Напоминание не имеет права уронить хост: подождём и попробуем ещё раз.
                logger.LogWarning(ex, "Не удалось посчитать напоминание о повторении.");
                nextWake = DateTimeOffset.Now.Add(MaxSleep);
            }

            var delay = nextWake - DateTimeOffset.Now;
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
    /// Один проход: решить, показывать ли напоминание сейчас, и когда просыпаться дальше.
    /// </summary>
    internal async Task<DateTimeOffset> EvaluateOnceAsync(DateTimeOffset now, CancellationToken ct)
    {
        var cards = cardsOptions.CurrentValue;
        var notifications = notificationOptions.CurrentValue;

        // Нужны оба рубильника: общий по уведомлениям и собственный у раздела.
        if (!cards.ReminderEnabled || !cards.ReviewEnabled || !notifications.RemindersEnabled)
        {
            return now.AddMinutes(15);
        }

        var today = StudyDay.DayOf(now, cards.DayRolloverHour);
        var fireAt = FireMoment(now, cards, today);

        if (notifications.QuietHoursEnabled)
        {
            fireAt = QuietHours.NextAllowedMoment(fireAt, notifications.QuietHoursStart, notifications.QuietHoursEnd);
        }

        if (fireAt > now)
        {
            return fireAt;
        }

        if (_notifiedDay != today)
        {
            _notifiedDay = today;

            var counts = await queue.GetCountsAsync(ct).ConfigureAwait(false);
            if (counts.Due > 0)
            {
                toasts.ShowAction(
                    "Пора повторить",
                    $"К повторению карточек: {counts.Due}.",
                    "Повторить",
                    () =>
                    {
                        // Навигацию модуль не знает — просит App через шину сообщений.
                        messenger.Send(new OpenReviewRequestedMessage());
                        return Task.CompletedTask;
                    });
            }
        }

        return FireMoment(now.AddDays(1), cards, today.AddDays(1));
    }

    private static DateTimeOffset FireMoment(DateTimeOffset now, CardsOptions cards, DateOnly day)
    {
        var dayStart = StudyDay.StartOf(now, cards.DayRolloverHour);
        var moment = new DateTimeOffset(
            dayStart.Year,
            dayStart.Month,
            dayStart.Day,
            cards.ReminderTime.Hour,
            cards.ReminderTime.Minute,
            0,
            now.Offset);

        // Время напоминания раньше часа начала суток — значит оно приходится уже на следующий день.
        return moment < dayStart ? moment.AddDays(1) : moment;
    }

    public override void Dispose()
    {
        _wake.Dispose();
        base.Dispose();
    }
}
