using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using StudComp.Core.Common;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.ReportForge.Services;
using StudComp.Services;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>
/// Раздел «Отчёты и титульный лист» (new_addons.md §7 §7): папка вывода, автооткрытие, тип работы,
/// профиль по умолчанию, данные студента.
/// </summary>
public sealed partial class ReportSettingsViewModel : SettingsSectionViewModelBase
{
    private readonly UserSettingsProvider _settings;
    private readonly IDialogService _dialogs;
    private readonly IReportTemplateService _templates;

    public ReportSettingsViewModel(
        UserSettingsProvider settings,
        IDialogService dialogs,
        IReportTemplateService templates,
        IOptions<ReportForgeOptions> reportForge,
        IOptions<UserProfileSettings> userProfile)
    {
        _settings = settings;
        _dialogs = dialogs;
        _templates = templates;

        var r = reportForge.Value;
        var p = userProfile.Value;
        using (BeginLoad())
        {
            _reportsFolder = r.OutputFolder;
            _openReportAfterGenerate = r.OpenAfterGenerate;
            _preferSubjectSubfolder = r.PreferSubjectSubfolder;
            _defaultWorkType = r.DefaultWorkType;
            _profileFullName = p.FullName ?? string.Empty;
            _profileGroup = p.Group ?? string.Empty;
            _profileUniversity = p.University ?? string.Empty;
            _profileFaculty = p.Faculty ?? string.Empty;
            _profileDepartment = p.Department ?? string.Empty;
            _profileCity = p.City ?? string.Empty;

            Profiles.Add(new ProfileChoice(null, "Заводской (ГОСТ 7.32-2017)"));
            _selectedProfile = Profiles[0];
        }
    }

    public override SettingsSection Section => SettingsSection.Reports;

    public override string Title => "Отчёты и титульный лист";

    public override SymbolRegular Icon => SymbolRegular.DocumentText24;

    public override IEnumerable<string> SearchKeywords =>
        [
            "отчёты", "папка вывода", "автооткрытие", "тип работы", "профиль оформления",
            "титульный лист", "фио", "группа", "вуз", "факультет", "кафедра", "город",
        ];

    public override async Task OnActivatedAsync()
    {
        var all = await _templates.GetAllAsync();
        var previous = SelectedProfile?.Id;

        using (BeginLoad())
        {
            Profiles.Clear();
            Profiles.Add(new ProfileChoice(null, "Заводской (ГОСТ 7.32-2017)"));
            foreach (var template in all.Where(t => t.GostVariant != "7.32-2017"))
            {
                Profiles.Add(new ProfileChoice(template.Id, template.Name));
            }

            var target = _settings.Get<ReportForgeOptions>(ReportForgeOptions.SectionName).DefaultProfileId ?? previous;
            SelectedProfile = Profiles.FirstOrDefault(x => x.Id == target) ?? Profiles[0];
        }
    }

    /// <summary>Что показывать под заголовком карточки: свою папку либо путь по умолчанию.</summary>
    public string ReportsFolderDisplay => string.IsNullOrWhiteSpace(ReportsFolder)
        ? $"По умолчанию: {RubricaPaths.ReportsDirectory}"
        : ReportsFolder;

    /// <summary>Профили оформления для выбора «по умолчанию».</summary>
    public ObservableCollection<ProfileChoice> Profiles { get; } = [];

    [ObservableProperty]
    private string _reportsFolder;

    [ObservableProperty]
    private bool _openReportAfterGenerate;

    [ObservableProperty]
    private bool _preferSubjectSubfolder;

    [ObservableProperty]
    private string _defaultWorkType;

    [ObservableProperty]
    private ProfileChoice _selectedProfile;

    [ObservableProperty]
    private string _profileFullName;

    [ObservableProperty]
    private string _profileGroup;

    [ObservableProperty]
    private string _profileUniversity;

    [ObservableProperty]
    private string _profileFaculty;

    [ObservableProperty]
    private string _profileDepartment;

    [ObservableProperty]
    private string _profileCity;

    partial void OnReportsFolderChanged(string value)
    {
        OnPropertyChanged(nameof(ReportsFolderDisplay));
        if (!IsLoading)
        {
            _settings.Update<ReportForgeOptions>(ReportForgeOptions.SectionName, o => o.OutputFolder = value ?? string.Empty);
        }
    }

    partial void OnOpenReportAfterGenerateChanged(bool value)
    {
        if (!IsLoading)
        {
            _settings.Update<ReportForgeOptions>(ReportForgeOptions.SectionName, o => o.OpenAfterGenerate = value);
        }
    }

    partial void OnPreferSubjectSubfolderChanged(bool value)
    {
        if (!IsLoading)
        {
            _settings.Update<ReportForgeOptions>(ReportForgeOptions.SectionName, o => o.PreferSubjectSubfolder = value);
        }
    }

    partial void OnDefaultWorkTypeChanged(string value)
    {
        if (!IsLoading)
        {
            var text = string.IsNullOrWhiteSpace(value) ? "Отчёт по лабораторной работе" : value.Trim();
            _settings.Update<ReportForgeOptions>(ReportForgeOptions.SectionName, o => o.DefaultWorkType = text);
        }
    }

    partial void OnSelectedProfileChanged(ProfileChoice value)
    {
        if (!IsLoading && value is not null)
        {
            _settings.Update<ReportForgeOptions>(ReportForgeOptions.SectionName, o => o.DefaultProfileId = value.Id);
        }
    }

    partial void OnProfileFullNameChanged(string value) => UpdateProfile(o => o.FullName = Trimmed(value));

    partial void OnProfileGroupChanged(string value) => UpdateProfile(o => o.Group = Trimmed(value));

    partial void OnProfileUniversityChanged(string value) => UpdateProfile(o => o.University = Trimmed(value));

    partial void OnProfileFacultyChanged(string value) => UpdateProfile(o => o.Faculty = Trimmed(value));

    partial void OnProfileDepartmentChanged(string value) => UpdateProfile(o => o.Department = Trimmed(value));

    partial void OnProfileCityChanged(string value) => UpdateProfile(o => o.City = Trimmed(value));

    [RelayCommand]
    private void PickReportsFolder()
    {
        var initial = string.IsNullOrWhiteSpace(ReportsFolder) ? RubricaPaths.ReportsDirectory : ReportsFolder;
        if (_dialogs.PickFolder("Папка для отчётов", initial) is { } picked)
        {
            ReportsFolder = picked;
        }
    }

    private void UpdateProfile(Action<UserProfileSettings> mutate)
    {
        if (!IsLoading)
        {
            _settings.Update(UserProfileSettings.SectionName, mutate);
        }
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Пункт выбора профиля оформления по умолчанию.</summary>
public sealed record ProfileChoice(Guid? Id, string Name);
