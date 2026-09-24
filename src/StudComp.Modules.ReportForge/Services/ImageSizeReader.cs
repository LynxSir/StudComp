using System.Buffers.Binary;

namespace StudComp.Modules.ReportForge.Services;

/// <summary>Размер изображения в пикселях и его плотность (для картинок без плотности — 96 dpi).</summary>
public readonly record struct ImageSize(int Width, int Height, double DpiX = ImageSizeReader.DefaultDpi, double DpiY = ImageSizeReader.DefaultDpi);

/// <summary>
/// Читает размеры картинки из заголовка файла: PNG, JPEG, GIF, BMP (ADR §16.35).
/// </summary>
/// <remarks>
/// Своё, а не <c>System.Drawing.Common</c> (только Windows, а модуль — <c>net10.0</c>) и не ImageSharp
/// (лишний пакет со своей лицензией) — рендереру от картинки нужны ровно размеры и плотность, чтобы
/// посчитать <c>w:extent</c> и не растянуть изображение.
/// Из плотности разбирается только PNG-чанк <c>pHYs</c> (частый случай — скриншоты 144/192 dpi);
/// для остальных форматов и PNG без <c>pHYs</c> подставляется 96 dpi.
/// </remarks>
public static class ImageSizeReader
{
    /// <summary>Разрешение по умолчанию: столько точек на дюйм подразумевает Word для картинок без явной плотности.</summary>
    public const double DefaultDpi = 96d;

    /// <summary>
    /// Размер изображения либо <see langword="null"/>, если формат не распознан или данных не хватает.
    /// Исключений не бросает — битая картинка не должна ронять генерацию отчёта.
    /// </summary>
    public static ImageSize? TryRead(ReadOnlySpan<byte> bytes) =>
        TryReadPng(bytes) ?? TryReadGif(bytes) ?? TryReadBmp(bytes) ?? TryReadJpeg(bytes);

    private static ImageSize? TryReadPng(ReadOnlySpan<byte> bytes)
    {
        // 8 байт сигнатуры, 4 длина чанка, 4 тип «IHDR», далее ширина и высота big-endian.
        ReadOnlySpan<byte> signature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(signature) || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(20, 4));
        var (dpiX, dpiY) = TryReadPngDensity(bytes);

        return new ImageSize(width, height, dpiX, dpiY);
    }

    /// <summary>
    /// Плотность из чанка <c>pHYs</c> (9 байт: X ppu, Y ppu big-endian + единица; 1 — метры).
    /// Нет чанка, единица не «метры» или данные обрываются — возвращает 96 dpi.
    /// </summary>
    private static (double X, double Y) TryReadPngDensity(ReadOnlySpan<byte> bytes)
    {
        // Чанки идут сразу после IHDR: смещение 8 (сигнатура) + 8 (длина+тип IHDR) + 13 (данные) + 4 (CRC).
        var offset = 33;

        while (offset + 8 <= bytes.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            var type = bytes.Slice(offset + 4, 4);

            if (type.SequenceEqual("IDAT"u8) || type.SequenceEqual("IEND"u8))
            {
                break;
            }

            if (type.SequenceEqual("pHYs"u8) && length == 9 && offset + 8 + 9 <= bytes.Length)
            {
                var data = bytes.Slice(offset + 8, 9);
                var ppuX = BinaryPrimitives.ReadUInt32BigEndian(data[..4]);
                var ppuY = BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4));

                if (data[8] == 1 && ppuX > 0 && ppuY > 0)
                {
                    return (Math.Round(ppuX * 0.0254d), Math.Round(ppuY * 0.0254d));
                }

                return (DefaultDpi, DefaultDpi);
            }

            // length + 4 (длина) + 4 (тип) + 4 (CRC); проверяем на переполнение int.
            var advance = 12L + length;
            if (advance <= 0 || offset + advance > bytes.Length)
            {
                break;
            }

            offset += (int)advance;
        }

        return (DefaultDpi, DefaultDpi);
    }

    private static ImageSize? TryReadGif(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 10 || !bytes[..3].SequenceEqual("GIF"u8))
        {
            return null;
        }

        return new ImageSize(
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(6, 2)),
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(8, 2)));
    }

    private static ImageSize? TryReadBmp(ReadOnlySpan<byte> bytes)
    {
        // BITMAPINFOHEADER: ширина и высота — со смещения 18, little-endian; высота может быть отрицательной.
        if (bytes.Length < 26 || !bytes[..2].SequenceEqual("BM"u8))
        {
            return null;
        }

        var height = BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(22, 4));
        return new ImageSize(BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(18, 4)), Math.Abs(height));
    }

    private static ImageSize? TryReadJpeg(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8)
        {
            return null;
        }

        var offset = 2;
        while (offset + 9 < bytes.Length)
        {
            if (bytes[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var marker = bytes[offset + 1];

            // Маркеры без полезной нагрузки: заполнители, RSTn, начало потока данных.
            if (marker is 0xFF or 0x01 or 0xD8 or (>= 0xD0 and <= 0xD7))
            {
                offset += 2;
                continue;
            }

            if (marker == 0xDA)
            {
                // Пошли сжатые данные — размеров дальше уже не будет.
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 2, 2));

            // SOF0…SOF15, кроме DHT (0xC4), DAC (0xCC) и RSTn: точность, высота, ширина.
            if (marker is (>= 0xC0 and <= 0xCF) and not 0xC4 and not 0xC8 and not 0xCC)
            {
                if (offset + 9 >= bytes.Length)
                {
                    return null;
                }

                return new ImageSize(
                    BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 7, 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset + 5, 2)));
            }

            if (length < 2)
            {
                return null;
            }

            offset += 2 + length;
        }

        return null;
    }
}
