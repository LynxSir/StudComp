using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Options;
using StudComp.Core.Common;
using StudComp.Infrastructure.Backup;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Services;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>
/// Раздел «Данные и резервные копии» (new_addons.md §7 §9): пути к БД/логам, бэкап/восстановление,
/// ретеншн, очистка временных файлов.
/// </summary>
public sealed partial class DataSettingsViewModel : SettingsSectionViewModelBase
{
    private readonly UserSettingsProvider _settings;
    private readonly IBackupService _backup;
    private readonly IDialogService _dialogs;
    private readonly IShellLauncher _shellLauncher;
    private readonly IToastService _toasts;
    private readonly IMessenger _messenger;

    public DataSettingsViewModel(
        UserSettingsProvider settings,
        IBackupService backup,
        IDialogService dialogs,
        IShellLauncher shellLauncher,
        IToastService toasts,
        IMessenger messenger,
        IOptions<DataOptions> data)
    {
        _settings = settings;
        _backup = backup;
        _dialogs = dialogs;
        _shellLauncher = shellLauncher;
        _toasts = toasts;
        _messenger = messenger;

        using (BeginLoad())
        {
            _activityRetentionDays = data.Value.ActivityRetentionDays;
            _activityRetentionKeepCount = data.Value.ActivityRetentionKeepCount;
            _operationLogRetentionDays = data.Value.OperationLogRetentionDays;
            _lineageText = BuildLineageText(data.Value);
        }
    }

    public override SettingsSection Section => SettingsSection.Data;

    public override string Title => "Данные и резервные копии";

    public override SymbolRegular Icon => SymbolRegular.Database24;

    public override IEnumerable<string> SearchKeywords =>
        [
            "данные", "база данных", "бэкап", "резервная копия", "восстановление", "лог", "путь",
            "ретеншн", "хранение", "временные файлы", "кэш", "экспорт правил", "импорт правил",
        ];

    public string DatabaseFilePath => RubricaPaths.DatabaseFile;

    public string LogsDirectoryPath => RubricaPaths.LogsDirectory;

    [ObservableProperty]
    private int _activityRetentionDays;

    [ObservableProperty]
    private int _activityRetentionKeepCount;

    [ObservableProperty]
    private int _operationLogRetentionDays;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _statusText;

    /// <summary>Линия/версия резервных копий этой установки (Phase 13.8) — обновляется сразу после
    /// успешного бэкапа/восстановления, а не только при открытии раздела (<see cref="IOptions{T}.Value"/>
    /// кэшируется на момент резолва DI и не видит более поздних изменений).</summary>
    [ObservableProperty]
    private string _lineageText;

    partial void OnActivityRetentionDaysChanged(int value) => Persist(o => o.ActivityRetentionDays = Math.Max(1, value));

    partial void OnActivityRetentionKeepCountChanged(int value) => Persist(o => o.ActivityRetentionKeepCount = Math.Max(0, value));

    partial void OnOperationLogRetentionDaysChanged(int value) => Persist(o => o.OperationLogRetentionDays = Math.Max(1, value));

    [RelayCommand]
    private void OpenDatabaseFolder() => _shellLauncher.RevealInExplorer(RubricaPaths.DatabaseFile);

    [RelayCommand]
    private void OpenLogsFolder() => _shellLauncher.RevealInExplorer(RubricaPaths.LogsDirectory);

    [RelayCommand]
    private void OpenRuleExportImport() => _messenger.Send(new OpenSettingsMessage(SettingsSection.Archivist));

