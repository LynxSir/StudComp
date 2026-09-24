using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Автосинтаксис редактора заметок (new_addons.md §11.2, §11.3, §11.6). Правки описываются как
/// замена диапазона, поэтому проверяем и результат применения, и куда встанет каретка.
/// </summary>
public class MarkdownEditingTests
{
    /// <summary>Применяет правку так же, как это сделает поведение над TextBox.</summary>
    private static (string Text, int Caret) Apply(string source, MarkdownEdit edit)
    {
        var text = source[..edit.Start] + edit.Replacement + source[(edit.Start + edit.Length)..];
        return (text, edit.Start + edit.CaretOffset);
    }

    // ── Enter: продолжение списков ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("- пункт", "- пункт\n- ")]
    [InlineData("* пункт", "* пункт\n* ")]
    [InlineData("+ пункт", "+ пункт\n+ ")]
    public void Enter_continues_a_bullet_list(string source, string expected)
    {
        var edit = MarkdownEditing.ContinueLine(source, source.Length);

        Assert.NotNull(edit);
        var (text, caret) = Apply(source, edit.Value);
        Assert.Equal(expected, text);
        Assert.Equal(expected.Length, caret);
    }

    [Theory]
    [InlineData("1. первый", "1. первый\n2. ")]
    [InlineData("9. девятый", "9. девятый\n10. ")]
    [InlineData("1) первый", "1) первый\n2) ")]
    public void Enter_increments_an_ordered_list(string source, string expected)
    {
        var edit = MarkdownEditing.ContinueLine(source, source.Length);

        Assert.NotNull(edit);
        Assert.Equal(expected, Apply(source, edit.Value).Text);
    }

    [Fact]
    public void Enter_keeps_the_indentation_of_a_nested_item()
    {
        const string source = "- верх\n  - вложенный";

        var edit = MarkdownEditing.ContinueLine(source, source.Length);

        Assert.NotNull(edit);
        Assert.Equal("- верх\n  - вложенный\n  - ", Apply(source, edit.Value).Text);
    }

    [Fact]
    public void Enter_continues_a_task_list_with_an_empty_checkbox()
    {
        const string source = "- [x] сделано";

        var edit = MarkdownEditing.ContinueLine(source, source.Length);

        Assert.NotNull(edit);
        Assert.Equal("- [x] сделано\n- [ ] ", Apply(source, edit.Value).Text);
    }

    [Fact]
    public void Enter_on_an_empty_item_leaves_the_list()
    {
        const string source = "- пункт\n- ";

        var edit = MarkdownEditing.ContinueLine(source, source.Length);

        Assert.NotNull(edit);
        var (text, caret) = Apply(source, edit.Value);
        Assert.Equal("- пункт\n", text);
        Assert.Equal("- пункт\n".Length, caret);
    }

    [Fact]
    public void Enter_on_an_empty_nested_item_leaves_the_list_together_with_its_indent()
    {
        const string source = "- верх\n  - ";

        var edit = MarkdownEditing.ContinueLine(source, source.Length);

        Assert.NotNull(edit);
        Assert.Equal("- верх\n", Apply(source, edit.Value).Text);
    }

    [Fact]
    public void Enter_continues_a_quote()
    {
        const string source = "> цитата";

        var edit = MarkdownEditing.ContinueLine(source, source.Length);

        Assert.NotNull(edit);
        Assert.Equal("> цитата\n> ", Apply(source, edit.Value).Text);
    }

    [Fact]
    public void Enter_on_an_empty_quote_leaves_it()
    {
        const string source = "> цитата\n> ";

        var edit = MarkdownEditing.ContinueLine(source, source.Length);

        Assert.NotNull(edit);
        Assert.Equal("> цитата\n", Apply(source, edit.Value).Text);
    }

