namespace StudComp.Core.Domain;

/// <summary>
/// История отмены/повтора для холста рисунка (new_addons.md §12) — стек полных снимков списка
/// элементов, а не команд: на разумном числе элементов одного рисунка (десятки, не тысячи) это проще и
/// безошибочнее, чем разводить отдельные типы команд для штриха/ластика/фигуры/текста, и гарантирует,
/// что после отмены визуал ровно совпадает с одним из уже показанных ранее состояний.
/// </summary>
public sealed class NoteDrawingHistory
{
    /// <summary>
    /// Сколько шагов отмены хранится. Снимок полный, а каждый штрих несёт все свои точки, поэтому
    /// без потолка длинная сессия рисования растила бы память квадратично.
    /// </summary>
    public const int MaxDepth = 50;

    /// <summary>Список вместо стека: у стека нельзя выбросить самый старый снимок.</summary>
    private readonly List<IReadOnlyList<NoteDrawingElement>> _undo = [];
    private readonly Stack<IReadOnlyList<NoteDrawingElement>> _redo = new();

    public NoteDrawingHistory(IReadOnlyList<NoteDrawingElement>? initial = null)
    {
        Elements = initial ?? [];
    }

    /// <summary>Текущий список элементов — то, что должно быть на холсте прямо сейчас.</summary>
    public IReadOnlyList<NoteDrawingElement> Elements { get; private set; }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Зафиксировать завершённое действие пользователя: новый снимок становится текущим, старый уходит
    /// в стек отмены, а накопленный редо (если после предыдущей отмены рисовали заново) сбрасывается —
    /// это уже другая ветка истории.
    /// </summary>
    public void Push(IReadOnlyList<NoteDrawingElement> newState)
    {
        _undo.Add(Elements);
        if (_undo.Count > MaxDepth)
        {
            _undo.RemoveAt(0);
        }

        Elements = newState;
        _redo.Clear();
    }

    /// <summary>Откатиться к предыдущему снимку. Пустая история — no-op, не бросает.</summary>
    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        _redo.Push(Elements);
        Elements = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
    }

    /// <summary>Вернуть отменённое. Нечего возвращать — no-op, не бросает.</summary>
    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        _undo.Add(Elements);
        Elements = _redo.Pop();
    }
}
