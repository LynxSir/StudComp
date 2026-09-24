using System.Windows.Controls;

namespace StudComp.Views.Archivist;

/// <summary>
/// Раздел «Архивариус»: три вкладки. Логики нет. Загрузка идёт из
/// <c>ArchivistPageViewModel.OnNavigatedTo</c> (Phase 13.10): страница кешируется и переключается
/// через <c>Visibility</c>, а <c>Loaded</c> при этом повторно не стреляет.
/// </summary>
public partial class ArchivistPage : UserControl
{
    public ArchivistPage() => InitializeComponent();
}