    [RelayCommand(CanExecute = nameof(CanRunBackupTask))]
    private async Task BackupAsync()
    {
        Directory.CreateDirectory(RubricaPaths.BackupsDirectory);
        var suggested = Path.Combine(
            RubricaPaths.BackupsDirectory,
            $"rubrica-backup-{DateTime.Now:yyyyMMdd-HHmmss}.zip");

        var path = _dialogs.PickSaveFile("Резервная копия", "Zip-архив (*.zip)|*.zip", suggested);
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        BackupCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        try
        {
            var progress = new Progress<BackupProgress>(p => StatusText = $"{p.Stage}…");
            var result = await Task.Run(() => _backup.BackupAsync(path, progress));
            if (result.IsSuccess)
            {
                StatusText = null;
                LineageText = BuildLineageText(_settings.Get<DataOptions>(DataOptions.SectionName));
                _toasts.Show("Резервная копия создана", Path.GetFileName(path), ToastKind.Success);
            }
            else
            {
                StatusText = null;
                _toasts.Show("Не удалось создать копию", result.Error.Message, ToastKind.Error);
            }
        }
        finally
        {
            IsBusy = false;
            BackupCommand.NotifyCanExecuteChanged();
            RestoreCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRunBackupTask))]
    private async Task RestoreAsync()
    {
        var path = _dialogs.PickOpenFile("Восстановить из копии", "Zip-архив (*.zip)|*.zip", RubricaPaths.BackupsDirectory);
        if (path is null)
        {
            return;
        }

        IsBusy = true;
        BackupCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        Result<BackupInspection> inspection;
        try
        {
            inspection = await _backup.InspectAsync(path);
        }
        finally
        {
            IsBusy = false;
            BackupCommand.NotifyCanExecuteChanged();
            RestoreCommand.NotifyCanExecuteChanged();
        }

        if (inspection.IsFailure)
        {
            _toasts.Show("Не удалось прочитать архив", inspection.Error.Message, ToastKind.Error);
            return;
        }

        string? studyParentDirectory = null;
        if (inspection.Value.IncludesFiles)
        {
            studyParentDirectory = _dialogs.PickFolder("Куда восстановить учебные файлы (будет создана новая подпапка)",
                _settings.Get<WorkspaceOptions>(WorkspaceOptions.SectionName).StudyRootPath);
            if (studyParentDirectory is null) return;
        }

        // Информированное подтверждение вместо молчаливой замены (Phase 13.8, new_addons.md §13.2):
        // диалог показывает линию/версию текущей базы и архива и требует явной галочки для
        // рискованных случаев (другая установка либо откат на не более новую версию).
        var confirmVm = new BackupRestoreConfirmationViewModel(inspection.Value);
        var confirmed = await _dialogs.ShowEditorAsync(
            confirmVm, "Восстановление из резервной копии", confirmVm.PrimaryButtonLabel, dialogMaxWidth: 640);
        if (!confirmed)
        {
            return;
        }

        IsBusy = true;
        BackupCommand.NotifyCanExecuteChanged();
        RestoreCommand.NotifyCanExecuteChanged();
        try
        {
            var progress = new Progress<BackupProgress>(p => StatusText = $"{p.Stage}…");
            var result = await Task.Run(() => _backup.RestoreAsync(path, progress, studyParentDirectory: studyParentDirectory));
            StatusText = null;

            if (result.IsFailure)
            {
                _toasts.Show("Не удалось восстановить", result.Error.Message, ToastKind.Error);
                return;
            }

            LineageText = BuildLineageText(_settings.Get<DataOptions>(DataOptions.SectionName));

            var restart = await _dialogs.ConfirmAsync(
                "Восстановление завершено",
                "Данные восстановлены. " + (result.Value.StudyRootPath is { } root ? $"Учебные файлы: {root}. " : string.Empty)
                + "Архивариус выключен: проверьте его папки перед включением. Перезапустите приложение, чтобы изменения вступили в силу.",
                primaryButton: "Перезапустить сейчас");
            if (restart)
            {
                RestartApplication();
            }
        }
        finally
        {
            IsBusy = false;
            BackupCommand.NotifyCanExecuteChanged();
            RestoreCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private async Task ClearTemporaryFilesAsync()
    {
        var (count, bytes) = await Task.Run(RemoveTemporaryArtifacts);
        _toasts.Show(
            "Временные файлы очищены",
            count == 0 ? "Нечего удалять." : $"Удалено объектов: {count}. Освобождено ~{bytes / (1024 * 1024)} МБ.",
            ToastKind.Success);
    }

    private bool CanRunBackupTask() => !IsBusy;

    private static (int Count, long Bytes) RemoveTemporaryArtifacts()
    {
        var count = 0;
        long bytes = 0;
        var temp = Path.GetTempPath();
        var cutoff = DateTime.Now.AddDays(-1);

        void TryFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    bytes += new FileInfo(path).Length;
                    File.Delete(path);
                    count++;
                }
            }
            catch
            {
                // занятый/недоступный файл — пропускаем
            }
        }

        void TryDirectory(string path)
        {
            try
            {
                if (!Directory.Exists(path) || Directory.GetLastWriteTime(path) > cutoff)
                {
                    return;
                }

                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        bytes += new FileInfo(file).Length;
                    }
                    catch
                    {
                        // размер не критичен
                    }
                }

                Directory.Delete(path, recursive: true);
                count++;
            }
            catch
            {
                // занятый каталог — пропускаем
            }
        }

        TryFile(RubricaPaths.UserSettingsFile + ".tmp");
        TryFile(Path.Combine(temp, "rubrica-design.db"));

        foreach (var prefix in new[] { "rubrica-backup-", "rubrica-restore-", "rubrica-import-", "rubrica-ws-" })
        {
            foreach (var dir in SafeEnumerateDirectories(temp, prefix + "*"))
            {
                TryDirectory(dir);
            }
        }

        return (count, bytes);
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateDirectories(root, pattern);
        }
        catch
        {
            return [];
        }
    }

    private static void RestartApplication()
    {
        var exe = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exe))
        {
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
        }

        Application.Current.Shutdown();
    }

    private void Persist(Action<DataOptions> mutate)
    {
        if (!IsLoading)
        {
            _settings.Update(DataOptions.SectionName, mutate);
        }
    }

    private static string BuildLineageText(DataOptions data)
    {
        if (data.BackupLineageId == Guid.Empty)
        {
            return "Резервных копий этой установки ещё не было.";
        }

        var shortId = data.BackupLineageId.ToString("N")[..8].ToUpperInvariant();
        return $"Линия {shortId} · версия {data.BackupVersion}";
    }
}
