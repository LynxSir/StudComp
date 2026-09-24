using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Common;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Archivist.Services;
using StudComp.Modules.Organizer.Services;
using StudComp.Services;

namespace StudComp.ViewModels.Archivist;

/// <summary>
/// Вкладка «Правила» (ARCHITECTURE §8.7): список правил с перестановкой приоритета (drag-n-drop и
/// кнопки ↑/↓), редактор с live-preview, экспорт/импорт набора в JSON.
/// </summary>
public sealed partial class RulesViewModel(
    IArchivistRuleService rules,
    IRulePreviewService preview,
    ISubjectService subjects,
    IFileWatcherService watcher,
    IDialogService dialogs,
    IToastService toasts) : ObservableObject
{
    public ObservableCollection<RuleRowViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsBusy = true;
        try
        {
            var subjectList = await subjects.GetAllAsync();
            var byId = subjectList.ToDictionary(s => s.Id);

            var all = await rules.GetAllAsync();

            Items.Clear();
            foreach (var rule in all)
            {
                var subject = rule.SubjectId is { } id ? byId.GetValueOrDefault(id) : null;
                Items.Add(new RuleRowViewModel(rule, subject));
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        var subjectList = await subjects.GetAllAsync();
        var editor = new RuleEditorViewModel(subjectList, watcher.ConfiguredFolders, existing: null, preview);
        if (await dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            await ApplyAsync(async () => (await rules.CreateAsync(editor.ToModel())).WithoutValue(),
                "Не удалось создать правило");
        }
    }

    [RelayCommand]
    private async Task EditAsync(RuleRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var subjectList = await subjects.GetAllAsync();
        var editor = new RuleEditorViewModel(subjectList, watcher.ConfiguredFolders, row.Rule, preview);
        if (await dialogs.ShowEditorAsync(editor, editor.HeaderText))
        {
            await ApplyAsync(() => rules.UpdateAsync(editor.ToModel()), "Не удалось сохранить правило");
        }
    }

    [RelayCommand]
    private async Task ToggleEnabledAsync(RuleRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        await ApplyAsync(() => rules.SetEnabledAsync(row.Rule.Id, !row.Rule.Enabled),
            "Не удалось изменить правило");
    }

    [RelayCommand]
    private async Task DeleteAsync(RuleRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        var confirmed = await dialogs.ConfirmAsync(
            "Удалить правило?",
            $"«{row.ConditionText}» больше не будет применяться. Уже разложенные файлы останутся на местах.");
        if (!confirmed)
        {
            return;
        }

        await ApplyAsync(() => rules.DeleteAsync(row.Rule.Id), "Не удалось удалить правило");
    }

    [RelayCommand]
    private Task MoveUpAsync(RuleRowViewModel? row) => MoveAsync(row, -1);

    [RelayCommand]
    private Task MoveDownAsync(RuleRowViewModel? row) => MoveAsync(row, +1);

    /// <summary>Зафиксировать порядок, полученный перетаскиванием (вызывается поведением списка).</summary>
    [RelayCommand]
    private Task ReorderCommittedAsync() => PersistOrderAsync();

    [RelayCommand]
    private async Task ExportAsync()
    {
        var path = dialogs.PickSaveFile(
            "Сохранить правила в файл", "Правила Rubrica (*.json)|*.json", "rubrica-rules.json");
        if (path is null)
        {
            return;
        }

        var result = await rules.ExportToFileAsync(path);
        if (result.IsFailure)
        {
            toasts.Show("Экспорт не удался", result.Error.Message, ToastKind.Error);
        }
        else
        {
            toasts.Show("Правила сохранены", $"Выгружено правил: {result.Value}.", ToastKind.Success);
        }
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var path = dialogs.PickOpenFile("Выбрать файл с правилами", "Правила Rubrica (*.json)|*.json");
        if (path is null)
        {
            return;
        }

        var options = new RulesImportViewModel();
        if (!await dialogs.ShowEditorAsync(options, "Импорт правил", "Импортировать"))
        {
            return;
        }

        var result = await rules.ImportFromFileAsync(path, options.Mode);
        if (result.IsFailure)
        {
            toasts.Show("Импорт не удался", result.Error.Message, ToastKind.Error);
            return;
        }

        var summary = result.Value;
        var kind = summary.Skipped > 0 || summary.Warnings.Count > 0 ? ToastKind.Warning : ToastKind.Success;
        var text = $"Добавлено правил: {summary.Imported}."
            + (summary.Skipped > 0 ? $" Пропущено: {summary.Skipped}." : string.Empty);
        toasts.Show("Правила импортированы", text, kind);

        foreach (var warning in summary.Warnings.Take(3))
        {
            toasts.Show("Импорт правил", warning, ToastKind.Warning);
        }

        watcher.RequestRescan();
        await RefreshAsync();
    }

    private async Task MoveAsync(RuleRowViewModel? row, int delta)
    {
        if (row is null)
        {
            return;
        }

        var index = Items.IndexOf(row);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= Items.Count)
        {
            return;
        }

        Items.Move(index, target);
        await PersistOrderAsync();
    }

    private async Task PersistOrderAsync()
    {
        var orderedIds = Items.Select(i => i.Rule.Id).ToList();
        var result = await rules.ReorderAsync(orderedIds);
        if (result.IsFailure)
        {
            toasts.Show("Не удалось изменить порядок", result.Error.Message, ToastKind.Error);
        }

        watcher.RequestRescan();
        await RefreshAsync();
    }

    private async Task ApplyAsync(Func<Task<Result>> action, string errorTitle)
    {
        var result = await action();
        if (result.IsFailure)
        {
            toasts.Show(errorTitle, result.Error.Message, ToastKind.Error);
            return;
        }

        // Новые правила должны сразу примениться к тому, что уже лежит в наблюдаемой папке.
        watcher.RequestRescan();
        await RefreshAsync();
    }
}
