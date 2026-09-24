using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using StudComp.Core.Abstractions.ReportForge;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace StudComp.Modules.ReportForge.Services.Docx;

/// <summary>
/// Вставка картинок: часть <c>ImagePart</c> плюс встроенный (inline) рисунок с размерами, вписанными
/// в ширину текстового блока (ARCHITECTURE §10.5).
/// </summary>
internal static class ImageWriter
{
    /// <summary>Абзац с картинкой либо <see langword="null"/>, если файл недоступен или формат неизвестен.</summary>
    /// <param name="main">Часть документа, куда добавляется <c>ImagePart</c>.</param>
    /// <param name="image">Блок модели: путь к файлу или <c>data:</c>-URI.</param>
    /// <param name="baseDirectory">Каталог, относительно которого разрешаются относительные пути.</param>
    /// <param name="maxWidthTwips">Полезная ширина строки — шире картинка не будет.</param>
    /// <param name="maxHeightTwips">Полезная высота страницы (за вычетом резерва под подпись) — выше картинка не будет.</param>
    /// <param name="drawingId">Сквозной номер рисунка в пакете (должен быть уникален).</param>
    internal static Paragraph? TryBuild(
        MainDocumentPart main,
        ImageBlock image,
        string? baseDirectory,
        int maxWidthTwips,
        int maxHeightTwips,
        uint drawingId)
    {
        var content = TryReadBytes(image.PathOrBase64, baseDirectory);
        if (content is not { } bytes || bytes.Length == 0)
        {
            return null;
        }

        var partType = ResolvePartType(image.PathOrBase64, bytes);
        if (partType is not { } imagePartType)
        {
            return null;
        }

        var part = main.AddImagePart(imagePartType);
        using (var stream = new MemoryStream(bytes, writable: false))
        {
            part.FeedData(stream);
        }

        var (widthEmu, heightEmu) = Measure(bytes, maxWidthTwips, maxHeightTwips, image.Width);
        var relationshipId = main.GetIdOfPart(part);

        return new Paragraph(
            new ParagraphProperties(
                new KeepNext(),
                new Indentation { FirstLine = "0" },
                new Justification { Val = JustificationValues.Center }),
            new Run(BuildDrawing(relationshipId, widthEmu, heightEmu, drawingId)));
    }

    /// <summary>Абзац-заглушка вместо картинки, которую не удалось прочитать: молча терять её нельзя.</summary>
    internal static Paragraph BuildMissingPlaceholder(GostStyleProfile profile, string pathOrBase64)
    {
        var shown = pathOrBase64.Length > 120 ? pathOrBase64[..120] + "…" : pathOrBase64;
        return ParagraphFactory.Text(profile, $"[Изображение не найдено: {shown}]", JustificationValues.Center);
    }

    private static (long WidthEmu, long HeightEmu) Measure(byte[] bytes, int maxWidthTwips, int maxHeightTwips, int? requestedWidth)
    {
        var maxWidthEmu = DocxUnits.TwipsToEmu(maxWidthTwips);
        var maxHeightEmu = DocxUnits.TwipsToEmu(maxHeightTwips);
        var size = ImageSizeReader.TryRead(bytes);

        long widthEmu;
        long heightEmu;

        if (size is { Width: > 0, Height: > 0 } pixels)
        {
            var dpiX = pixels.DpiX > 0 ? pixels.DpiX : ImageSizeReader.DefaultDpi;
            var dpiY = pixels.DpiY > 0 ? pixels.DpiY : ImageSizeReader.DefaultDpi;
            widthEmu = DocxUnits.PixelsToEmu(pixels.Width, dpiX);
            heightEmu = DocxUnits.PixelsToEmu(pixels.Height, dpiY);
        }
        else
        {
            // Размеры не прочитались — занимаем всю ширину при пропорции 4:3, это лучше нулевого extent.
            widthEmu = maxWidthEmu;
            heightEmu = maxWidthEmu * 3 / 4;
        }

        if (requestedWidth is >= 32 and <= 2400)
        {
            var requestedEmu = DocxUnits.PixelsToEmu(requestedWidth.Value, 96);
            heightEmu = Math.Max(1, heightEmu * requestedEmu / widthEmu);
            widthEmu = requestedEmu;
        }

        // Шире строки не бывает: ужимаем пропорционально (ARCHITECTURE §10.5).
        if (widthEmu > maxWidthEmu)
        {
            heightEmu = Math.Max(1L, heightEmu * maxWidthEmu / widthEmu);
            widthEmu = maxWidthEmu;
        }

        // Выше полезной высоты страницы — тоже ужимаем, иначе рисунок уедет за нижнее поле.
        if (heightEmu > maxHeightEmu)
        {
            widthEmu = Math.Max(1L, widthEmu * maxHeightEmu / heightEmu);
            heightEmu = maxHeightEmu;
        }

        return (widthEmu, heightEmu);
    }

