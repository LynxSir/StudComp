using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты истории отмены/повтора холста рисунка (new_addons.md §12, Phase 13.7) — стек полных
/// снимков, а не команд.
/// </summary>
public sealed class NoteDrawingHistoryTests
{
    private static NoteDrawingElement Stroke(string colorHex = "#000000") =>
        new(NoteDrawingElementKind.Stroke, [new NoteDrawingPoint(0, 0), new NoteDrawingPoint(1, 1)], colorHex, 2);

    [Fact]
    public void Empty_history_cannot_undo_or_redo()
    {
        var history = new NoteDrawingHistory();

        Assert.False(history.CanUndo);
        Assert.False(history.CanRedo);
        Assert.Empty(history.Elements);
    }

    [Fact]
    public void Undo_on_empty_history_is_a_no_op()
    {
        var history = new NoteDrawingHistory();

        history.Undo();

        Assert.Empty(history.Elements);
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void Redo_on_empty_history_is_a_no_op()
    {
        var history = new NoteDrawingHistory();

        history.Redo();

        Assert.Empty(history.Elements);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_replaces_current_state_and_enables_undo()
    {
        var history = new NoteDrawingHistory();
        IReadOnlyList<NoteDrawingElement> state = [Stroke()];

        history.Push(state);

        Assert.Same(state, history.Elements);
        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Undo_restores_the_previous_snapshot_and_enables_redo()
    {
        var history = new NoteDrawingHistory();
        IReadOnlyList<NoteDrawingElement> first = [Stroke("#111111")];
        IReadOnlyList<NoteDrawingElement> second = [Stroke("#111111"), Stroke("#222222")];
        history.Push(first);
        history.Push(second);

        history.Undo();

        Assert.Same(first, history.Elements);
        Assert.True(history.CanRedo);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void Redo_after_undo_restores_the_newer_snapshot()
    {
        var history = new NoteDrawingHistory();
        IReadOnlyList<NoteDrawingElement> first = [Stroke("#111111")];
        IReadOnlyList<NoteDrawingElement> second = [Stroke("#111111"), Stroke("#222222")];
        history.Push(first);
        history.Push(second);
        history.Undo();

        history.Redo();

        Assert.Same(second, history.Elements);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_after_undo_discards_the_redo_branch()
    {
        var history = new NoteDrawingHistory();
        IReadOnlyList<NoteDrawingElement> first = [Stroke("#111111")];
        IReadOnlyList<NoteDrawingElement> second = [Stroke("#111111"), Stroke("#222222")];
        IReadOnlyList<NoteDrawingElement> third = [Stroke("#333333")];
        history.Push(first);
        history.Push(second);
        history.Undo();

        history.Push(third);

        Assert.Same(third, history.Elements);
        Assert.False(history.CanRedo);
        Assert.True(history.CanUndo);
    }

    [Fact]
    public void Repeated_undo_walks_all_the_way_back_to_the_initial_state()
    {
        var history = new NoteDrawingHistory();
        history.Push([Stroke("#1")]);
        history.Push([Stroke("#1"), Stroke("#2")]);
        history.Push([Stroke("#1"), Stroke("#2"), Stroke("#3")]);

        history.Undo();
        history.Undo();
        history.Undo();

        Assert.Empty(history.Elements);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
    }

    [Fact]
    public void Initial_state_is_preserved_until_the_first_push()
    {
        IReadOnlyList<NoteDrawingElement> initial = [Stroke("#abcdef")];

        var history = new NoteDrawingHistory(initial);

        Assert.Same(initial, history.Elements);
        Assert.False(history.CanUndo);
    }
}
