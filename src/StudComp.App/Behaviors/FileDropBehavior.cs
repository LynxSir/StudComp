using System.Windows;
using System.Windows.Input;
using StudComp.Core.Abstractions.Workspace;

namespace StudComp.Behaviors;

/// <summary>
/// Что именно перетащили и с какими модификаторами (new_addons.md §1.11).
/// </summary>
/// <param name="Paths">Пути файлов и папок из проводника.</param>
/// <param name="ForcedMode">
/// <c>Ctrl</c> — форс-копирование, <c>Shift</c> — форс-перемещение; <see langword="null"/> —
/// спросить пользователя.
/// </param>
public sealed record FileDropRequest(IReadOnlyList<string> Paths, ImportMode? ForcedMode);

/// <summary>
/// Приём файлов, перетащенных из проводника: подсветка цели во время перетаскивания и команда с
/// путями на <c>Drop</c> (new_addons.md §1.11).
/// </summary>
/// <remarks>
/// Отдельное поведение, а не расширение <c>DragDropSortBehavior</c>: тот про перетаскивание внутри
/// приложения и типизирован строками Архивариуса, здесь же источник — проводник Windows.
/// </remarks>
public static class FileDropBehavior
{
    /// <summary>Команда, получающая <see cref="FileDropRequest"/> при сбросе файлов.</summary>
    public static readonly DependencyProperty DropCommandProperty =
        DependencyProperty.RegisterAttached(
            "DropCommand",
            typeof(ICommand),
            typeof(FileDropBehavior),
            new PropertyMetadata(null, OnDropCommandChanged));

    /// <summary>Свойство VM, в которое пишется признак «над целью сейчас тащат файлы».</summary>
    public static readonly DependencyProperty IsDragOverProperty =
        DependencyProperty.RegisterAttached(
            "IsDragOver",
            typeof(bool),
            typeof(FileDropBehavior),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static ICommand? GetDropCommand(DependencyObject element) =>
        (ICommand?)element.GetValue(DropCommandProperty);

    public static void SetDropCommand(DependencyObject element, ICommand? value) =>
        element.SetValue(DropCommandProperty, value);

    public static bool GetIsDragOver(DependencyObject element) =>
        (bool)element.GetValue(IsDragOverProperty);

    public static void SetIsDragOver(DependencyObject element, bool value) =>
        element.SetValue(IsDragOverProperty, value);

    private static void OnDropCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        element.DragEnter -= OnDragOver;
        element.DragOver -= OnDragOver;
        element.DragLeave -= OnDragLeave;
        element.Drop -= OnDrop;
        element.Unloaded -= OnUnloaded;

        if (e.NewValue is null)
        {
            element.AllowDrop = false;
            return;
        }

        element.AllowDrop = true;
        element.DragEnter += OnDragOver;
        element.DragOver += OnDragOver;
        element.DragLeave += OnDragLeave;
        element.Drop += OnDrop;
        element.Unloaded += OnUnloaded;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var hasFiles = e.Data.GetDataPresent(DataFormats.FileDrop);
        SetIsDragOver(element, hasFiles);

        e.Effects = hasFiles ? EffectFor(e.KeyStates) : DragDropEffects.None;
        e.Handled = true;
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            SetIsDragOver(element, false);
        }
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        SetIsDragOver(element, false);
        e.Handled = true;

        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var command = GetDropCommand(element);
        var request = new FileDropRequest(paths, ForcedModeFor(e.KeyStates));

        if (command?.CanExecute(request) == true)
        {
            command.Execute(request);
        }
    }

    private static void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
        {
            SetIsDragOver(element, false);
        }
    }

    /// <summary>Модификаторы как в проводнике Windows: Ctrl — копировать, Shift — переместить.</summary>
    private static ImportMode? ForcedModeFor(DragDropKeyStates keys) =>
        keys.HasFlag(DragDropKeyStates.ControlKey) ? ImportMode.Copy
        : keys.HasFlag(DragDropKeyStates.ShiftKey) ? ImportMode.Move
        : null;

    private static DragDropEffects EffectFor(DragDropKeyStates keys) =>
        ForcedModeFor(keys) == ImportMode.Copy ? DragDropEffects.Copy : DragDropEffects.Move;
}
