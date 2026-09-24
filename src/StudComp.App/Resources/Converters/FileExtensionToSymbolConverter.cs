using System.Globalization;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace StudComp.Resources.Converters;

/// <summary>
/// Расширение файла (без точки, любой регистр) → иконка типа для карточки «Неразобранного» (Phase 12.2,
/// new_addons.md §4). Единый outline-стиль (ARCHITECTURE §19.4) — берём готовые Fluent-символы WPF-UI,
/// не смешиваем с другими иконостилями.
/// </summary>
public sealed class FileExtensionToSymbolConverter : IValueConverter
{
    /// <summary>Готовый экземпляр для <c>x:Static</c> в XAML.</summary>
    public static FileExtensionToSymbolConverter Instance { get; } = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var extension = (value as string)?.ToUpperInvariant() ?? string.Empty;
        return extension switch
        {
            "PNG" or "JPG" or "JPEG" or "GIF" or "BMP" or "WEBP" => SymbolRegular.Image24,
            "PDF" => SymbolRegular.DocumentPdf24,
            "XLSX" or "XLS" or "CSV" => SymbolRegular.DocumentTable24,
            "CS" or "PY" or "JS" or "TS" or "JSON" or "XML" or "HTML" or "CSS" or "SQL" or "XAML" =>
                SymbolRegular.Code24,
            "MP4" or "AVI" or "MKV" or "MOV" => SymbolRegular.VideoClip24,
            "MP3" or "WAV" or "FLAC" => SymbolRegular.MusicNote224,
            _ => SymbolRegular.Document24,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
