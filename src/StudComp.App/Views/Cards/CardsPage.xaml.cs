using System.Windows.Controls;

namespace StudComp.Views.Cards;

/// <summary>
/// Раздел «Картотека». Логики нет вовсе: загрузку запускает OnNavigatedTo вьюмодели, а обработчик
/// Loaded здесь сознательно отсутствует — их смешение уже давало двойную загрузку (Phase 12.4).
/// </summary>
public partial class CardsPage : UserControl
{
    public CardsPage() => InitializeComponent();
}
