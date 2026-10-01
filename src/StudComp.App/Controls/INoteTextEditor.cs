using StudComp.Core.Domain;

namespace StudComp.Controls;

/// <summary>
/// Кто именно применяет правку к тексту заметки. В режиме правки это живое поле ввода — правка
/// ложится в его стек отмены пошагово; в предпросмотре поля нет, и вьюмодель пишет текст целиком,
/// что даёт ровно один шаг отмены на операцию.
/// </summary>
/// <remarks>
/// Интерфейс нужен, чтобы вьюмодель не знала про <c>TextBox</c>: его реализует код-behind
/// <see cref="NoteEditor"/> и отдаёт себя вьюмодели при смене <c>DataContext</c>.
/// </remarks>
public interface INoteTextEditor
{
    /// <summary>Поле ввода на экране и готово принять правку.</summary>
    bool IsLive { get; }

    /// <summary>Текущая позиция каретки.</summary>
    int Caret { get; }

    /// <summary>Применить правку одним шагом отмены. <see langword="false"/> — текст ушёл вперёд.</summary>
    bool TryApply(MarkdownEdit edit);

    /// <summary>
    /// Забыть историю отмены. Обязательно при смене заметки: поле ввода одно на всю жизнь
    /// редактора, и без сброса <c>Ctrl+Z</c> в новой заметке откатил бы её текст к тексту
    /// предыдущей, а автосохранение записало бы чужой текст в базу.
    /// </summary>
    void ResetUndoHistory();

    /// <summary>Отменить последнюю правку — <c>Ctrl+Z</c> из предпросмотра, где поля ввода не видно.</summary>
    bool TryUndo();
}
