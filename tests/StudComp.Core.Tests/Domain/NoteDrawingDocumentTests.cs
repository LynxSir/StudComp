using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты сериализации рисунка заметки в JSON-сайдкар (new_addons.md §12, Phase 13.7) — файл
/// на диске может оказаться чужим или битым, разбор никогда не должен бросать.
/// </summary>
public sealed class NoteDrawingDocumentTests
{
    [Fact]
    public void Round_trip_preserves_every_element_kind()
    {
        var document = new NoteDrawingDocument
        {
            CanvasWidth = 800,
            CanvasHeight = 500,
            Elements =
            [
                new NoteDrawingElement(
                    NoteDrawingElementKind.Stroke,
                    [new NoteDrawingPoint(1, 2), new NoteDrawingPoint(3, 4)],
                    "#101010",
                    2),
                new NoteDrawingElement(
                    NoteDrawingElementKind.Line,
                    [new NoteDrawingPoint(0, 0), new NoteDrawingPoint(10, 10)],
                    "#FF0000",
                    3),
                new NoteDrawingElement(
                    NoteDrawingElementKind.Rectangle,
                    [new NoteDrawingPoint(5, 5), new NoteDrawingPoint(50, 40)],
                    "#00FF00",
                    1.5),
                new NoteDrawingElement(
                    NoteDrawingElementKind.Ellipse,
                    [new NoteDrawingPoint(5, 5), new NoteDrawingPoint(50, 40)],
                    "#0000FF",
                    1),
                new NoteDrawingElement(
                    NoteDrawingElementKind.Arrow,
                    [new NoteDrawingPoint(0, 0), new NoteDrawingPoint(20, 20)],
                    "#123456",
                    2),
                new NoteDrawingElement(
                    NoteDrawingElementKind.Text,
                    [new NoteDrawingPoint(12, 34)],
                    "#000000",
                    0,
                    Text: "Подпись",
                    FontSize: 18),
            ],
        };

        var json = document.ToJson();
        var parsed = NoteDrawingDocument.TryParse(json, out var restored);

        Assert.True(parsed);
        Assert.NotNull(restored);
        Assert.Equal(document.CanvasWidth, restored!.CanvasWidth);
        Assert.Equal(document.CanvasHeight, restored.CanvasHeight);
        Assert.Equal(document.Elements.Count, restored.Elements.Count);
        for (var i = 0; i < document.Elements.Count; i++)
        {
            var expected = document.Elements[i];
            var actual = restored.Elements[i];
            Assert.Equal(expected.Kind, actual.Kind);
            Assert.Equal(expected.ColorHex, actual.ColorHex);
            Assert.Equal(expected.Thickness, actual.Thickness);
            Assert.Equal(expected.Text, actual.Text);
            Assert.Equal(expected.FontSize, actual.FontSize);
            Assert.True(expected.Points.SequenceEqual(actual.Points));
        }
    }

    [Fact]
    public void Empty_document_round_trips_to_an_empty_element_list()
    {
        var document = new NoteDrawingDocument();

        var parsed = NoteDrawingDocument.TryParse(document.ToJson(), out var restored);

        Assert.True(parsed);
        Assert.Empty(restored!.Elements);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"schema\":\"some.other.schema\",\"version\":1,\"elements\":[]}")]
    [InlineData("{\"schema\":\"rubrica.note-drawing\"")]
    public void Foreign_or_malformed_input_never_throws_and_reports_failure(string? input)
    {
        var parsed = NoteDrawingDocument.TryParse(input, out var document);

        Assert.False(parsed);
        Assert.Null(document);
    }

    [Fact]
    public void A_document_with_a_non_positive_canvas_size_falls_back_to_defaults()
    {
        const string json =
            "{\"schema\":\"rubrica.note-drawing\",\"version\":1,\"canvasWidth\":0,\"canvasHeight\":-5,\"elements\":[]}";

        var parsed = NoteDrawingDocument.TryParse(json, out var document);

        Assert.True(parsed);
        Assert.True(document!.CanvasWidth > 0);
        Assert.True(document.CanvasHeight > 0);
    }
}
