using System.Windows;
using System.Windows.Controls;

namespace StudComp.Views.ReportForge;

/// <summary>Вкладка «Новый отчёт». Вся логика — в <c>NewReportViewModel</c>.</summary>
public partial class NewReportPage : UserControl
{
    public NewReportPage() => InitializeComponent();

    /// <summary>
    /// <see cref="ContextMenu"/> в WPF открывается по правому клику, а кнопке «Собрать текст» нужен
    /// обычный левый (приём из <c>CardLibraryPage</c>, Phase 12.9) — чисто визуальная деталь, которой
    /// не место во вьюмодели.
    /// </summary>
    private void OnComposeMenuButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } element)
        {
            menu.PlacementTarget = element;
            menu.IsOpen = true;
        }
    }
}
