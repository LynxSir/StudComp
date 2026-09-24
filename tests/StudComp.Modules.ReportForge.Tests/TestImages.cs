using System.Buffers.Binary;

namespace StudComp.Modules.ReportForge.Tests;

/// <summary>
/// Тестовая картинка. Хранится строкой base64, а не файлом в репозитории: бинарь в git ради 233 байт
/// не нужен, а так содержимое очевидно и одинаково в каждом прогоне (важно для golden-теста).
/// </summary>
internal static class TestImages
{
    /// <summary>Ширина <see cref="SamplePng"/> в пикселях.</summary>
    internal const int SampleWidth = 120;

    /// <summary>Высота <see cref="SamplePng"/> в пикселях.</summary>
    internal const int SampleHeight = 80;

    /// <summary>PNG 120×80 — шахматка, RGB без альфа-канала.</summary>
    private const string SamplePngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAHgAAABQCAIAAABd+SbeAAAAsElEQVR42u3ZMQ0AQAgDQOS8fxXIegeEiZBw3bvc2MYrk2V0" +
        "+92ABRo0aNCgQcMCDRo06LXQOGa6oEGDBg0aNGhYoEGDBr0XGoetAzRo0KBBwwINGjRo0D5DHLYO0KBBgwYNCzRo0KBB+wxx" +
        "2DpAgwYNGjQs0KBBgwbtM8Rh6wANGjRo0LBAgwYNGrTPEIetA7QuaNCgYYEGrQsatM8Qh60DtC5o0KBhgQatCxr0SegPJUW/" +
        "DWZVLdMAAAAASUVORK5CYII=";

    internal static byte[] SamplePng => Convert.FromBase64String(SamplePngBase64);

    /// <summary>Кладёт тестовый PNG по указанному пути, создавая недостающие папки.</summary>
    internal static string Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, SamplePng);
        return path;
    }

    /// <summary><c>data:</c>-URI той же картинки — для проверки вставки без файла на диске.</summary>
    internal static string SampleDataUri => "data:image/png;base64," + SamplePngBase64;

    /// <summary>
    /// Синтетический PNG нужного размера: настоящая сигнатура + <c>IHDR</c> (+ по желанию <c>pHYs</c>) + <c>IEND</c>.
    /// Пиксельных данных нет — рендереру и <see cref="StudComp.Modules.ReportForge.Services.ImageSizeReader"/>
    /// хватает заголовка, а <c>OpenXmlValidator</c> бинарь картинки не разбирает. CRC не считаем — его никто не проверяет.
    /// </summary>
    internal static byte[] BuildPng(int width, int height, int? pixelsPerMetre = null)
    {
        using var stream = new MemoryStream();

        stream.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.Slice(4, 4), height);
        ihdr[8] = 8;   // глубина
        ihdr[9] = 2;   // тип цвета: truecolor
        WriteChunk(stream, "IHDR"u8, ihdr);

        if (pixelsPerMetre is { } ppm)
        {
            Span<byte> phys = stackalloc byte[9];
            BinaryPrimitives.WriteUInt32BigEndian(phys[..4], (uint)ppm);
            BinaryPrimitives.WriteUInt32BigEndian(phys.Slice(4, 4), (uint)ppm);
            phys[8] = 1;   // единица: метр
            WriteChunk(stream, "pHYs"u8, phys);
        }

        WriteChunk(stream, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return stream.ToArray();

        static void WriteChunk(Stream target, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
            target.Write(length);
            target.Write(type);
            target.Write(data);
            target.Write(stackalloc byte[4]);   // CRC-заглушка
        }
    }

    /// <summary>Плотность в пикселях на метр для заданного DPI (нужна для чанка <c>pHYs</c>).</summary>
    internal static int PixelsPerMetreForDpi(double dpi) => (int)Math.Round(dpi / 0.0254d);
}
