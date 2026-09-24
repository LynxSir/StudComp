using System.Windows.Controls;

namespace StudComp.Controls;

/// <summary>Слой тостов внутри окна. Логики нет — всё в <c>ToastHostViewModel</c>.</summary>
public partial class ToastHost : UserControl
{
    public ToastHost() => InitializeComponent();
}
