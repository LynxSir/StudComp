using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Modules.ReportForge.Services.Docx;

namespace StudComp.Modules.ReportForge.Services;

/// <summary>
/// <see cref="ReportDocumentModel"/> + <see cref="GostStyleProfile"/> → <c>.docx</c> через OpenXML SDK
/// (ARCHITECTURE §10.2, ADR §16.3). Результат обязан проходить <c>OpenXmlValidator</c> (ARCHITECTURE §12).
/// </summary>
internal sealed class GostDocxRenderer(ILogger<GostDocxRenderer> logger) : IGostDocxRenderer
{
    public Task RenderAsync(ReportDocumentModel model, GostStyleProfile profile, Stream output, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(output);

        // Сборка пакета целиком синхронная и не самая дешёвая — уводим её с вызывающего потока,
        // чтобы UI не замирал на большом отчёте (ARCHITECTURE §11.4).
        return Task.Run(() => Render(model, profile, output, ct), ct);
    }

    private void Render(ReportDocumentModel model, GostStyleProfile profile, Stream output, CancellationToken ct)
    {
        using var document = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document);

        var main = document.AddMainDocumentPart();
        main.Document = new Document();
        var body = main.Document.AppendChild(new Body());

        main.AddNewPart<StyleDefinitionsPart>().Styles = StyleDefinitionsFactory.Create(profile);
        main.AddNewPart<NumberingDefinitionsPart>().Numbering = NumberingFactory.Create(profile);

        // Просим Word пересчитать поля при открытии — иначе оглавление останется заглушкой до F9.
        main.AddNewPart<DocumentSettingsPart>().Settings = new Settings(new UpdateFieldsOnOpen { Val = true });

        if (model.TitlePage is { } titlePage)
        {
            body.Append(TitlePageWriter.Build(titlePage, profile));
        }

        if (model.GenerateTableOfContents)
        {
            body.Append(TocWriter.Build(profile));
        }

        new BodyWriter(main, profile, logger).Write(model.Blocks, body, ct);

