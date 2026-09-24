using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Core.Domain;
using StudComp.Modules.Archivist.Services;

namespace StudComp.ViewModels.Archivist;

/// <summary>
/// Строка списка «Неразобранное» (Phase 12.2, new_addons.md §4). Текстовые свойства считаются один раз
/// в конструкторе (дёшево — только манипуляции со строками); размер файла и подсказки-кандидаты
/// приходят готовыми параметрами — их вычисление (диск/токенная похожесть) сделано пачкой на весь
/// список в <see cref="UnsortedFilesViewModel.RefreshAsync"/>, чтобы не блокировать UI-поток и не
/// плодить N отдельных операций на каждую строку.
/// </summary>
public sealed partial class UnsortedFileRowViewModel : ObservableObject
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    private readonly IReadOnlyList<Subject> _subjects;

    public UnsortedFileRowViewModel(
        FileRecord record, long sizeBytes, IReadOnlyList<UnsortedSuggestion> suggestions, IReadOnlyList<Subject> subjects)
    {
        _subjects = subjects;
        Record = record;
        Path = record.CurrentPath.Length > 0 ? record.CurrentPath : record.OriginalPath;
        FileName = System.IO.Path.GetFileName(Path);
        FolderName = System.IO.Path.GetDirectoryName(Path) ?? string.Empty;
        DetectedText = record.DetectedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm", Russian);
        Extension = System.IO.Path.GetExtension(Path).TrimStart('.').ToUpperInvariant();

        // Quarantined здесь означает «не смогли разложить»: дубликат, занятый файл, недоступная папка.
        IsBlocked = record.Status == FileRecordStatus.Quarantined;
        StatusText = IsBlocked ? "Не удалось разложить" : "Ждёт разбора";

        SizeText = FormatSize(sizeBytes);
        Suggestions = suggestions;
    }

    public FileRecord Record { get; }

    public string Path { get; }

    public string FileName { get; }

    public string FolderName { get; }

    public string DetectedText { get; }

    public string SizeText { get; }

    public string StatusText { get; }

    public bool IsBlocked { get; }

    /// <summary>Расширение без точки, в верхнем регистре — для иконки типа файла в карточке.</summary>
    public string Extension { get; }

    /// <summary>1–3 предмета-кандидата по похожести имени (ARCHITECTURE §8.4 п.7), вычислены один раз.</summary>
    public IReadOnlyList<UnsortedSuggestion> Suggestions { get; }

    public bool HasSuggestions => Suggestions.Count > 0;

    /// <summary>Отражение выделения строки в <c>ListBox</c> — для панели пакетной сортировки.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Инлайн-выбор предмета в строке — заменяет прежний единый выбор на всю вкладку.</summary>
    [ObservableProperty]
    private Subject? _selectedSubject;

    /// <summary>Клик по чипу-подсказке — мгновенно проставляет предмет в строке (без запуска сортировки).</summary>
    [RelayCommand]
    private void ApplySuggestion(UnsortedSuggestion? suggestion)
    {
        if (suggestion is not null)
        {
            SelectedSubject = _subjects.FirstOrDefault(s => s.Id == suggestion.SubjectId);
        }
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} Б",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} КБ",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} МБ",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} ГБ",
    };
}
