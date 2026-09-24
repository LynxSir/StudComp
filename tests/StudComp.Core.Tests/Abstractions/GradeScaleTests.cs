using StudComp.Core.Abstractions.Organizer;

namespace StudComp.Core.Tests.Abstractions;

/// <summary>
/// Стандартные границы известных шкал (ARCHITECTURE §9.4). Значения зашиты здесь намеренно —
/// молчаливое изменение «сколько такое 3 по 5-балльной» должно ломать тест.
/// </summary>
public sealed class GradeScaleTests
{
    [Theory]
    [InlineData(GradeScaleKind.FivePoint, 2, 5, 3)]
    [InlineData(GradeScaleKind.HundredPoint, 0, 100, 60)]
    public void For_returns_the_documented_bounds(GradeScaleKind kind, int min, int max, int pass)
    {
        var scale = GradeScale.For(kind);

        Assert.Equal(min, scale.Min);
        Assert.Equal(max, scale.Max);
        Assert.Equal(pass, scale.PassThreshold);
        Assert.Equal(kind, scale.Kind);
    }

    [Fact]
    public void For_custom_returns_a_custom_kind_placeholder()
    {
        var scale = GradeScale.For(GradeScaleKind.Custom);

        Assert.Equal(GradeScaleKind.Custom, scale.Kind);
    }
}