    [Theory]
    [InlineData("обычный абзац")]
    [InlineData("# Заголовок")]
    [InlineData("")]
    [InlineData("   ")]
    public void Enter_outside_a_list_is_left_alone(string source)
    {
        Assert.Null(MarkdownEditing.ContinueLine(source, source.Length));
    }

    [Fact]
    public void Enter_in_the_middle_of_a_line_is_left_alone()
    {
        const string source = "- пункт";

        Assert.Null(MarkdownEditing.ContinueLine(source, 3));
    }

    [Fact]
    public void Enter_handles_a_caret_beyond_the_text_without_throwing()
    {
        Assert.Null(MarkdownEditing.ContinueLine("абзац", 9999));
        Assert.Null(MarkdownEditing.ContinueLine(null, 5));
    }

    // ── Tab / Shift+Tab: вложенность ────────────────────────────────────────────────────

    [Fact]
    public void Tab_indents_the_current_list_item()
    {
        const string source = "- пункт";

        var edit = MarkdownEditing.Indent(source, source.Length, 0, outdent: false);

        Assert.NotNull(edit);
        Assert.Equal("  - пункт", Apply(source, edit.Value).Text);
    }

    [Fact]
    public void Shift_tab_outdents_the_current_list_item()
    {
        const string source = "  - пункт";

        var edit = MarkdownEditing.Indent(source, source.Length, 0, outdent: true);

        Assert.NotNull(edit);
        Assert.Equal("- пункт", Apply(source, edit.Value).Text);
    }

    [Fact]
    public void Shift_tab_on_a_top_level_item_changes_nothing()
    {
        Assert.Null(MarkdownEditing.Indent("- пункт", 7, 0, outdent: true));
    }

    [Fact]
    public void Tab_indents_every_selected_list_line()
    {
        const string source = "- один\n- два\n- три";

        var edit = MarkdownEditing.Indent(source, 0, source.Length, outdent: false);

        Assert.NotNull(edit);
        Assert.Equal("  - один\n  - два\n  - три", Apply(source, edit.Value).Text);
    }

    [Fact]
    public void Tab_outside_a_list_keeps_its_usual_behaviour()
    {
        Assert.Null(MarkdownEditing.Indent("обычный абзац", 5, 0, outdent: false));
    }

    // ── Ctrl+B / Ctrl+I: обёртка выделения ──────────────────────────────────────────────

    [Fact]
    public void Wrapping_a_selection_adds_the_token_on_both_sides()
    {
        const string source = "жирный текст";

        var edit = MarkdownEditing.ToggleWrap(source, 0, 6, "**");

        Assert.NotNull(edit);
        Assert.Equal("**жирный** текст", Apply(source, edit.Value).Text);
        Assert.Equal(2, edit.Value.CaretOffset);
        Assert.Equal(6, edit.Value.SelectionLength);
    }

    [Fact]
    public void Wrapping_a_selection_that_already_carries_the_token_removes_it()
    {
        const string source = "**жирный** текст";

        var edit = MarkdownEditing.ToggleWrap(source, 0, 10, "**");

        Assert.NotNull(edit);
        Assert.Equal("жирный текст", Apply(source, edit.Value).Text);
    }

    [Fact]
    public void Wrapping_a_selection_inside_an_existing_pair_removes_the_outer_token()
    {
        const string source = "**жирный** текст";

        var edit = MarkdownEditing.ToggleWrap(source, 2, 6, "**");

        Assert.NotNull(edit);
        Assert.Equal("жирный текст", Apply(source, edit.Value).Text);
    }

    [Fact]
    public void Wrapping_without_a_selection_inserts_an_empty_pair_with_the_caret_inside()
    {
        const string source = "текст";

        var edit = MarkdownEditing.ToggleWrap(source, 5, 0, "*");

        Assert.NotNull(edit);
        var (text, caret) = Apply(source, edit.Value);
        Assert.Equal("текст**", text);
        Assert.Equal(6, caret);
    }

