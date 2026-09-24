using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;
using StudComp.ViewModels.Organizer;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>
/// Раздел «Расписание и семестр» (new_addons.md §7 §5): CRUD семестров, активный семестр, сетка
/// звонков (внутри редактора семестра), напоминание за N минут до пары. Список семестров живёт в БД —
/// перечитывается при каждом открытии раздела.
/// </summary>
public sealed partial class SemesterSettingsViewModel : SettingsSectionViewModelBase
{
    private readonly UserSettingsProvider _settings;
    private readonly IDialogService _dialogs;
    private readonly ISemesterService _semesters;
    private readonly ISubjectService _subjects;
    private readonly IToastService _toasts;

    public SemesterSettingsViewModel(
        UserSettingsProvider settings,
        IDialogService dialogs,
        ISemesterService semesters,
        ISubjectService subjects,
        IToastService toasts,
        IOptions<NotificationOptions> notifications)
    {
        _settings = settings;
        _dialogs = dialogs;
        _semesters = semesters;
        _subjects = subjects;
        _toasts = toasts;

        using (BeginLoad())
        {
            _remindMinutesBefore = notifications.Value.RemindMinutesBefore;
        }
    }

    public override SettingsSection Section => SettingsSection.Semester;

    public override string Title => "Расписание и семестр";

    public override SymbolRegular Icon => SymbolRegular.CalendarLtr24;

    public override IEnumerable<string> SearchKeywords =>
        ["семестр", "курс", "чётность", "числитель", "знаменатель", "сетка звонков", "пары", "напоминание", "активный семестр"];

    public override Task OnActivatedAsync() => RefreshSemestersAsync();

    /// <summary>Семестры для списка раздела.</summary>
    public ObservableCollection<SemesterRowViewModel> Semesters { get; } = [];

    public bool HasSemesters => Semesters.Count > 0;

    [ObservableProperty]
    private int _remindMinutesBefore;

    partial void OnRemindMinutesBeforeChanged(int value)
    {
        if (!IsLoading)
        {
            _settings.Update<NotificationOptions>(
                NotificationOptions.SectionName, o => o.RemindMinutesBefore = Math.Clamp(value, 0, 240));
        }
    }

    [RelayCommand]
    public async Task RefreshSemestersAsync()
    {
        var all = await _semesters.GetAllAsync();
        var subjects = await _subjects.GetAllAsync();

        Semesters.Clear();
        foreach (var semester in all)
        {
            Semesters.Add(new SemesterRowViewModel(semester, subjects.Count(x => x.SemesterId == semester.Id)));
        }

        OnPropertyChanged(nameof(HasSemesters));
    }

    [RelayCommand]
    private async Task AddSemesterAsync()
    {
        var editor = new SemesterEditorViewModel(existing: null);
        if (!await _dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            return;
        }

        var result = await _semesters.CreateAsync(editor.ToModel());
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось создать семестр", result.Error.Message, ToastKind.Error);
            return;
        }

        await RefreshSemestersAsync();
    }

    [RelayCommand]
    private async Task EditSemesterAsync(SemesterRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var editor = new SemesterEditorViewModel(row.Semester);
        if (!await _dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            return;
        }

        var model = editor.ToModel();
        model.IsActive = row.IsActive;

        var result = await _semesters.UpdateAsync(model);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось сохранить семестр", result.Error.Message, ToastKind.Error);
            return;
        }

        await RefreshSemestersAsync();
    }

    [RelayCommand]
    private async Task SetActiveSemesterAsync(SemesterRowViewModel? row)
    {
        if (row is null || row.IsActive)
        {
            return;
        }

        var result = await _semesters.SetActiveAsync(row.Id);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось переключить семестр", result.Error.Message, ToastKind.Error);
            return;
        }

        await RefreshSemestersAsync();
    }

    [RelayCommand]
    private async Task DeleteSemesterAsync(SemesterRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var confirmed = await _dialogs.ConfirmAsync(
            "Удалить семестр?",
            $"«{row.Name}» будет удалён. Предметы, расписание и оценки останутся, но потеряют привязку к семестру.");
        if (!confirmed)
        {
            return;
        }

        var result = await _semesters.DeleteAsync(row.Id);
        if (result.IsFailure)
        {
            _toasts.Show("Не удалось удалить семестр", result.Error.Message, ToastKind.Error);
            return;
        }

        await RefreshSemestersAsync();
    }
}
