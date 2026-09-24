using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Backup;
using StudComp.Resources;

namespace StudComp.ViewModels.Settings;

/// <summary>
/// Диалог «Восстановление из резервной копии» (Phase 13.8, new_addons.md §13.2) — информированное
/// подтверждение вместо молчаливой замены. Показывается через <c>IDialogService.ShowEditorAsync</c>:
/// <see cref="CanSave"/> гейтит основную кнопку, для рискованных случаев (не «строго новее той же
/// линии») требует явной галочки подтверждения — приём принудительного подтверждения для опасных
/// действий, тот же принцип, что у остальных диалогов замены/удаления в приложении.
/// </summary>
public sealed partial class BackupRestoreConfirmationViewModel : ObservableObject
{
    public BackupRestoreConfirmationViewModel(BackupInspection inspection)
    {
        Inspection = inspection;
    }

    public BackupInspection Inspection { get; }

    public bool IsRisky => Inspection.Relation != BackupLineageRelation.SameLineageNewer;

    [ObservableProperty]
    private bool _acknowledged;

    public bool CanSave => !IsRisky || Acknowledged;

    partial void OnAcknowledgedChanged(bool value) => OnPropertyChanged(nameof(CanSave));

    public string LocalLineageShort => ShortId(Inspection.LocalLineageId);

    public string RemoteLineageShort => ShortId(Inspection.LineageId);

    public string LocalVersionText => Inspection.LocalVersion == 0
        ? "бэкапов ещё не было"
        : $"версия {Inspection.LocalVersion}";

    public string RemoteVersionText => Inspection.HasManifest
        ? $"версия {Inspection.Version}"
        : "версия неизвестна";

    public string RemoteCreatedText => Inspection.HasManifest
        ? Inspection.CreatedUtc.ToLocalTime().ToString(AppDateFormat.ShortDate + " HH:mm", CultureInfo.InvariantCulture)
        : "дата неизвестна";

    /// <summary>Тяжесть баннера — значение <c>Wpf.Ui.Controls.InfoBarSeverity</c> строкой (без ссылки
    /// на WPF-UI в VM: биндинг <c>ui:InfoBar.Severity</c> принимает строку через конвертер типов).</summary>
    public string Severity => Inspection.Relation switch
    {
        BackupLineageRelation.SameLineageNewer => "Success",
        BackupLineageRelation.SameLineageOlderOrEqual => "Warning",
        _ => "Error",
    };

    /// <summary>Заголовок цветного баннера — по классификации <see cref="BackupLineageComparison"/>.</summary>
    public string Headline => Inspection.Relation switch
    {
        BackupLineageRelation.SameLineageNewer =>
            $"Та же линия копий — эта копия новее на {Inspection.Version - Inspection.LocalVersion} версии",
        BackupLineageRelation.SameLineageOlderOrEqual =>
            "Та же линия копий, но эта копия не новее текущей — вы откатите более новые изменения назад",
        _ => "Другая установка или архив без сведений о линии",
    };

    public string Detail => Inspection.Relation switch
    {
        BackupLineageRelation.SameLineageNewer =>
            "Обычное восстановление вперёд по той же линии резервных копий.",
        BackupLineageRelation.SameLineageOlderOrEqual =>
            "Программа узнала эту линию, но версия в архиве не новее текущей локальной базы — " +
            "после восстановления часть уже сделанных изменений может исчезнуть.",
        _ =>
            "Программа не может подтвердить, что это копия той же установки — либо это архив с " +
            "другого компьютера, либо очень старый архив без данных о линии.",
    };

    /// <summary>Подпись основной кнопки — контекстная по риску, а не всегда «Восстановить».</summary>
    public string PrimaryButtonLabel => Inspection.Relation switch
    {
        BackupLineageRelation.SameLineageNewer => "Восстановить",
        BackupLineageRelation.SameLineageOlderOrEqual => "Всё равно откатить",
        _ => "Всё равно восстановить",
    };

    private static string ShortId(Guid id) => id == Guid.Empty ? "—" : id.ToString("N")[..8].ToUpperInvariant();
}
