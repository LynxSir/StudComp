namespace StudComp.Services;

/// <summary>
/// Показ модальных диалогов Shell'а (ARCHITECTURE §11.5). Обёртка над WPF-UI
/// <c>IContentDialogService</c> — чтобы ViewModel'и не зависели от типов конкретной UI-библиотеки
/// (ADR: диалоги Phase 4).
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Открыть диалог-редактор: содержимое рендерится по <c>DataTemplate</c> для типа
    /// <paramref name="editorViewModel"/>. Кнопка «Сохранить» активна, пока у VM свойство
    /// <c>CanSave</c> истинно. Возвращает <see langword="true"/>, если пользователь подтвердил.
    /// </summary>
    /// <param name="dialogMaxWidth">
    /// Максимальная ширина диалога — стандартных 560px мало для холста рисования (Phase 13.7), поэтому
    /// параметр вынесен наружу вместо жёсткого значения внутри реализации.
    /// </param>
    Task<bool> ShowEditorAsync(
        object editorViewModel, string title, string primaryButton = "Сохранить", double dialogMaxWidth = 560);

    /// <summary>Диалог подтверждения с текстом и двумя кнопками. <see langword="true"/> — подтверждено.</summary>
    Task<bool> ConfirmAsync(string title, string message, string primaryButton = "Удалить");

    /// <summary>
    /// Справочный диалог с одной кнопкой «Закрыть»: содержимое рендерится по <c>DataTemplate</c>,
    /// подтверждать нечего. Отдельный метод, а не перегрузка <see cref="ShowEditorAsync"/> — там
    /// привязка к <c>CanSave</c> вернула бы на экран кнопку подтверждения.
    /// </summary>
    Task ShowInfoAsync(object viewModel, string title, double maxWidth = 760);

    /// <summary>
    /// Системный диалог выбора папки. Возвращает выбранный путь либо <see langword="null"/>, если
    /// пользователь отказался. Нужен Архивариусу — наблюдаемая папка и корень архива (ARCHITECTURE §8.7).
    /// </summary>
    string? PickFolder(string title, string? initialPath = null);

    /// <summary>
    /// Системный диалог открытия файла. <paramref name="filter"/> — в формате WPF
    /// (<c>"Markdown (*.md)|*.md"</c>). Возвращает путь либо <see langword="null"/> при отказе.
    /// </summary>
    string? PickOpenFile(string title, string filter, string? initialPath = null);

    /// <summary>Выбор нескольких файлов; пустой список — отмена.</summary>
    IReadOnlyList<string> PickOpenFiles(string title, string filter, string? initialPath = null);

    /// <summary>
    /// Системный диалог «Сохранить как». <paramref name="suggestedPath"/> задаёт и папку, и имя файла
    /// по умолчанию. Возвращает путь либо <see langword="null"/> при отказе.
    /// </summary>
    string? PickSaveFile(string title, string filter, string? suggestedPath = null);
}
