using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.Controls;

/// <summary>
/// Рендер Markdown в <see cref="FlowDocument"/> для предпросмотра заметки (new_addons.md §1.9).
/// </summary>
/// <remarks>
/// Разбирает Markdown не сам, а переиспользует <see cref="IMarkdownDocumentModelBuilder"/> из модуля
/// отчётов: он уже покрыт табличными тестами, и заметка в предпросмотре выглядит ровно так, как её
/// потом соберёт генератор отчётов. Побочная выгода — <c>Markdig</c> не нужно тащить в <c>App</c>
/// отдельной ссылкой.
/// </remarks>
public static class MarkdownFlowRenderer
{
    private static readonly FontFamily Monospace = new("Consolas, Courier New, monospace");

    /// <summary>Кегль основного текста предпросмотра — от него считаются индексы формул.</summary>
    private const double BaseFontSize = 14;

    /// <summary>Максимальная ширина картинки в предпросмотре — крупный скан/фото не должен растягивать заметку.</summary>
    private const int MaxImageDecodeWidth = 700;

    /// <summary>
    /// Собирает документ предпросмотра из размеченного текста.
    /// </summary>
    /// <param name="imageBaseDirectory">
    /// Папка, относительно которой резолвится относительный путь картинки (Phase 13.7 — рисунки заметки
    /// хранятся относительно <c>IStudyWorkspace.StudyRootPath</c>). <see langword="null"/> — относительные
    /// пути не резолвятся вовсе (как раньше), абсолютные грузятся всегда.
    /// </param>
    public static FlowDocument Render(IMarkdownDocumentModelBuilder builder, string? markdown, string? imageBaseDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var document = new FlowDocument
        {
            PagePadding = new Thickness(0),
            FontSize = BaseFontSize,
        };

        if (string.IsNullOrWhiteSpace(markdown))
        {
            return document;
        }

        ReportDocumentModel model;
        try
        {
            model = builder.Build(markdown);
        }
        catch (Exception)
        {
            // Предпросмотр не то, ради чего стоит ронять редактор: показываем текст как есть.
            document.Blocks.Add(new Paragraph(new Run(markdown)));
            return document;
        }

        foreach (var block in BuildBlocks(model, imageBaseDirectory))
        {
            document.Blocks.Add(block);
        }

        return document;
    }

