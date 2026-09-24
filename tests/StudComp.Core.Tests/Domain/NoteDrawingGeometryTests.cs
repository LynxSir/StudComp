using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты геометрии наконечника стрелки рисунка заметки (new_addons.md §12, Phase 13.7).
/// </summary>
public sealed class NoteDrawingGeometryTests
{
    private const double Tolerance = 1e-9;

    [Fact]
    public void Both_wings_are_equidistant_from_the_tip()
    {
        var start = new NoteDrawingPoint(0, 0);
        var end = new NoteDrawingPoint(100, 0);

        var (wing1, wing2) = NoteDrawingGeometry.ArrowHead(start, end, 10);

        Assert.Equal(10, Distance(end, wing1), Tolerance);
        Assert.Equal(10, Distance(end, wing2), Tolerance);
    }

    [Fact]
    public void Wings_are_symmetric_around_the_shaft_for_a_horizontal_arrow()
    {
        var start = new NoteDrawingPoint(0, 0);
        var end = new NoteDrawingPoint(100, 0);

        var (wing1, wing2) = NoteDrawingGeometry.ArrowHead(start, end, 10);

        Assert.Equal(wing1.Y, -wing2.Y, Tolerance);
        Assert.Equal(wing1.X, wing2.X, Tolerance);
    }

    [Fact]
    public void Wings_are_symmetric_for_a_vertical_arrow()
    {
        var start = new NoteDrawingPoint(0, 0);
        var end = new NoteDrawingPoint(0, 100);

        var (wing1, wing2) = NoteDrawingGeometry.ArrowHead(start, end, 10);

        Assert.Equal(wing1.X, -wing2.X, Tolerance);
        Assert.Equal(wing1.Y, wing2.Y, Tolerance);
    }

    [Fact]
    public void A_diagonal_arrow_still_places_both_wings_at_the_requested_distance()
    {
        var start = new NoteDrawingPoint(0, 0);
        var end = new NoteDrawingPoint(50, 50);

        var (wing1, wing2) = NoteDrawingGeometry.ArrowHead(start, end, 8);

        Assert.Equal(8, Distance(end, wing1), Tolerance);
        Assert.Equal(8, Distance(end, wing2), Tolerance);
    }

    [Fact]
    public void A_degenerate_zero_length_arrow_does_not_throw_and_collapses_to_the_end_point()
    {
        var point = new NoteDrawingPoint(5, 5);

        var (wing1, wing2) = NoteDrawingGeometry.ArrowHead(point, point, 10);

        Assert.Equal(point, wing1);
        Assert.Equal(point, wing2);
    }

    private static double Distance(NoteDrawingPoint a, NoteDrawingPoint b) =>
        Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
}
