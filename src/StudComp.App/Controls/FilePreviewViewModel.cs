using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using StudComp.Services;

namespace StudComp.Controls;

/// <summary>Что показывает панель предпросмотра для текущего файла.</summary>
public enum FilePreviewKind
{
    None,
    Loading,
    Image,
    Text,
    Docx,
    ExternalOnly,
    Error,
}

/// <summary>
/// Быстрый предпросмотр файла (ARCHITECTURE §1.10, new_addons.md §4): изображения — напрямую,
/// текст/код — как есть, DOCX — первые абзацы best-effort, PDF и прочее — карточка «Открыть во внешнем».
/// Строится off-thread с дебаунсом и отменой — тот же паттерн, что <c>RuleEditorViewModel.SchedulePreview</c>,
/// только без диалогового контекста: контрол сам реагирует на смену пути через <see cref="Load"/>.
/// </summary>
public sealed partial class FilePreviewViewModel(IShellLauncher shell) : ObservableObject, IDisposable
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".md", ".markdown", ".log", ".json", ".xml", ".csv", ".cs", ".xaml", ".yml", ".yaml", ".ini",
        ".config", ".py", ".js", ".ts", ".html", ".css", ".sql",
    };

    private const int TextByteLimit = 64 * 1024;
    private const int ImageDecodePixelWidth = 640;
    private const int DocxParagraphLimit = 20;

    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private FilePreviewKind _kind = FilePreviewKind.None;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileName))]
    private string? _path;

    public string FileName => string.IsNullOrEmpty(Path) ? string.Empty : System.IO.Path.GetFileName(Path);

    [ObservableProperty]
    private BitmapImage? _image;

    [ObservableProperty]
    private string? _textContent;

    [ObservableProperty]
    private string _infoLine = string.Empty;

    [ObservableProperty]
    private string? _errorText;

    // Булевы проекции Kind для XAML-триггеров — в проекте принято сравнивать булевы свойства
    // (IsUrgent/IsOverdue и т.п.), а не заводить конвертер enum→Visibility ради одного контрола.
    public bool IsNone => Kind == FilePreviewKind.None;
    public bool IsLoading => Kind == FilePreviewKind.Loading;
    public bool IsImage => Kind == FilePreviewKind.Image;
    public bool IsText => Kind == FilePreviewKind.Text;
    public bool IsDocx => Kind == FilePreviewKind.Docx;
    public bool IsExternalOnly => Kind == FilePreviewKind.ExternalOnly;
    public bool IsError => Kind == FilePreviewKind.Error;

    partial void OnKindChanged(FilePreviewKind value)
    {
        OnPropertyChanged(nameof(IsNone));
        OnPropertyChanged(nameof(IsLoading));
        OnPropertyChanged(nameof(IsImage));
        OnPropertyChanged(nameof(IsText));
        OnPropertyChanged(nameof(IsDocx));
        OnPropertyChanged(nameof(IsExternalOnly));
        OnPropertyChanged(nameof(IsError));
    }

    /// <summary>Запросить построение предпросмотра для нового пути. <see langword="null"/> — очистить панель.</summary>
    public void Load(string? path)
    {
        _cts?.Cancel();
        Path = path;
        Image = null;
        TextContent = null;
        ErrorText = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            Kind = FilePreviewKind.None;
            return;
        }

        Kind = FilePreviewKind.Loading;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _ = LoadAsync(path, cts.Token);
    }

    [RelayCommand]
    private void OpenExternal()
    {
        if (!string.IsNullOrWhiteSpace(Path))
        {
            shell.OpenFile(Path);
        }
    }

    private async Task LoadAsync(string path, CancellationToken ct)
    {
        try
        {
            // Дебаунс: при быстром листании строк списка отменяем недостроенные превью, не начиная их.
            await Task.Delay(180, ct).ConfigureAwait(true);

            var built = await Task.Run(() => Build(path, ct), ct).ConfigureAwait(true);
            if (ct.IsCancellationRequested)
            {
                return;
            }

            Apply(built);
        }
        catch (OperationCanceledException)
        {
            // Заменено более свежим вызовом Load — ничего применять не нужно.
        }
        catch (Exception)
        {
            if (!ct.IsCancellationRequested)
            {
                Kind = FilePreviewKind.Error;
                ErrorText = "Не удалось построить предпросмотр.";
            }
        }
    }

    private void Apply(BuiltPreview built)
    {
        Kind = built.Kind;
        Image = built.Image;
        TextContent = built.Text;
        InfoLine = built.InfoLine;
        ErrorText = built.Error;
    }

    /// <summary>Вся тяжёлая часть — диск, декодирование — работает off-thread, без UI-типов на входе.</summary>
    private static BuiltPreview Build(string path, CancellationToken ct)
    {
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists)
            {
                return new BuiltPreview(FilePreviewKind.Error, Error: "Файл не найден.");
            }
        }
        catch (Exception)
        {
            return new BuiltPreview(FilePreviewKind.Error, Error: "Не удалось прочитать файл.");
        }

        var extension = System.IO.Path.GetExtension(path);
        var infoLine = $"{extension.TrimStart('.').ToUpperInvariant()} · {FormatSize(info.Length)} · {info.LastWriteTime:dd.MM.yyyy HH:mm}";

        if (ImageExtensions.Contains(extension))
        {
            var image = BuildImage(path);
            return image is null
                ? new BuiltPreview(FilePreviewKind.Error, InfoLine: infoLine, Error: "Не удалось открыть изображение.")
                : new BuiltPreview(FilePreviewKind.Image, Image: image, InfoLine: infoLine);
        }

        if (string.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            // Решённый открытый вопрос (new_addons.md §9 п.5): рендер PDF требует нового пакета —
            // пакеты заморожены, поэтому PDF всегда открывается во внешнем приложении.
            return new BuiltPreview(FilePreviewKind.ExternalOnly, InfoLine: infoLine);
        }

        if (string.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
        {
            var text = BuildDocxPreview(path);
            return text is null
                ? new BuiltPreview(FilePreviewKind.ExternalOnly, InfoLine: infoLine)
                : new BuiltPreview(FilePreviewKind.Docx, Text: text, InfoLine: infoLine);
        }

        if (TextExtensions.Contains(extension))
        {
            var text = BuildTextPreview(path, ct);
            return new BuiltPreview(FilePreviewKind.Text, Text: text, InfoLine: infoLine);
        }

        return new BuiltPreview(FilePreviewKind.ExternalOnly, InfoLine: infoLine);
    }

    private static BitmapImage? BuildImage(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = ImageDecodePixelWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze(); // заморожен — безопасно передавать на UI-поток из фонового Task.Run
            return image;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string BuildTextPreview(string path, CancellationToken ct)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[Math.Min(TextByteLimit, stream.Length)];
        var read = stream.Read(buffer, 0, buffer.Length);
        ct.ThrowIfCancellationRequested();

        var text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
        return stream.Length > read ? text + "\n\n[…файл больше — открыт лишь фрагмент…]" : text;
    }

    private static string? BuildDocxPreview(string path)
    {
        try
        {
            using var document = WordprocessingDocument.Open(path, isEditable: false);
            var body = document.MainDocumentPart?.Document.Body;
            if (body is null)
            {
                return null;
            }

            var paragraphs = body.Descendants<Paragraph>()
                .Select(p => p.InnerText)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Take(DocxParagraphLimit);

            var text = string.Join(Environment.NewLine + Environment.NewLine, paragraphs);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception)
        {
            // Битый или защищённый паролем файл — откат на «Открыть во внешнем».
            return null;
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} КБ",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} МБ",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} ГБ",
    };

    public void Dispose() => _cts?.Cancel();

    private sealed record BuiltPreview(
        FilePreviewKind Kind,
        BitmapImage? Image = null,
        string? Text = null,
        string InfoLine = "",
        string? Error = null);
}
