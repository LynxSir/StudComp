using System.IO;
using System.Windows.Data;
using Microsoft.Win32;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace StudComp.Services;

/// <inheritdoc cref="IDialogService"/>
internal sealed class DialogService(IContentDialogService contentDialogService) : IDialogService
{
    public async Task<bool> ShowEditorAsync(
        object editorViewModel, string title, string primaryButton = "Сохранить", double dialogMaxWidth = 560)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = editorViewModel,
            DataContext = editorViewModel,
            PrimaryButtonText = primaryButton,
            CloseButtonText = "Отмена",
            PrimaryButtonAppearance = ControlAppearance.Primary,
            DefaultButton = ContentDialogButton.Primary,
            DialogMaxWidth = dialogMaxWidth,
        };

        // Кнопка подтверждения активна только при валидной форме (VM.CanSave).
        BindingOperations.SetBinding(
            dialog,
            ContentDialog.IsPrimaryButtonEnabledProperty,
            new Binding("CanSave") { Source = editorViewModel, FallbackValue = true });

        var result = await contentDialogService.ShowAsync(dialog, CancellationToken.None);
        return result == ContentDialogResult.Primary;
    }

    public async Task ShowInfoAsync(object viewModel, string title, double maxWidth = 760)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = viewModel,
            DataContext = viewModel,

            // Пустая строка (и только она, не null) схлопывает колонку кнопки в шаблоне WPF-UI.
            PrimaryButtonText = string.Empty,
            CloseButtonText = "Закрыть",
            DefaultButton = ContentDialogButton.Close,
            DialogMaxWidth = maxWidth,
            DialogMaxHeight = 900,
        };

        await contentDialogService.ShowAsync(dialog, CancellationToken.None);
    }

    public string? PickFolder(string title, string? initialPath = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false,
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public IReadOnlyList<string> PickOpenFiles(string title, string filter, string? initialPath = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = true,
            CheckFileExists = true,
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    public string? PickOpenFile(string title, string filter, string? initialPath = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = false,
            CheckFileExists = true,
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSaveFile(string title, string filter, string? suggestedPath = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            AddExtension = true,
            OverwritePrompt = true,
        };

        if (!string.IsNullOrWhiteSpace(suggestedPath))
        {
            var directory = Path.GetDirectoryName(suggestedPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                dialog.InitialDirectory = directory;
            }

            dialog.FileName = Path.GetFileName(suggestedPath);
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public async Task<bool> ConfirmAsync(string title, string message, string primaryButton = "Удалить")
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            PrimaryButtonText = primaryButton,
            CloseButtonText = "Отмена",
            PrimaryButtonAppearance = ControlAppearance.Danger,
            DefaultButton = ContentDialogButton.Close,
            DialogMaxWidth = 460,
        };

        var result = await contentDialogService.ShowAsync(dialog, CancellationToken.None);
        return result == ContentDialogResult.Primary;
    }
}