        body.Append(SectionFactory.Create(main, profile, model.TitlePage is not null));
    }

    /// <summary>
    /// Пишет блоки модели в тело документа, ведя сквозные счётчики таблиц, рисунков и графических
    /// объектов: нумерация подписей должна быть непрерывной по всему отчёту (ARCHITECTURE §10.5).
    /// </summary>
    private sealed class BodyWriter(MainDocumentPart main, GostStyleProfile profile, ILogger logger)
    {
        private readonly int _maxHeadingLevel = profile.HeadingRules.Count == 0
            ? 1
            : profile.HeadingRules.Max(rule => rule.Level);

        private readonly int _textWidthTwips = SectionFactory.TextWidthTwips(profile);

        /// <summary>Потолок высоты рисунка: полоса набора минус ~3 строки резерва под подпись и отбивку.</summary>
        private readonly int _imageMaxHeightTwips = Math.Max(
            1440,
            SectionFactory.TextHeightTwips(profile)
                - (int)Math.Round(profile.FontSizePt * 20d * 3d, MidpointRounding.AwayFromZero));

        private int _tableNumber;
        private int _figureNumber;
        private uint _drawingId = 1;

        internal void Write(IReadOnlyList<IReportBlock> blocks, OpenXmlCompositeElement target, CancellationToken ct)
        {
            foreach (var block in blocks)
            {
                ct.ThrowIfCancellationRequested();
                WriteBlock(block, target, ct);
            }
        }

        private void WriteBlock(IReportBlock block, OpenXmlCompositeElement target, CancellationToken ct)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    target.Append(BuildHeading(heading));
                    break;

                case ParagraphBlock paragraph:
                    target.Append(BuildParagraph(paragraph));
                    break;

                case ListBlock list:
                    WriteList(list, target, level: 0, ct);
                    break;

                case TableBlock table:
                    WriteTable(table, target);
                    break;

                case CodeBlock code:
                    WriteCode(code, target);
                    break;

                case ImageBlock image:
                    WriteImage(image, target);
                    break;
            }
        }

        private Paragraph BuildHeading(HeadingBlock heading)
        {
            var level = Math.Clamp(heading.Level, 1, _maxHeadingLevel);

            return new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = StyleDefinitionsFactory.HeadingStyleId(level) }),
                BuildRun(new InlineRun(heading.Text)));
        }

        private Paragraph BuildParagraph(ParagraphBlock block)
        {
            var paragraph = new Paragraph();

            foreach (var run in FlattenSoftBreaks(block.Runs))
            {
                paragraph.Append(BuildRunOrHyperlink(run));
            }

            return paragraph;
        }

        /// <summary>
        /// Мягкий перенос в .docx — обычный пробел, и он вливается обратно в соседний кусок текста.
        /// Модель несёт переносы отдельными кусками ради предпросмотра заметки (new_addons.md §11.7),
        /// но отчёту это дало бы лишние <c>w:r</c> на ровном месте — вывод не изменился ни на байт.
        /// </summary>
        private static List<InlineRun> FlattenSoftBreaks(IReadOnlyList<InlineRun> runs)
        {
            var result = new List<InlineRun>(runs.Count);

            foreach (var run in runs)
            {
                var current = run.Break == InlineBreak.Soft
                    ? run with { Text = " ", Break = InlineBreak.None }
                    : run;

                if (result.Count > 0
                    && result[^1] is { Break: InlineBreak.None } last
                    && current.Break == InlineBreak.None
                    && last.Style == current.Style
                    && last.Hyperlink == current.Hyperlink
                    && !current.Style.HasFlag(InlineStyle.Math))
                {
                    result[^1] = last with { Text = last.Text + current.Text };
                    continue;
                }

                result.Add(current);
            }

            return result;
        }

        private OpenXmlElement BuildRunOrHyperlink(InlineRun run)
        {
            // Мягкий перенос в .docx остаётся пробелом (так велит CommonMark и так вёрстка отчёта
            // не рвётся на импортированном .md с жёстко обрезанными строками), жёсткий — настоящий
            // разрыв строки. В предпросмотре заметки переносятся оба (new_addons.md §11.7).
            if (run.Break != InlineBreak.None)
            {
                return run.Break == InlineBreak.Hard
                    ? new Run(new Break())
                    : new Run(new Text(" ") { Space = SpaceProcessingModeValues.Preserve });
            }

            if (string.IsNullOrEmpty(run.Hyperlink)
                || !Uri.TryCreate(run.Hyperlink, UriKind.Absolute, out var uri))
            {
                return BuildRun(run);
            }

            var relationship = main.AddHyperlinkRelationship(uri, isExternal: true);
            return new Hyperlink(BuildRun(run, StyleDefinitionsFactory.HyperlinkStyleId))
            {
                Id = relationship.Id,
            };
        }

        private static Run BuildRun(InlineRun run, string? characterStyleId = null)
        {
            var properties = new RunProperties();

            if (characterStyleId is not null)
            {
                properties.Append(new RunStyle { Val = characterStyleId });
            }

            // Формула уезжает в .docx исходником моноширинным курсивом: до этой фазы она пропадала
            // из отчёта вовсе. Настоящий OMML — по-прежнему в бэклоге (ARCHITECTURE §18).
            var isMath = run.Style.HasFlag(InlineStyle.Math);

            // Каждый элемент w:rPr по схеме встречается не больше раза, поэтому сочетания флагов
            // (код + формула, формула + курсив) складываются здесь, а не дописываются по одному.
            if (isMath || run.Style.HasFlag(InlineStyle.Code))
            {
                properties.Append(new RunFonts { Ascii = "Consolas", HighAnsi = "Consolas", ComplexScript = "Consolas" });
            }

            if (run.Style.HasFlag(InlineStyle.Bold))
            {
                properties.Append(new Bold());
                properties.Append(new BoldComplexScript());
            }

            if (isMath || run.Style.HasFlag(InlineStyle.Italic))
            {
                properties.Append(new Italic());
                properties.Append(new ItalicComplexScript());
            }

            if (run.Style.HasFlag(InlineStyle.Strikethrough))
            {
                properties.Append(new Strike());
            }

            if (run.Style.HasFlag(InlineStyle.Underline))
            {
                properties.Append(new Underline { Val = UnderlineValues.Single });
            }

            var element = new Run(new Text(run.Text) { Space = SpaceProcessingModeValues.Preserve });

            if (properties.HasChildren)
            {
                element.InsertAt(properties, 0);
            }

            return element;
        }

        private void WriteList(ListBlock list, OpenXmlCompositeElement target, int level, CancellationToken ct)
        {
            var clampedLevel = Math.Clamp(level, 0, NumberingFactory.LevelCount - 1);

            foreach (var item in list.Items)
            {
                ct.ThrowIfCancellationRequested();

                var numbered = false;

                foreach (var block in item.Blocks)
                {
                    switch (block)
                    {
                        case ParagraphBlock paragraph:
                            var element = BuildParagraph(paragraph);
                            ApplyListFormatting(element, clampedLevel, list.Ordered, numbered);
                            numbered = true;
                            target.Append(element);
                            break;

                        case ListBlock nested:
                            WriteList(nested, target, level + 1, ct);
                            break;

                        default:
                            WriteBlock(block, target, ct);
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Маркер вешается только на первый абзац пункта: продолжение пункта выравнивается по нему,
        /// но своего маркера не получает.
        /// </summary>
        private static void ApplyListFormatting(Paragraph paragraph, int level, bool ordered, bool alreadyNumbered)
        {
            var properties = new ParagraphProperties();

            if (!alreadyNumbered)
            {
                properties.Append(new NumberingProperties(
                    new NumberingLevelReference { Val = level },
                    new NumberingId { Val = NumberingFactory.NumberIdFor(ordered) }));
            }

            // Продолжение пункта выравнивается по тексту первого абзаца, поэтому выступа у него нет.
            var indentation = new Indentation { Left = DocxUnits.Twips(720 * (level + 1)) };

            if (alreadyNumbered)
            {
                indentation.FirstLine = "0";
            }
            else
            {
                indentation.Hanging = DocxUnits.Twips(360);
            }

            properties.Append(indentation);

            paragraph.InsertAt(properties, 0);
        }

        private void WriteTable(TableBlock block, OpenXmlCompositeElement target)
        {
            var columns = Math.Max(
                block.Headers.Count,
                block.Rows.Count == 0 ? 0 : block.Rows.Max(row => row.Count));

            if (columns == 0)
            {
                return;
            }

            _tableNumber++;
            target.Append(BuildCaption(string.IsNullOrWhiteSpace(block.Caption)
                ? $"Таблица {_tableNumber}"
                : $"Таблица {_tableNumber} — {block.Caption}"));

            var columnWidths = ComputeColumnWidths(block, columns, _textWidthTwips);

            var table = new Table(
                new TableProperties(
                    new TableStyle { Val = StyleDefinitionsFactory.TableStyleId },
                    new TableWidth { Width = DocxUnits.Twips(_textWidthTwips), Type = TableWidthUnitValues.Dxa },
                    // Фиксированная раскладка: Word уважает наши w:gridCol, а не пересчитывает по содержимому.
                    new TableLayout { Type = TableLayoutValues.Fixed },
                    // Только w:val: остальные атрибуты tblLook — из строгой схемы 2010, транзитивная их не знает.
                    new TableLook { Val = "04A0" }),
                new TableGrid(columnWidths.Select(width => new GridColumn { Width = DocxUnits.Twips(width) })));

            if (block.Headers.Count > 0)
            {
                table.Append(BuildRow(block.Headers, columns, columnWidths, header: true));
            }

            foreach (var row in block.Rows)
            {
                table.Append(BuildRow(row, columns, columnWidths, header: false));
            }

            target.Append(table);

            // После таблицы Word требует абзац, иначе две таблицы подряд склеятся в одну.
            target.Append(new Paragraph());
        }

        /// <summary>
        /// Ширины колонок пропорционально самой длинной ячейке в каждой, с зажимом доли в
        /// <c>[10%, 60%]</c> — узкие колонки не схлопываются, широкие не съедают всю строку.
        /// Равномерное деление остаётся вырожденным случаем (одинаковая длина содержимого).
        /// </summary>
        private static int[] ComputeColumnWidths(TableBlock block, int columns, int totalTwips)
        {
            const double minShare = 0.10;
            const double maxShare = 0.60;

            var lengths = new int[columns];
            for (var c = 0; c < columns; c++)
            {
                var longest = c < block.Headers.Count ? block.Headers[c]?.Length ?? 0 : 0;
                foreach (var row in block.Rows)
                {
                    if (c < row.Count)
                    {
                        longest = Math.Max(longest, row[c]?.Length ?? 0);
                    }
                }

                lengths[c] = Math.Max(1, longest);
            }

            double totalLength = lengths.Sum();
            var shares = new double[columns];
            var shareSum = 0d;
            for (var c = 0; c < columns; c++)
            {
                shares[c] = Math.Clamp(lengths[c] / totalLength, minShare, maxShare);
                shareSum += shares[c];
            }

            var widths = new int[columns];
            var used = 0;
            for (var c = 0; c < columns; c++)
            {
                widths[c] = c == columns - 1
                    ? Math.Max(1, totalTwips - used)
                    : Math.Max(1, (int)Math.Round(totalTwips * shares[c] / shareSum, MidpointRounding.AwayFromZero));
                used += widths[c];
            }

            return widths;
        }

        private static TableRow BuildRow(IReadOnlyList<string> cells, int columns, int[] columnWidths, bool header)
        {
            var row = new TableRow();

            if (header)
            {
                // Шапка повторяется на каждой странице — требование к многостраничным таблицам.
                row.Append(new TableRowProperties(new TableHeader()));
            }

            for (var i = 0; i < columns; i++)
            {
                var text = i < cells.Count ? cells[i] : string.Empty;
                var run = BuildRun(new InlineRun(text, header ? InlineStyle.Bold : InlineStyle.None));

                row.Append(new TableCell(
                    new TableCellProperties(
                        new TableCellWidth { Width = DocxUnits.Twips(columnWidths[i]), Type = TableWidthUnitValues.Dxa },
                        new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }),
                    new Paragraph(
                        new ParagraphProperties(
                            new SpacingBetweenLines
                            {
                                Line = "240",
                                LineRule = LineSpacingRuleValues.Auto,
                                Before = "20",
                                After = "20",
                            },
                            new Indentation { FirstLine = "0" },
                            new Justification { Val = header ? JustificationValues.Center : JustificationValues.Left }),
                        run)));
            }

            return row;
        }

        private void WriteCode(CodeBlock block, OpenXmlCompositeElement target)
        {
            var lines = block.Code.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            foreach (var line in lines)
            {
                target.Append(new Paragraph(
                    new ParagraphProperties(
                        new ParagraphStyleId { Val = StyleDefinitionsFactory.CodeBlockStyleId }),
                    new Run(new Text(line) { Space = SpaceProcessingModeValues.Preserve })));
            }
        }

        private void WriteImage(ImageBlock block, OpenXmlCompositeElement target)
        {
            var paragraph = ImageWriter.TryBuild(
                main, block, baseDirectory: null, _textWidthTwips, _imageMaxHeightTwips, _drawingId);

            if (paragraph is null)
            {
                logger.LogWarning("Изображение не вставлено, источник недоступен: {Source}", block.PathOrBase64);
                target.Append(ImageWriter.BuildMissingPlaceholder(profile, block.PathOrBase64));
                return;
            }

            _drawingId++;
            _figureNumber++;
            target.Append(paragraph);
            target.Append(BuildCaption(
                string.IsNullOrWhiteSpace(block.Caption)
                    ? $"Рисунок {_figureNumber}"
                    : $"Рисунок {_figureNumber} — {block.Caption}",
                JustificationValues.Center));
        }

        private static Paragraph BuildCaption(string text, JustificationValues? alignment = null)
        {
            var properties = new ParagraphProperties(
                new ParagraphStyleId { Val = StyleDefinitionsFactory.CaptionStyleId });

            if (alignment is { } value)
            {
                properties.Append(new Justification { Val = value });
            }

            return new Paragraph(properties, new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }
    }
}
