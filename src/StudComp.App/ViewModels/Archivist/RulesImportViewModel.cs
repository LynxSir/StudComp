using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Modules.Archivist.Services;

namespace StudComp.ViewModels.Archivist;

/// <summary>Маленький диалог перед импортом: заменить весь набор правил или добавить к текущему.</summary>
public sealed partial class RulesImportViewModel : ObservableObject
{
    public RulesImportViewModel() => _selectedMode = Modes[0];

    public IReadOnlyList<NamedChoice<RulesImportMode>> Modes { get; } =
    [
        new(RulesImportMode.Append, "Добавить к текущим правилам"),
        new(RulesImportMode.Replace, "Заменить все правила"),
    ];

    [ObservableProperty]
    private NamedChoice<RulesImportMode> _selectedMode;

    public RulesImportMode Mode => SelectedMode.Value;

    /// <summary>Кнопка диалога всегда активна — выбор режима сделан по умолчанию.</summary>
    public bool CanSave => true;
}
