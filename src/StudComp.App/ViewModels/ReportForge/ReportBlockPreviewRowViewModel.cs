using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.ViewModels.ReportForge;

/// <summary>
/// Строка предпросмотра разобранной модели: что именно генератор увидел в markdown. Показывает
/// структуру, а не вёрстку — вёрстку показывает сам Word.
/// </summary>
public sealed class ReportBlockPreviewRowViewModel
{
    public ReportBlockPreviewRowViewModel(IReportBlock block)
    {
        (Kind, Description, Icon, IndentLevel) = Describe(block);
    }

    /// <summary>Вид блока по-русски: «Заголовок 1», «Таблица», «Листинг».</summary>
    public string Kind { get; }

    /// <summary>Короткая выжимка содержимого.</summary>
    public string Description { get; }

    /// <summary>Имя символа WPF-UI для иконки строки.</summary>
    public string Icon { get; }

    /// <summary>Отступ строки: заголовки нижних уровней сдвигаются вправо, чтобы читалась структура.</summary>
    public double IndentLevel { get; }

    private static (string Kind, string Description, string Icon, double Indent) Describe(IReportBlock block) =>
        block switch
        {
            HeadingBlock heading => (
                $"Заголовок {heading.Level}",
                heading.Text,
                "TextHeader124",
                Math.Clamp(heading.Level - 1, 0, 3) * 16d),

            ParagraphBlock paragraph => (
                "Абзац",
                Shorten(string.Concat(paragraph.Runs.Select(run => run.Text))),
                "TextParagraph24",
                0d),

            ListBlock list => (
                list.Ordered ? "Нумерованный список" : "Список",
                $"пунктов: {list.Items.Count}",
                list.Ordered ? "TextNumberListLtr24" : "TextBulletListLtr24",
                0d),

            TableBlock table => (
                "Таблица",
                string.IsNullOrWhiteSpace(table.Caption)
                    ? $"{table.Rows.Count} × {table.Headers.Count}"
                    : $"{table.Caption} ({table.Rows.Count} × {table.Headers.Count})",
                "Table24",
                0d),

            CodeBlock code => (
                "Листинг",
                code.Language is { Length: > 0 } language ? language : "без указания языка",
                "Code24",
                0d),

            ImageBlock image => (
                "Изображение",
                string.IsNullOrWhiteSpace(image.Caption) ? Shorten(image.PathOrBase64) : image.Caption!,
                "Image24",
                0d),

            _ => ("Блок", string.Empty, "Document24", 0d),
        };

    private static string Shorten(string text)
    {
        var single = text.ReplaceLineEndings(" ").Trim();
        return single.Length <= 90 ? single : single[..90] + "…";
    }
}
