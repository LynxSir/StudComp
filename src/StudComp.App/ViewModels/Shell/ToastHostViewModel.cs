using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Infrastructure.Notifications;

namespace StudComp.ViewModels.Shell;

/// <summary>
/// Слой всплывающих карточек-тостов внутри окна (ARCHITECTURE §11.5). Держит очередь видимых тостов;
/// каждая карточка сама снимает себя по таймеру или по кнопке. Используется для тостов с действием
/// («Файл отсортирован · Отменить») — балун трея кнопку показать не умеет.
/// </summary>
public sealed class ToastHostViewModel : ObservableObject
{
    private const int MaxVisible = 4;

    public ObservableCollection<ToastViewModel> Toasts { get; } = [];

    /// <summary>Показать тост. Вызывать только на UI-потоке.</summary>
    public void Push(string title, string message, ToastKind kind, string? actionLabel, Func<Task>? action)
    {
        while (Toasts.Count >= MaxVisible)
        {
            Dismiss(Toasts[0]);
        }

        var toast = new ToastViewModel(title, message, kind, actionLabel, action);
        toast.Dismissed += (_, _) => Dismiss(toast);
        Toasts.Add(toast);
        toast.Begin();
    }

    private void Dismiss(ToastViewModel toast)
    {
        toast.Cancel();
        Toasts.Remove(toast);
    }
}

/// <summary>Одна карточка-тост: текст, акцент по <see cref="ToastKind"/> и, возможно, кнопка-действие.</summary>
public sealed partial class ToastViewModel : ObservableObject
{
    private static readonly TimeSpan PlainLifetime = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan ActionLifetime = TimeSpan.FromSeconds(10);

    private readonly DispatcherTimer _timer;
    private readonly Func<Task>? _action;

    public ToastViewModel(string title, string message, ToastKind kind, string? actionLabel, Func<Task>? action)
    {
        Title = title;
        Message = message;
        ActionLabel = actionLabel;
        _action = action;
        HasAction = !string.IsNullOrWhiteSpace(actionLabel) && action is not null;
        AccentBrush = AccentFor(kind);

        _timer = new DispatcherTimer { Interval = HasAction ? ActionLifetime : PlainLifetime };
        _timer.Tick += (_, _) => Dismiss();
    }

    public string Title { get; }

    public string Message { get; }

    public string? ActionLabel { get; }

    public bool HasAction { get; }

    public Brush AccentBrush { get; }

    public event EventHandler? Dismissed;

    public void Begin() => _timer.Start();

    public void Cancel() => _timer.Stop();

    [RelayCommand]
    private async Task RunAction()
    {
        Cancel();
        Dismissed?.Invoke(this, EventArgs.Empty);

        if (_action is not null)
        {
            // Сервис действия сам покажет тост об ошибке — здесь просто не даём ей всплыть в UI.
            try
            {
                await _action();
            }
            catch
            {
                // проглочено намеренно
            }
        }
    }

    [RelayCommand]
    private void Dismiss()
    {
        Cancel();
        Dismissed?.Invoke(this, EventArgs.Empty);
    }

    private static Brush AccentFor(ToastKind kind)
    {
        var color = kind switch
        {
            ToastKind.Success => Color.FromRgb(0x2F, 0x85, 0x5A),
            ToastKind.Warning => Color.FromRgb(0xB7, 0x79, 0x1F),
            ToastKind.Error => Color.FromRgb(0x8A, 0x1C, 0x2B),
            _ => Color.FromRgb(0x2B, 0x6C, 0xB0),
        };

        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
