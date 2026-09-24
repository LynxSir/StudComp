using System.Windows.Controls;

namespace StudComp.Views.Organizer;

/// <summary>
/// Раздел «Органайзер»: четыре вкладки. Логики нет. Загрузка идёт из
/// <c>OrganizerPageViewModel.OnNavigatedTo</c> (Phase 13.10): страница кешируется и переключается
/// через <c>Visibility</c>, а <c>Loaded</c> при этом повторно не стреляет.
/// </summary>
public partial class OrganizerPage : UserControl
{
    public OrganizerPage() => InitializeComponent();
}
