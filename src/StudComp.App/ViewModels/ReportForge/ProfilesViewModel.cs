using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.ReportForge.Services;
using StudComp.Services;

namespace StudComp.ViewModels.ReportForge;

/// <summary>
/// Вкладка «Профили оформления» раздела «Отчёты» (ARCHITECTURE §10.4, Phase 9). Master–detail:
/// <see cref="ActiveEditor"/> = <see langword="null"/> — список профилей, иначе форма редактирования.
/// </summary>
public sealed partial class ProfilesViewModel(
    IReportTemplateService templates,
    IDialogService dialogs,
    IToastService toasts) : ObservableObject
{
    public ObservableCollection<ProfileRowViewModel> Profiles { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Открытая форма редактора либо <see langword="null"/> — тогда показывается список.</summary>
    [ObservableProperty]
    private ProfileEditorViewModel? _activeEditor;

    public bool IsListVisible => ActiveEditor is null;

    partial void OnActiveEditorChanged(ProfileEditorViewModel? value) => OnPropertyChanged(nameof(IsListVisible));

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var all = await templates.GetAllAsync();

            // Сначала собрать строки (внутри — await профиля на шаблон), затем заменить коллекцию
            // одним синхронным блоком: наложившиеся обновления иначе давали бы дубли (Phase 13.10).
            var rows = new List<ProfileRowViewModel>(all.Count);
            foreach (var template in all)
            {
                var profile = await templates.GetProfileAsync(template.Id);
                rows.Add(new ProfileRowViewModel(template, profile));
            }

            Profiles.Clear();
            foreach (var row in rows)
            {
                Profiles.Add(row);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Клонировать встроенный заводской ГОСТ-профиль в новый пользовательский шаблон.</summary>
    [RelayCommand]
    private Task CloneDefaultAsync() => CloneAndEditAsync(null, "Копия ГОСТ 7.32-2017");

    /// <summary>Клонировать выбранный профиль.</summary>
    [RelayCommand]
    private Task CloneAsync(ProfileRowViewModel? row) =>
        row is null ? Task.CompletedTask : CloneAndEditAsync(row.Id, $"{row.Name} — копия");

    [RelayCommand]
    private async Task EditAsync(ProfileRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var profile = await templates.GetProfileAsync(row.Id);
        ActiveEditor = new ProfileEditorViewModel(row.Template, profile);
    }

    [RelayCommand]
    private async Task DeleteAsync(ProfileRowViewModel? row)
    {
        if (row is null || row.IsFactoryDefault)
        {
            return;
        }

        if (await templates.IsInUseAsync(row.Id))
        {
            toasts.Show(
                "Профиль используется",
                $"«{row.Name}» уже применён в сгенерированных отчётах — его нельзя удалить.",
                ToastKind.Warning);
            return;
        }

        if (!await dialogs.ConfirmAsync(
                "Удалить профиль?",
                $"«{row.Name}» будет удалён. Предметы с этим профилем вернутся к заводскому ГОСТ."))
        {
            return;
        }

        if (await templates.DeleteAsync(row.Id))
        {
            toasts.Show("Профиль удалён", row.Name, ToastKind.Success);
            await RefreshAsync();
        }
        else
        {
            toasts.Show("Не удалось удалить", row.Name, ToastKind.Error);
        }
    }

    [RelayCommand]
    private async Task SaveEditorAsync()
    {
        if (ActiveEditor is not { CanSave: true } editor)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await templates.SaveAsync(editor.TemplateId, editor.Name, editor.ToProfile());
            toasts.Show("Профиль сохранён", editor.Name, ToastKind.Success);
            ActiveEditor = null;
            await RefreshAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void CancelEditor() => ActiveEditor = null;

    private async Task CloneAndEditAsync(Guid? sourceId, string name)
    {
        IsBusy = true;
        try
        {
            var clone = await templates.CloneAsync(sourceId, name);
            var profile = await templates.GetProfileAsync(clone.Id);
            await RefreshAsync();
            ActiveEditor = new ProfileEditorViewModel(clone, profile);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