    private static IEnumerable<Block> BuildBlocks(ReportDocumentModel model, string? imageBaseDirectory)
    {
        foreach (var block in model.Blocks)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    yield return BuildHeading(heading);
                    break;

                case ParagraphBlock paragraph:
                    yield return BuildParagraph(paragraph);
                    break;

                case ListBlock list:
                    yield return BuildList(list, imageBaseDirectory);
                    break;

                case CodeBlock code:
                    yield return BuildCode(code);
                    break;

                case TableBlock table:
                    yield return BuildTable(table);
                    break;

                case ImageBlock image:
                    yield return BuildImage(image, imageBaseDirectory);
                    break;
            }
        }
    }

    /// <summary>
    /// Рисунок/картинка — реальный <see cref="Image"/>, а не заглушка (закрывает пробел Phase 12.3 и
    /// открывает рисование в заметках, Phase 13.7). Путь резолвится, файл читается и декодируется — всё
    /// в приглушённом try/catch: предпросмотр не то, ради чего стоит ронять редактор, а битая/пропавшая
    /// картинка не редкость (переименовали файл, перенесли учебную папку).
    /// </summary>
    private static Block BuildImage(ImageBlock image, string? imageBaseDirectory)
    {
        var path = ResolveImagePath(image.PathOrBase64, imageBaseDirectory);
        if (path is null || !File.Exists(path))
        {
            return Muted($"[изображение: {image.Caption ?? image.PathOrBase64}]");
        }

        try
        {
            // BitmapImage обязателен к инициализации через BeginInit/EndInit — без этого объект
            // остаётся не полностью инициализированным (несмотря на все свойства, заданные через
            // object initializer), Freeze() при этом не бросает исключение, и в предпросмотре тихо
            // ничего не появляется вместо картинки (Phase 13.7, найдено на реальном запуске).
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;

            // Тот же файл на диске может обновиться при повторном редактировании рисунка (Phase
            // 13.7) — без сброса кэша по URI старая картинка осталась бы в предпросмотре.
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.DecodePixelWidth = MaxImageDecodeWidth;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            var imageElement = new Image
            {
                Source = bitmap,
                Stretch = Stretch.Uniform,
                MaxWidth = image.Width ?? MaxImageDecodeWidth,
                Width = image.Width ?? double.NaN,
                HorizontalAlignment = HorizontalAlignment.Left,
                Tag = image,
                ToolTip = "Нажмите, чтобы изменить размер или отредактировать рисунок",
            };

            var container = new BlockUIContainer(imageElement) { Margin = new Thickness(0, 0, 0, 8) };
            return container;
        }
        catch (Exception)
        {
            return Muted($"[изображение: {image.Caption ?? image.PathOrBase64}]");
        }
    }

    private static string? ResolveImagePath(string pathOrBase64, string? imageBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(pathOrBase64))
        {
            return null;
        }

        if (Path.IsPathRooted(pathOrBase64))
        {
            return pathOrBase64;
        }

        return string.IsNullOrWhiteSpace(imageBaseDirectory)
            ? null
            : Path.Combine(imageBaseDirectory, pathOrBase64);
    }

    private static Paragraph BuildHeading(HeadingBlock heading)
    {
        var size = heading.Level switch
        {
            1 => 22d,
            2 => 18d,
            3 => 16d,
            _ => 14d,
        };

        return new Paragraph(new Run(heading.Text))
        {
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, heading.Level == 1 ? 0 : 12, 0, 6),
        };
    }

    private static Paragraph BuildParagraph(ParagraphBlock block)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var run in block.Runs)
        {
            paragraph.Inlines.Add(BuildInline(run));
        }

        // Выключная формула ($$…$$) приходит абзацем из одного куска — её принято центрировать.
        if (block.Runs is [{ Style: var only, Break: InlineBreak.None }] && only.HasFlag(InlineStyle.Math))
        {
            paragraph.TextAlignment = TextAlignment.Center;
            paragraph.Margin = new Thickness(0, 6, 0, 10);
        }

        return paragraph;
    }

    private static Inline BuildInline(InlineRun run)
    {
        // Перенос строки: в предпросмотре переносятся и мягкий, и жёсткий. Иначе одиночный Enter
        // пропадал бы, хотя пользователь его видел, пока печатал (new_addons.md §11.7).
        if (run.Break != InlineBreak.None)
        {
            return new LineBreak();
        }

        Inline inline = run.Style.HasFlag(InlineStyle.Math)
            ? MathInlineRenderer.Build(run.Text, BaseFontSize)
            : new Run(run.Text);

        if (run.Style.HasFlag(InlineStyle.Code))
        {
            inline = new Span(inline) { FontFamily = Monospace };
        }

        if (run.Style.HasFlag(InlineStyle.Bold))
        {
            inline = new Bold(inline);
        }

        if (run.Style.HasFlag(InlineStyle.Italic))
        {
            inline = new Italic(inline);
        }

        if (run.Style.HasFlag(InlineStyle.Underline))
        {
            inline = new Underline(inline);
        }

        if (run.Style.HasFlag(InlineStyle.Strikethrough))
        {
            inline.TextDecorations = TextDecorations.Strikethrough;
        }

        if (!string.IsNullOrWhiteSpace(run.Hyperlink))
        {
            // Ссылка в предпросмотре только подсвечивается: открывать её мы здесь не беремся.
            inline.Foreground = Brushes.SteelBlue;
            inline.TextDecorations = TextDecorations.Underline;
        }

        return inline;
    }

    private static Block BuildList(ListBlock block, string? imageBaseDirectory)
    {
        var list = new List
        {
            MarkerStyle = block.Ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
            Margin = new Thickness(0, 0, 0, 8),
            Padding = new Thickness(20, 0, 0, 0),
        };

        foreach (var item in block.Items)
        {
            var listItem = new ListItem();
            foreach (var child in BuildBlocks(item, imageBaseDirectory))
            {
                listItem.Blocks.Add(child);
            }

            if (listItem.Blocks.Count == 0)
            {
                listItem.Blocks.Add(new Paragraph());
            }

            list.ListItems.Add(listItem);
        }

        return list;
    }

    private static Block BuildCode(CodeBlock block) =>
        new Paragraph(new Run(block.Code))
        {
            FontFamily = Monospace,
            FontSize = 12.5,
            Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF5, 0xF5)),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(0, 0, 0, 8),
        };

    private static Block BuildTable(TableBlock block)
    {
        var columns = Math.Max(
            block.Headers.Count,
            block.Rows.Count == 0 ? 0 : block.Rows.Max(r => r.Count));

        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 0, 0, 8) };
        for (var i = 0; i < Math.Max(1, columns); i++)
        {
            table.Columns.Add(new TableColumn());
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        if (block.Headers.Count > 0)
        {
            group.Rows.Add(BuildRow(block.Headers, bold: true, columns));
        }

        foreach (var row in block.Rows)
        {
            group.Rows.Add(BuildRow(row, bold: false, columns));
        }

        if (!string.IsNullOrWhiteSpace(block.Caption))
        {
            // Подпись остаётся отдельным абзацем — таблица во FlowDocument её не носит.
            group.Rows.Add(BuildRow([block.Caption], bold: false, columns));
        }

        return table;
    }

    private static TableRow BuildRow(IReadOnlyList<string> cells, bool bold, int columns)
    {
        var row = new TableRow();
        for (var i = 0; i < Math.Max(1, columns); i++)
        {
            var text = i < cells.Count ? cells[i] : string.Empty;
            row.Cells.Add(new TableCell(new Paragraph(new Run(text)))
            {
                Padding = new Thickness(6, 4, 6, 4),
                BorderThickness = new Thickness(0.5),
                BorderBrush = Brushes.Gainsboro,
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            });
        }

        return row;
    }

    private static Paragraph Muted(string text) =>
        new(new Run(text))
        {
            Foreground = Brushes.Gray,
            FontStyle = FontStyles.Italic,
            Margin = new Thickness(0, 0, 0, 8),
        };
}
