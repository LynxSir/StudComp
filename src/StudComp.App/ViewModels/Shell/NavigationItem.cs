using CommunityToolkit.Mvvm.ComponentModel;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Shell;

/// <summary>
/// Пункт боковой навигации Shell'а: подпись, иконка и тип ViewModel, к которой ведёт переход.
/// </summary>
/// <remarks>
/// Раньше это была неизменяемая запись, но у Картотеки появился счётчик «к повторению» прямо на
/// иконке (new_addons.md §2.1), а он меняется по ходу работы — значит пункту нужны уведомления.
/// </remarks>
public sealed partial class NavigationItem : ObservableObject
{
    public NavigationItem(string title, SymbolRegular icon, Type viewModelType)
    {
        Title = title;
        Icon = icon;
        ViewModelType = viewModelType;
    }

    /// <summary>Подпись в сайдбаре — она же тултип.</summary>
    public string Title { get; }

    /// <summary>Иконка (Fluent System Icons из WPF-UI).</summary>
    public SymbolRegular Icon { get; }

    /// <summary>Тип страницы-ViewModel, резолвится из контейнера при навигации.</summary>
    public Type ViewModelType { get; }

    /// <summary>Число на бейдже. Ноль — бейджа нет: пустой счётчик хуже отсутствующего.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBadge))]
    private int _badgeCount;

    public bool HasBadge => BadgeCount > 0;
}
