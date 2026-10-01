using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Resources;

namespace StudComp.ViewModels;

/// <summary>
/// Помощник по разделу: слева темы («О разделе» плюс по теме на подвкладку), справа – выбранная.
/// Открывается кнопкой «?» справа от полосы вкладок и сразу показывает тему текущей подвкладки.
/// </summary>
/// <remarks>
/// Содержимое статично и никуда не сохраняется, поэтому вьюмодель без зависимостей – её создают на
/// месте и отдают в <c>IDialogService.ShowInfoAsync</c> (тот же приём, что у <see cref="MarkdownHelpViewModel"/>).
/// </remarks>
public sealed partial class SectionHelpViewModel : ObservableObject
{
    /// <param name="section">Раздел, чей набор тем показывать.</param>
    /// <param name="selectedTabIndex">
    /// Индекс текущей подвкладки. Темы в каталоге лежат в физическом порядке вкладок, поэтому
    /// нулевая тема – «О разделе», а подвкладка с индексом N – это тема N+1.
    /// </param>
    public SectionHelpViewModel(HelpSection section, int selectedTabIndex)
    {
        Topics = HelpCatalog.Topics(section);

        var wanted = selectedTabIndex + 1;
        _current = Topics.Count == 0
            ? null
            : Topics[wanted >= 0 && wanted < Topics.Count ? wanted : 0];
    }

    /// <summary>Темы раздела в порядке рельса.</summary>
    public IReadOnlyList<HelpTopic> Topics { get; }

    /// <summary>Открытая тема.</summary>
    [ObservableProperty]
    private HelpTopic? _current;
}