    [Fact]
    public void Wrapping_with_an_empty_token_is_left_alone()
    {
        Assert.Null(MarkdownEditing.ToggleWrap("текст", 0, 5, string.Empty));
    }

    // ── Клик ниже последней строки ──────────────────────────────────────────────────────

    [Fact]
    public void Clicking_below_the_text_appends_one_line()
    {
        const string source = "последняя строка";

        var edit = MarkdownEditing.AppendTrailingLine(source);

        Assert.NotNull(edit);
        var (text, caret) = Apply(source, edit.Value);
        Assert.Equal("последняя строка\n", text);
        Assert.Equal(text.Length, caret);
    }

    [Theory]
    [InlineData("")]
    [InlineData("уже с переносом\n")]
    public void Clicking_below_does_not_add_a_second_empty_line(string source)
    {
        Assert.Null(MarkdownEditing.AppendTrailingLine(source));
    }

    // ── Помощник ввода: парные скобки и обёртки ─────────────────────────────────────────

    /// <summary>Набор символа с «пустой» позиции: «abc|» → результат и каретка.</summary>
    private static (string Text, int Caret)? Type(string source, int caret, string typed, int selection = 0)
    {
        var edit = MarkdownEditing.AutoPair(source, caret, selection, typed);
        return edit is null ? null : Apply(source, edit.Value);
    }

    [Theory]
    [InlineData("(", "()")]
    [InlineData("[", "[]")]
    [InlineData("{", "{}")]
    [InlineData("\"", "\"\"")]
    [InlineData("$", "$$")]
    [InlineData("`", "``")]
    public void Opening_symbol_at_the_end_inserts_the_pair_with_the_caret_inside(string typed, string expected)
    {
        var result = Type("x ", 2, typed);

        Assert.NotNull(result);
        Assert.Equal("x " + expected, result.Value.Text);
        Assert.Equal(3, result.Value.Caret);
    }

    [Fact]
    public void Opening_bracket_before_a_space_inserts_the_pair()
    {
        var result = Type("a b", 1, "(");

        Assert.Equal("a() b", result?.Text);
        Assert.Equal(2, result?.Caret);
    }

    [Fact]
    public void Opening_bracket_before_a_closing_bracket_inserts_the_pair()
    {
        var result = Type("f()", 2, "(");

        Assert.Equal("f(())", result?.Text);
    }

    [Fact]
    public void Opening_bracket_inside_a_word_is_left_alone()
    {
        Assert.Null(MarkdownEditing.AutoPair("fx", 1, 0, "("));
    }

    [Theory]
    [InlineData("5", "$")]
    [InlineData("слово", "\"")]
    [InlineData("x", "*")]
    public void Symmetric_token_right_after_a_letter_or_digit_is_left_alone(string source, string typed)
    {
        Assert.Null(MarkdownEditing.AutoPair(source, source.Length, 0, typed));
    }

    [Theory]
    [InlineData("", "*")]
    [InlineData("  ", "*")]
    [InlineData("текст\n", "*")]
    [InlineData("", "_")]
    public void Star_at_the_start_of_a_line_is_a_list_marker_not_a_pair(string source, string typed)
    {
        Assert.Null(MarkdownEditing.AutoPair(source, source.Length, 0, typed));
    }

    [Fact]
    public void Star_after_a_space_mid_line_inserts_the_pair()
    {
        var result = Type("это ", 4, "*");

        Assert.Equal("это **", result?.Text);
        Assert.Equal(5, result?.Caret);
    }

    [Theory]
    [InlineData(")")]
    [InlineData("]")]
    [InlineData("}")]
    [InlineData("$")]
    [InlineData("*")]
    [InlineData("\"")]
    public void Typing_the_closing_symbol_already_under_the_caret_skips_over_it(string typed)
    {
        var open = typed switch { ")" => "(", "]" => "[", "}" => "{", _ => typed };
        var source = open + "x" + typed;

        var result = Type(source, 2, typed);

        Assert.Equal(source, result?.Text);
        Assert.Equal(3, result?.Caret);
    }

