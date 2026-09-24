using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>
/// Раздел «Дедлайны и уведомления» (new_addons.md §7 §6): напоминания, тихие часы, звук.
/// </summary>
public sealed partial class NotificationSettingsViewModel : SettingsSectionViewModelBase
{
    // "hh" в .NET — 12-часовой формат без AM/PM: час 13 печатался как «01» (new_addons.md §8).
    private const string TimeFormat = @"HH\:mm";

    private readonly UserSettingsProvider _settings;

    public NotificationSettingsViewModel(UserSettingsProvider settings, IOptions<NotificationOptions> notifications)
    {
        _settings = settings;

        var o = notifications.Value;
        using (BeginLoad())
        {
            _remindersEnabled = o.RemindersEnabled;
            _remindMinutesBefore = o.RemindMinutesBefore;
            _quietHoursEnabled = o.QuietHoursEnabled;
            _quietHoursStartText = o.QuietHoursStart.ToString(TimeFormat, CultureInfo.InvariantCulture);
            _quietHoursEndText = o.QuietHoursEnd.ToString(TimeFormat, CultureInfo.InvariantCulture);
            _soundEnabled = o.SoundEnabled;
            _unsortedReminderDays = o.UnsortedReminderDays;
        }
    }

    public override SettingsSection Section => SettingsSection.Notifications;

    public override string Title => "Дедлайны и уведомления";

    public override SymbolRegular Icon => SymbolRegular.Alert24;

    public override IEnumerable<string> SearchKeywords =>
        ["напоминания", "дедлайны", "пары", "тихие часы", "не беспокоить", "звук", "уведомления", "залежавшиеся файлы"];

    [ObservableProperty]
    private bool _remindersEnabled;

    [ObservableProperty]
    private int _remindMinutesBefore;

    [ObservableProperty]
    private bool _quietHoursEnabled;

    [ObservableProperty]
    private string _quietHoursStartText;

    [ObservableProperty]
    private string _quietHoursEndText;

    [ObservableProperty]
    private bool _soundEnabled;

    [ObservableProperty]
    private int _unsortedReminderDays;

    partial void OnRemindersEnabledChanged(bool value) => Persist(o => o.RemindersEnabled = value);

    partial void OnRemindMinutesBeforeChanged(int value) => Persist(o => o.RemindMinutesBefore = Math.Clamp(value, 0, 240));

    partial void OnQuietHoursEnabledChanged(bool value) => Persist(o => o.QuietHoursEnabled = value);

    partial void OnSoundEnabledChanged(bool value) => Persist(o => o.SoundEnabled = value);

    partial void OnUnsortedReminderDaysChanged(int value) => Persist(o => o.UnsortedReminderDays = Math.Max(0, value));

    partial void OnQuietHoursStartTextChanged(string value)
    {
        if (TryParse(value, out var time))
        {
            Persist(o => o.QuietHoursStart = time);
        }
    }

    partial void OnQuietHoursEndTextChanged(string value)
    {
        if (TryParse(value, out var time))
        {
            Persist(o => o.QuietHoursEnd = time);
        }
    }

    private static bool TryParse(string? text, out TimeOnly value) =>
        TimeOnly.TryParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
        || TimeOnly.TryParse(text, CultureInfo.CurrentCulture, out value);

    private void Persist(Action<NotificationOptions> mutate)
    {
        if (!IsLoading)
        {
            _settings.Update(NotificationOptions.SectionName, mutate);
        }
    }
}
