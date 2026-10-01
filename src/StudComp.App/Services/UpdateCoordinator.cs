using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Notifications;
using StudComp.Infrastructure.Settings;
using StudComp.Infrastructure.Startup;
using Wpf.Ui;
using Wpf.Ui.Controls;
using TextBlock = System.Windows.Controls.TextBlock;

namespace StudComp.Services;

public enum UpdateFlowKind
{
    Unsupported,
    UpToDate,
    Skipped,
    Cancelled,
    Scheduled,
    Restarting,
    Failed,
}

public sealed record UpdateFlowResult(UpdateFlowKind Kind, string Message);

/// <summary>
/// Единый пользовательский сценарий обновления: проверка, описание версии, скачивание с прогрессом
/// и выбор момента установки. Все диалоги гарантированно открываются в UI-потоке.
/// </summary>
public sealed class UpdateCoordinator(
    IUpdateService updates,
    IContentDialogService dialogs,
    IToastService toasts,
    UserSettingsProvider settings,
    IOptionsMonitor<UpdateOptions> options,
    ILogger<UpdateCoordinator> logger)
{
    private static readonly Regex MarkdownLink = new(@"\[([^\]]+)]\([^)]+\)", RegexOptions.Compiled);
    private readonly SemaphoreSlim _flowGate = new(1, 1);

    public Task<UpdateFlowResult> CheckAndPromptAsync(
        bool respectSkippedVersion,
        CancellationToken cancellationToken = default)
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.CheckAccess())
        {
            return CheckAndPromptOnUiAsync(respectSkippedVersion, cancellationToken);
        }

        return app.Dispatcher
            .InvokeAsync(() => CheckAndPromptOnUiAsync(respectSkippedVersion, cancellationToken))
            .Task
            .Unwrap();
    }

    private async Task<UpdateFlowResult> CheckAndPromptOnUiAsync(
        bool respectSkippedVersion,
        CancellationToken cancellationToken)
    {
        await _flowGate.WaitAsync(cancellationToken);
        try
        {
            if (!updates.IsUpdateSupported)
            {
                return new UpdateFlowResult(
                    UpdateFlowKind.Unsupported,
                    "Проверка обновлений доступна в версии, установленной через Rubrica Setup.");
            }

            var update = await updates.CheckAsync(cancellationToken);
            if (update is null)
            {
                return new UpdateFlowResult(UpdateFlowKind.UpToDate, "Установлена последняя версия.");
            }

            if (respectSkippedVersion
                && string.Equals(
                    options.CurrentValue.SkippedVersion,
                    update.Version,
                    StringComparison.OrdinalIgnoreCase))
            {
                return new UpdateFlowResult(
                    UpdateFlowKind.Skipped,
                    $"Версия {update.Version} ранее была пропущена.");
            }

            var choice = await ShowUpdatePromptAsync(update, cancellationToken);
            if (choice == ContentDialogResult.None)
            {
                settings.Update<UpdateOptions>(
                    UpdateOptions.SectionName,
                    value => value.SkippedVersion = update.Version);
                return new UpdateFlowResult(
                    UpdateFlowKind.Skipped,
                    $"Версия {update.Version} пропущена. Следующая версия снова будет предложена.");
            }

            var downloaded = await ShowDownloadProgressAsync(update, cancellationToken);
            if (!downloaded)
            {
                return new UpdateFlowResult(UpdateFlowKind.Cancelled, "Скачивание обновления отменено.");
            }

            settings.Update<UpdateOptions>(
                UpdateOptions.SectionName,
                value => value.SkippedVersion = string.Empty);
            updates.RequestApplyOnExit(update);

            if (choice == ContentDialogResult.Secondary)
            {
                var message = $"Версия {update.Version} скачана и установится при выходе из Rubrica.";
                toasts.Show("Обновление готово", message, ToastKind.Success);
                return new UpdateFlowResult(UpdateFlowKind.Scheduled, message);
            }

            Application.Current.Shutdown();
            return new UpdateFlowResult(UpdateFlowKind.Restarting, "Устанавливаем обновление и перезапускаем Rubrica…");
        }
        catch (OperationCanceledException)
        {
            return new UpdateFlowResult(UpdateFlowKind.Cancelled, "Проверка обновлений отменена.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось завершить сценарий обновления");
            toasts.Show(
                "Не удалось обновить Rubrica",
                "Проверьте подключение к интернету и повторите попытку позже.",
                ToastKind.Warning);
            return new UpdateFlowResult(
                UpdateFlowKind.Failed,
                "Не удалось проверить или скачать обновление. Попробуйте ещё раз позже.");
        }
        finally
        {
            _flowGate.Release();
        }
    }

    private async Task<ContentDialogResult> ShowUpdatePromptAsync(
        AppUpdateInfo update,
        CancellationToken cancellationToken)
    {
        var notes = new TextBlock
        {
            Text = ToReadableReleaseNotes(update.ReleaseNotes),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
        };

        var notesBox = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14, 12, 14, 12),
            Margin = new Thickness(0, 12, 0, 0),
            Child = new ScrollViewer
            {
                MaxHeight = 280,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = notes,
            },
        };
        notesBox.SetResourceReference(Border.BackgroundProperty, "ControlFillColorDefaultBrush");

        var content = new StackPanel { Width = 540 };
        content.Children.Add(new TextBlock
        {
            Text = $"Rubrica {update.Version} готова к установке",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
        });
        content.Children.Add(new TextBlock
        {
            Text = $"Загрузка: {FormatBytes(update.DownloadSizeBytes)}"
                   + (update.IsDelta ? " • компактное обновление" : string.Empty),
            Margin = new Thickness(0, 5, 0, 0),
            Opacity = 0.7,
        });
        content.Children.Add(notesBox);
        content.Children.Add(new TextBlock
        {
            Text = "Можно обновиться сейчас или скачать обновление и установить его при обычном выходе.",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0),
            Opacity = 0.75,
        });

        var dialog = new ContentDialog
        {
            Title = "Доступно обновление",
            Content = content,
            PrimaryButtonText = "Обновить сейчас",
            SecondaryButtonText = "При закрытии",
            CloseButtonText = "Пропустить",
            PrimaryButtonAppearance = ControlAppearance.Primary,
            DefaultButton = ContentDialogButton.Primary,
            DialogMaxWidth = 620,
            DialogMaxHeight = 720,
        };

        return await dialogs.ShowAsync(dialog, cancellationToken);
    }

    private async Task<bool> ShowDownloadProgressAsync(
        AppUpdateInfo update,
        CancellationToken cancellationToken)
    {
        using var downloadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 6,
            Margin = new Thickness(0, 16, 0, 8),
        };
        var status = new TextBlock { Text = "Подготавливаем загрузку…", Opacity = 0.75 };
        var content = new StackPanel { Width = 500 };
        content.Children.Add(new TextBlock
        {
            Text = $"Скачиваем Rubrica {update.Version}",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
        });
        content.Children.Add(progressBar);
        content.Children.Add(status);

        var dialog = new ContentDialog
        {
            Title = "Обновление Rubrica",
            Content = content,
            PrimaryButtonText = string.Empty,
            CloseButtonText = "Отмена",
            DefaultButton = ContentDialogButton.Close,
            DialogMaxWidth = 580,
        };

        var isOpen = false;
        dialog.Opened += async (_, _) =>
        {
            isOpen = true;
            try
            {
                var progress = new Progress<int>(value =>
                {
                    var safeValue = Math.Clamp(value, 0, 100);
                    progressBar.Value = safeValue;
                    status.Text = $"Загружено {safeValue}%";
                });
                await updates.DownloadAsync(update, progress, downloadCancellation.Token);
                completion.TrySetResult(true);
                if (isOpen)
                {
                    dialog.Hide(ContentDialogResult.Primary);
                }
            }
            catch (OperationCanceledException)
            {
                completion.TrySetResult(false);
                if (isOpen)
                {
                    dialog.Hide(ContentDialogResult.None);
                }
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
                if (isOpen)
                {
                    dialog.Hide(ContentDialogResult.None);
                }
            }
        };
        dialog.Closing += (_, args) =>
        {
            if (!completion.Task.IsCompleted && args.Result == ContentDialogResult.None)
            {
                downloadCancellation.Cancel();
            }
        };
        dialog.Closed += (_, _) => isOpen = false;

        await dialogs.ShowAsync(dialog, CancellationToken.None);
        if (!completion.Task.IsCompleted)
        {
            downloadCancellation.Cancel();
        }

        return await completion.Task;
    }

    private static string ToReadableReleaseNotes(string markdown)
    {
        var result = MarkdownLink.Replace(markdown, "$1")
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);

        var lines = result.ReplaceLineEndings("\n")
            .Split('\n')
            .Select(line => line.Trim())
            .Select(line => line.TrimStart('#').Trim())
            .Select(line => line.StartsWith("- ", StringComparison.Ordinal) ? $"• {line[2..]}" : line);
        return string.Join(Environment.NewLine, lines).Trim();
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "размер уточняется";
        }

        const double megabyte = 1024 * 1024;
        return bytes >= 1024 * megabyte
            ? $"{bytes / (1024 * megabyte):0.0} ГБ"
            : $"{bytes / megabyte:0.#} МБ";
    }
}
