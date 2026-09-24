using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;
using StudComp.Services;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>Раздел «Учебная папка» (new_addons.md §7 §3): путь Study Root, шаблон подпапок предмета.</summary>
public sealed partial class WorkspaceSettingsViewModel : SettingsSectionViewModelBase
{
    private readonly UserSettingsProvider _settings;
    private readonly IDialogService _dialogs;
    private readonly IShellLauncher _shellLauncher;

    private string _previousRoot = string.Empty;

    public WorkspaceSettingsViewModel(
        UserSettingsProvider settings,
        IDialogService dialogs,
        IShellLauncher shellLauncher,
        IOptions<WorkspaceOptions> workspace)
    {
        _settings = settings;
        _dialogs = dialogs;
        _shellLauncher = shellLauncher;

        using (BeginLoad())
        {
            _studyRootPath = workspace.Value.StudyRootPath;
            _previousRoot = _studyRootPath;
            _selectedRootChangeBehavior = RootChangeChoices.FirstOrDefault(x => x.Value == workspace.Value.OnRootChange)
                ?? RootChangeChoices[0];

            foreach (var sub in (workspace.Value.SubjectFolderTemplate ?? []).Where(s => !string.IsNullOrWhiteSpace(s)))
            {
                SubjectFolderTemplate.Add(sub);
            }
        }
    }

    public override SettingsSection Section => SettingsSection.Workspace;

    public override string Title => "Учебная папка";

    public override SymbolRegular Icon => SymbolRegular.Folder24;

    public override IEnumerable<string> SearchKeywords =>
        ["учебная папка", "study root", "путь", "подпапки", "предмет", "скелет", "перенос", "смена корня"];

    /// <summary>Варианты поведения при смене корня.</summary>
    public IReadOnlyList<RootChangeChoice> RootChangeChoices { get; } =
    [
        new(RootChangeBehavior.DoNothing, "Ничего не делать"),
        new(RootChangeBehavior.OfferMove, "Предложить перенести файлы вручную"),
    ];

    /// <summary>Шаблон подпапок, создаваемых внутри папки нового предмета.</summary>
    public ObservableCollection<string> SubjectFolderTemplate { get; } = [];

    public bool HasStudyRoot => !string.IsNullOrWhiteSpace(StudyRootPath);

    [ObservableProperty]
    private string _studyRootPath;

    [ObservableProperty]
    private RootChangeChoice _selectedRootChangeBehavior;

    [ObservableProperty]
    private string _newSubjectFolder = string.Empty;

    partial void OnStudyRootPathChanged(string value)
    {
        OnPropertyChanged(nameof(HasStudyRoot));
        if (IsLoading)
        {
            _previousRoot = value ?? string.Empty;
            return;
        }

        _settings.Update<WorkspaceOptions>(WorkspaceOptions.SectionName, o => o.StudyRootPath = value ?? string.Empty);

        if (SelectedRootChangeBehavior?.Value == RootChangeBehavior.OfferMove
            && !string.IsNullOrWhiteSpace(_previousRoot)
            && !string.Equals(_previousRoot, value, StringComparison.OrdinalIgnoreCase))
        {
            _ = OfferMoveAsync(_previousRoot, value ?? string.Empty);
        }

        _previousRoot = value ?? string.Empty;
    }

    partial void OnSelectedRootChangeBehaviorChanged(RootChangeChoice value)
    {
        if (!IsLoading && value is not null)
        {
            _settings.Update<WorkspaceOptions>(WorkspaceOptions.SectionName, o => o.OnRootChange = value.Value);
        }
    }

    [RelayCommand]
    private void PickStudyRoot()
    {
        var initial = string.IsNullOrWhiteSpace(StudyRootPath) ? null : StudyRootPath;
        if (_dialogs.PickFolder("Учебная папка", initial) is { } picked)
        {
            StudyRootPath = picked;
        }
    }

    [RelayCommand]
    private void OpenStudyRoot()
    {
        if (!string.IsNullOrWhiteSpace(StudyRootPath))
        {
            _shellLauncher.RevealInExplorer(StudyRootPath);
        }
    }

    [RelayCommand]
    private void AddSubjectFolder()
    {
        var name = NewSubjectFolder?.Trim();
        if (string.IsNullOrWhiteSpace(name) || SubjectFolderTemplate.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        SubjectFolderTemplate.Add(name);
        NewSubjectFolder = string.Empty;
        PersistSubjectFolderTemplate();
    }

    [RelayCommand]
    private void RemoveSubjectFolder(string? folder)
    {
        if (folder is not null && SubjectFolderTemplate.Remove(folder))
        {
            PersistSubjectFolderTemplate();
        }
    }

    private void PersistSubjectFolderTemplate()
    {
        if (IsLoading)
        {
            return;
        }

        var list = SubjectFolderTemplate.ToList();
        _settings.Update<WorkspaceOptions>(WorkspaceOptions.SectionName, o => o.SubjectFolderTemplate = list);
    }

    private async Task OfferMoveAsync(string oldRoot, string newRoot)
    {
        var move = await _dialogs.ConfirmAsync(
            "Учебная папка изменена",
            $"Новые файлы теперь раскладываются в «{newRoot}». Прежнее содержимое осталось в «{oldRoot}» — " +
            "перенесите его вручную, если нужно. Открыть обе папки в проводнике?",
            primaryButton: "Открыть обе");

        if (move)
        {
            _shellLauncher.RevealInExplorer(oldRoot);
            _shellLauncher.RevealInExplorer(newRoot);
        }
    }
}

/// <summary>Пункт выбора поведения при смене корня учебной папки.</summary>
public sealed record RootChangeChoice(RootChangeBehavior Value, string Display);
