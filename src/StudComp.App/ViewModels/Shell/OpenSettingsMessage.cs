namespace StudComp.ViewModels.Shell;

/// <summary>
/// Просьба открыть модалку настроек, опционально на конкретном разделе (new_addons.md §1.6). Шлётся
/// через <c>IMessenger</c> из экранов, которым нужен дип-линк в настройки (онбординг Дашборда,
/// <c>InfoBar</c> Архивариуса и т. п.). Получатель — <c>MainWindowViewModel</c>.
/// </summary>
public sealed record OpenSettingsMessage(SettingsSection? Section = null);