    private static byte[]? TryReadBytes(string pathOrBase64, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(pathOrBase64))
        {
            return null;
        }

        if (pathOrBase64.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            var comma = pathOrBase64.IndexOf(',');
            if (comma < 0)
            {
                return null;
            }

            try
            {
                return Convert.FromBase64String(pathOrBase64[(comma + 1)..]);
            }
            catch (FormatException)
            {
                return null;
            }
        }

        // Сетевые адреса не тянем: приложение офлайновое по умолчанию (ARCHITECTURE §11.6).
        if (Uri.TryCreate(pathOrBase64, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return null;
        }

        try
        {
            var path = Path.IsPathRooted(pathOrBase64) || string.IsNullOrEmpty(baseDirectory)
                ? pathOrBase64
                : Path.Combine(baseDirectory, pathOrBase64);

            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static PartTypeInfo? ResolvePartType(string pathOrBase64, byte[] bytes)
    {
        // Расширение — подсказка, но решают байты: у скачанных картинок расширение врёт регулярно.
        if (bytes.Length >= 4)
        {
            if (bytes[0] == 0x89 && bytes[1] == 'P' && bytes[2] == 'N' && bytes[3] == 'G')
            {
                return ImagePartType.Png;
            }

            if (bytes[0] == 0xFF && bytes[1] == 0xD8)
            {
                return ImagePartType.Jpeg;
            }

            if (bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F')
            {
                return ImagePartType.Gif;
            }

            if (bytes[0] == 'B' && bytes[1] == 'M')
            {
                return ImagePartType.Bmp;
            }
        }

        return Path.GetExtension(pathOrBase64).ToLowerInvariant() switch
        {
            ".png" => ImagePartType.Png,
            ".jpg" or ".jpeg" => ImagePartType.Jpeg,
            ".gif" => ImagePartType.Gif,
            ".bmp" => ImagePartType.Bmp,
            ".tif" or ".tiff" => ImagePartType.Tiff,
            _ => null,
        };
    }

    private static Drawing BuildDrawing(string relationshipId, long widthEmu, long heightEmu, uint drawingId)
    {
        var name = $"Рисунок {drawingId}";

        return new Drawing(
            new DW.Inline(
                new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                new DW.DocProperties { Id = drawingId, Name = name },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(new A.GraphicData(
                    new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties { Id = 0U, Name = name },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(
                            new A.Blip { Embed = relationshipId },
                            new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(
                            new A.Transform2D(
                                new A.Offset { X = 0L, Y = 0L },
                                new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                            new A.PresetGeometry(new A.AdjustValueList())
                            {
                                Preset = A.ShapeTypeValues.Rectangle,
                            })))
                {
                    Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture",
                }))
            {
                DistanceFromTop = 0U,
                DistanceFromBottom = 0U,
                DistanceFromLeft = 0U,
                DistanceFromRight = 0U,
            });
    }
}
