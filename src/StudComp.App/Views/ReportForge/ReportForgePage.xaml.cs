using System.Windows.Controls;

namespace StudComp.Views.ReportForge;

/// <summary>
/// Раздел «Отчёты»: три вкладки. Логики нет — загрузка данных идёт через
/// <c>ReportForgePageViewModel.OnNavigatedTo</c> (навигация с параметром, new_addons.md §6).
/// </summary>
public partial class ReportForgePage : UserControl
{
    public ReportForgePage() => InitializeComponent();
}
