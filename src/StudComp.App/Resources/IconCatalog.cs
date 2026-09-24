using Wpf.Ui.Controls;

namespace StudComp.Resources;

/// <summary>
/// Справочник иконок для типовых действий — держит согласованность там, где символ выбирается
/// программно (C#/code-behind), а не прямым тегом в XAML. Предотвращает повтор инцидента с
/// «перевёрнутой Я» у экспорта (Phase 13.1: <see cref="SymbolRegular.ArrowExport24"/> заменён на
/// <see cref="SymbolRegular.DocumentArrowRight24"/> в трёх местах) — новый код выбирает символ здесь,
/// а не заново «на глаз» (Phase 13.9).
/// </summary>
public static class IconCatalog
{
    public const SymbolRegular Add = SymbolRegular.Add24;
    public const SymbolRegular Edit = SymbolRegular.Edit24;
    public const SymbolRegular Delete = SymbolRegular.Delete24;
    public const SymbolRegular Export = SymbolRegular.DocumentArrowRight24;
    public const SymbolRegular Import = SymbolRegular.ArrowImport24;
    public const SymbolRegular Help = SymbolRegular.QuestionCircle24;
    public const SymbolRegular Pin = SymbolRegular.Pin24;
    public const SymbolRegular Undo = SymbolRegular.ArrowUndo24;
    public const SymbolRegular Refresh = SymbolRegular.ArrowSync24;
    public const SymbolRegular DragHandle = SymbolRegular.ReOrderDotsVertical24;
    public const SymbolRegular MoreActions = SymbolRegular.MoreHorizontal24;
    public const SymbolRegular Dismiss = SymbolRegular.Dismiss24;
    public const SymbolRegular Confirm = SymbolRegular.Checkmark24;
    public const SymbolRegular Attach = SymbolRegular.AttachArrowRight24;
}
