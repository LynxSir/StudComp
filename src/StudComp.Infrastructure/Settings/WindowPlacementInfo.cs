namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Запомненные положение и состояние главного окна (new_addons.md §1.2). При неоднозначности окно
/// открывается развёрнутым. Сериализуется в <c>usersettings.json</c> как поддерево
/// <c>Rubrica:General:WindowPlacement</c>.
/// </summary>
public sealed class WindowPlacementInfo
{
    /// <summary>Состояние окна: <c>"Maximized"</c> или <c>"Normal"</c>.</summary>
    public string State { get; set; } = "Maximized";

    /// <summary>Границы окна в состоянии <c>Normal</c> (device-independent пиксели).</summary>
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }
}
