using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using StudComp.ViewModels.Shell;

namespace StudComp.Views.Shell;

/// <summary>
/// Дашборд — стартовый экран (new_addons.md §1.5, Phase 13.5). Загрузка данных идёт из
/// <see cref="DashboardPageViewModel.OnNavigatedTo"/>, не отсюда — здесь только два чисто
/// View-специфичных момента: переключение широкой/узкой раскладки по ширине окна и лёгкий
/// периодический авто-рефреш, пока страница открыта.
/// </summary>
public partial class DashboardPage : UserControl
{
    /// <summary>
    /// Порог переключения на бенто-сетку (new_addons.md §2/§18): ниже — узкая раскладка со скроллом
    /// страницы (разрешено документом явно), на развёрнутом 1920×1080 и выше — широкая сетка без него.
    /// </summary>
    private const double WideLayoutThreshold = 1280;

    private readonly DispatcherTimer _refreshTimer;

    public DashboardPage()
    {
        InitializeComponent();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _refreshTimer.Tick += (_, _) => (DataContext as DashboardPageViewModel)?.LoadCommand.Execute(null);

        // Страница кешируется и переключается через Visibility (Phase 13.10): Loaded/Unloaded при
        // этом не стреляют, а IsVisibleChanged учитывает и первый показ, и скрытие предком.
        IsVisibleChanged += OnIsVisibleChanged;
        SizeChanged += OnSizeChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            ApplyLayout(ActualWidth);
            _refreshTimer.Start();
        }
        else
        {
            _refreshTimer.Stop();
        }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyLayout(e.NewSize.Width);

    private void ApplyLayout(double width)
    {
        var wide = width >= WideLayoutThreshold;
        WideContent.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        NarrowContent.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
    }
}