    [Fact]
    public void Closing_bracket_without_a_match_under_the_caret_is_left_alone()
    {
        Assert.Null(MarkdownEditing.AutoPair("(x", 2, 0, ")"));
    }

    [Theory]
    [InlineData("$", "$$$$")]
    [InlineData("*", "****")]
    [InlineData("`", "````")]
    public void Second_symmetric_token_inside_an_empty_pair_doubles_it(string typed, string expected)
    {
        var source = typed + typed;

        var result = Type(source, 1, typed);

        Assert.Equal(expected, result?.Text);
        Assert.Equal(2, result?.Caret);
    }

    [Theory]
    [InlineData("(", "(слово)")]
    [InlineData("[", "[слово]")]
    [InlineData("{", "{слово}")]
    [InlineData("*", "*слово*")]
    [InlineData("_", "_слово_")]
    [InlineData("$", "$слово$")]
    [InlineData("`", "`слово`")]
    [InlineData("\"", "\"слово\"")]
    public void Opening_symbol_wraps_the_selection_and_keeps_it_selected(string typed, string expected)
    {
        var edit = MarkdownEditing.AutoPair("a слово b", 2, 5, typed);

        Assert.NotNull(edit);
        var (text, caret) = Apply("a слово b", edit.Value);
        Assert.Equal("a " + expected + " b", text);
        Assert.Equal(3, caret);
        Assert.Equal(5, edit.Value.SelectionLength);
    }

    [Fact]
    public void Closing_bracket_with_a_selection_is_left_alone()
    {
        Assert.Null(MarkdownEditing.AutoPair("слово", 0, 5, ")"));
    }

    [Theory]
    [InlineData("a")]
    [InlineData(" ")]
    [InlineData("ab")]
    [InlineData("")]
    [InlineData(null)]
    public void Ordinary_input_is_left_alone(string? typed)
    {
        Assert.Null(MarkdownEditing.AutoPair("x", 1, 0, typed));
    }

    [Fact]
    public void Auto_pair_tolerates_a_caret_beyond_the_text()
    {
        var result = Type("ab", 99, "(");

        Assert.Equal("ab()", result?.Text);
    }

    [Theory]
    [InlineData("()", 1)]
    [InlineData("$$", 1)]
    [InlineData("a{}b", 2)]
    public void Backspace_inside_an_empty_pair_removes_both(string source, int caret)
    {
        var edit = MarkdownEditing.DeletePair(source, caret);

        Assert.NotNull(edit);
        var (text, newCaret) = Apply(source, edit.Value);
        Assert.Equal(source.Remove(caret - 1, 2), text);
        Assert.Equal(caret - 1, newCaret);
    }

    [Theory]
    [InlineData("(x)", 1)]
    [InlineData("()", 0)]
    [InlineData("()", 2)]
    [InlineData("ab", 1)]
    [InlineData("", 0)]
    public void Backspace_elsewhere_is_left_alone(string source, int caret)
    {
        Assert.Null(MarkdownEditing.DeletePair(source, caret));
    }

    [Fact]
    public void Enter_between_double_dollars_moves_them_to_their_own_lines()
    {
        var edit = MarkdownEditing.SplitDisplayMath("$$$$", 2);

        Assert.NotNull(edit);
        var (text, caret) = Apply("$$$$", edit.Value);
        Assert.Equal("$$\n\n$$", text);
        Assert.Equal(3, caret);
    }

    [Theory]
    [InlineData("$$", 1)]
    [InlineData("$$x$$", 2)]
    [InlineData("$$$$", 1)]
    [InlineData("", 0)]
    public void Enter_elsewhere_does_not_split(string source, int caret)
    {
        Assert.Null(MarkdownEditing.SplitDisplayMath(source, caret));
    }
}
