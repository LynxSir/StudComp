using System.Windows.Controls;

namespace StudComp.Controls;

/// <summary>
/// Третья колонка библиотеки: карточка целиком с правкой на месте (new_addons.md §8.2).
/// Логики здесь нет — вся она в <see cref="CardPreviewViewModel"/>.
/// </summary>
public partial class CardPreview : UserControl
{
    public CardPreview()
    {
        InitializeComponent();
    }
}
