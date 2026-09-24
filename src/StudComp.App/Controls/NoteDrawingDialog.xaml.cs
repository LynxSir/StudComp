using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;

namespace StudComp.Controls;

/// <summary>
/// Код-behind диалога рисования (new_addons.md §12, Phase 13.7): тулбар управляет DP/методами
/// <see cref="NoteDrawingCanvas"/> прямыми обработчиками — тот же приём, что вставка токенов в
/// <c>RuleEditor.xaml.cs</c> Архивариуса.
/// </summary>
public partial class NoteDrawingDialog : UserControl
{
    private NoteDrawingDialogViewModel? ViewModel => DataContext as NoteDrawingDialogViewModel;

    // ui:Button — панель инструментов; кружки-пресеты цвета ниже собраны на обычном System.Windows.Controls.Button.
    private Wpf.Ui.Controls.Button[] ToolButtons =>
        [PenButton, EraserButton, LineButton, RectangleButton, EllipseButton, ArrowButton, TextButton];

    public NoteDrawingDialog()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;
        InputBindings.Add(new KeyBinding(new RelayCommand(DrawingCanvas.Undo), Key.Z, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(new RelayCommand(DrawingCanvas.Redo), Key.Y, ModifierKeys.Control));
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not NoteDrawingDialogViewModel vm)
        {
            return;
        }

        DrawingCanvas.Load(vm.InitialDocument);
        SyncViewModel();
    }

    private void OnToolButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button clicked || clicked.Tag is not string tag
            || !Enum.TryParse<NoteDrawingTool>(tag, out var tool))
        {
            return;
        }

        DrawingCanvas.Tool = tool;
        foreach (var button in ToolButtons)
        {
            button.Appearance = button == clicked ? ControlAppearance.Primary : ControlAppearance.Secondary;
        }
    }

    private void OnThicknessChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Слайдер задаёт Value="3"/Minimum="1" прямо в XAML — оба присваивания синхронно вызывают
        // ValueChanged ещё во время InitializeComponent(), раньше, чем ниже по разметке будет создан
        // DrawingCanvas. Без guard'а это ронёт весь конструктор диалога NullReferenceException'ом.
        if (DrawingCanvas is null)
        {
            return;
        }

        DrawingCanvas.StrokeThicknessValue = e.NewValue;
    }

    private void OnColorPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string hex })
        {
            DrawingCanvas.StrokeColorHex = hex;
            CustomColorBox.Text = hex;
        }
    }

    private void OnCustomColorChanged(object sender, RoutedEventArgs e)
    {
        var text = CustomColorBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            if (System.Windows.Media.ColorConverter.ConvertFromString(text) is not null)
            {
                DrawingCanvas.StrokeColorHex = text;
            }
        }
        catch (FormatException)
        {
            // Некорректный ввод — цвет просто не меняется, поле остаётся как есть.
        }
    }

    private void OnUndoClick(object sender, RoutedEventArgs e) => DrawingCanvas.Undo();

    private void OnRedoClick(object sender, RoutedEventArgs e) => DrawingCanvas.Redo();

    private void OnClearClick(object sender, RoutedEventArgs e) => DrawingCanvas.Clear();

    private void OnCanvasChanged(object? sender, EventArgs e) => SyncViewModel();

    private void SyncViewModel()
    {
        if (ViewModel is not { } vm)
        {
            return;
        }

        vm.CanSave = DrawingCanvas.HasElements;
        vm.CanUndo = DrawingCanvas.CanUndo;
        vm.CanRedo = DrawingCanvas.CanRedo;
        UndoButton.IsEnabled = DrawingCanvas.CanUndo;
        RedoButton.IsEnabled = DrawingCanvas.CanRedo;

        vm.Document = DrawingCanvas.ExportDocument();
        vm.PngBytes = DrawingCanvas.HasElements ? DrawingCanvas.RenderToPng() : null;
    }
}
