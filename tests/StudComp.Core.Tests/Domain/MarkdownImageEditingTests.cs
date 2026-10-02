using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>Картинка как цельный объект: навигация, двухшаговое удаление и мутации.</summary>
public sealed class MarkdownImageEditingTests
{
    private const string Inline = "ab![i](i.png)cd";
    private const string OwnLine = "one\n![a](a.png)\ntwo";

    [Fact]
    public void Pasted_image_replaces_selection_in_one_undoable_edit()
    {
        const string text = "до заменить после";
        var edit = MarkdownImageEditing.Insert(text, 3, "Предмет/Рисунки/снимок.png", selectionLength: 8);
        var result = MarkdownEditing.Apply(text, edit);
        Assert.Equal(8, edit.Length);
        Assert.DoesNotContain("заменить", result);
        Assert.StartsWith("до \n![Рисунок]", result);
        Assert.EndsWith("\n после", result);
        var token = Assert.Single(MarkdownLocalImages.Tokens(result));
        Assert.Equal("Предмет/Рисунки/снимок.png", token.Path);
        Assert.Equal(token.End, edit.Start + edit.CaretOffset);
    }

    [Fact]
    public void Pasting_over_entire_note_leaves_only_the_image_link()
    {
        var edit = MarkdownImageEditing.Insert("старый текст", 0, "a.png", selectionLength: int.MaxValue);
        Assert.Equal("![Рисунок](<a.png>)", MarkdownEditing.Apply("старый текст", edit));
    }

    private static MarkdownImageToken Token(string source, int index = 0) =>
        MarkdownLocalImages.Tokens(source)[index];

    // --- навигация -------------------------------------------------------

    [Fact]
    public void An_arrow_steps_over_the_token_from_either_side()
    {
        var token = Token(Inline);

        Assert.Equal(token.End, MarkdownImageEditing.StepOverToken(Inline, token.Start, forward: true));
        Assert.Equal(token.Start, MarkdownImageEditing.StepOverToken(Inline, token.End, forward: false));
    }

    [Fact]
    public void An_arrow_elsewhere_is_left_alone()
    {
        Assert.Null(MarkdownImageEditing.StepOverToken(Inline, 1, forward: true));
        Assert.Null(MarkdownImageEditing.StepOverToken(Inline, 14, forward: false));
    }

    [Fact]
    public void One_press_crosses_exactly_one_of_two_adjacent_tokens()
    {
        const string source = "![a](a.png)![b](b.png)";
        var first = Token(source);
        var second = Token(source, 1);

        Assert.Equal(first.End, MarkdownImageEditing.StepOverToken(source, first.Start, forward: true));
        Assert.Equal(second.End, MarkdownImageEditing.StepOverToken(source, second.Start, forward: true));
    }

    [Fact]
    public void A_caret_inside_the_token_snaps_along_the_direction_of_travel()
    {
        var token = Token(Inline);
        var middle = token.Start + 4;

        Assert.Equal((token.End, 0), MarkdownImageEditing.Snap(Inline, middle, 0, token.Start));
        Assert.Equal((token.Start, 0), MarkdownImageEditing.Snap(Inline, middle, 0, token.End));
    }

    [Fact]
    public void An_unknown_direction_snaps_to_the_nearer_boundary()
    {
        var token = Token(Inline);

        Assert.Equal((token.Start, 0), MarkdownImageEditing.Snap(Inline, token.Start + 1, 0, -1));
        Assert.Equal((token.End, 0), MarkdownImageEditing.Snap(Inline, token.End - 1, 0, -1));
    }

    [Fact]
    public void Snap_returns_null_exactly_on_the_boundaries()
    {
        // Иначе SelectionChanged войдёт в осцилляцию: наш Select снова поднимет это же событие.
        var token = Token(Inline);

        Assert.Null(MarkdownImageEditing.Snap(Inline, token.Start, 0, 0));
        Assert.Null(MarkdownImageEditing.Snap(Inline, token.End, 0, 0));
        Assert.Null(MarkdownImageEditing.Snap(Inline, token.Start, token.Length, 0));
        Assert.Null(MarkdownImageEditing.Snap(Inline, 0, Inline.Length, 0));
    }

    [Fact]
    public void A_selection_overlapping_a_token_expands_to_whole_tokens()
    {
        var token = Token(Inline);

        Assert.Equal((token.Start, token.Length), MarkdownImageEditing.Snap(Inline, token.Start + 3, 3, 0));
        // Выделение, кончающееся внутри картинки, дотягивается до её конца.
        Assert.Equal((0, token.End), MarkdownImageEditing.Snap(Inline, 0, token.Start + 3, 0));
    }

    [Fact]
    public void A_selection_across_two_half_tokens_expands_to_both()
    {
        const string source = "![a](a.png) x ![b](b.png)";
        var snapped = MarkdownImageEditing.Snap(source, 5, source.Length - 10, 0);

        Assert.Equal((0, source.Length), snapped);
    }

    [Fact]
    public void Snap_without_tokens_changes_nothing() =>
        Assert.Null(MarkdownImageEditing.Snap("plain text", 3, 2, 0));

    // --- двухшаговое удаление --------------------------------------------

    [Fact]
    public void The_first_backspace_only_selects_the_token()
    {
        var token = Token(Inline);
        var erase = MarkdownImageEditing.Eraser(Inline, token.End, 0, forward: false);

        Assert.Equal(ImageEraseStep.Select, erase!.Value.Step);
        Assert.Equal(token, erase.Value.Token);
    }

    [Fact]
    public void The_first_delete_key_only_selects_the_token()
    {
        var token = Token(Inline);
        var erase = MarkdownImageEditing.Eraser(Inline, token.Start, 0, forward: true);

        Assert.Equal(ImageEraseStep.Select, erase!.Value.Step);
    }

    [Fact]
    public void The_second_press_removes_the_whole_token()
    {
        var token = Token(Inline);
        var erase = MarkdownImageEditing.Eraser(Inline, token.Start, token.Length, forward: false);

        Assert.Equal(ImageEraseStep.Delete, erase!.Value.Step);
        Assert.Equal("abcd", MarkdownEditing.Apply(Inline, erase.Value.Edit));
    }

    [Fact]
    public void A_selection_one_character_wider_than_the_token_is_left_alone()
    {
        var token = Token(Inline);

        Assert.Null(MarkdownImageEditing.Eraser(Inline, token.Start, token.Length + 1, forward: false));
        Assert.Null(MarkdownImageEditing.Eraser(Inline, token.Start, token.Length - 1, forward: false));
    }

    [Fact]
    public void Backspace_away_from_a_token_is_left_alone() =>
        Assert.Null(MarkdownImageEditing.Eraser(Inline, 1, 0, forward: false));

    [Fact]
    public void Deleting_a_token_that_owns_its_line_takes_the_line_break_with_it()
    {
        var edit = MarkdownImageEditing.Delete(OwnLine, Token(OwnLine));

        Assert.Equal("one\ntwo", MarkdownEditing.Apply(OwnLine, edit));
    }

    [Fact]
    public void Deleting_an_inline_token_keeps_the_surrounding_text()
    {
        var edit = MarkdownImageEditing.Delete(Inline, Token(Inline));

        Assert.Equal("abcd", MarkdownEditing.Apply(Inline, edit));
    }

    [Fact]
    public void Deleting_the_last_token_takes_the_leading_line_break()
    {
        const string source = "one\n![a](a.png)";
        var edit = MarkdownImageEditing.Delete(source, Token(source));

        Assert.Equal("one", MarkdownEditing.Apply(source, edit));
    }

    // --- мутации ---------------------------------------------------------

    [Fact]
    public void Insert_into_an_empty_note_adds_no_line_breaks()
    {
        var edit = MarkdownImageEditing.Insert(string.Empty, 0, "a.png");

        Assert.Equal("![Рисунок](<a.png>)", MarkdownEditing.Apply(string.Empty, edit));
        Assert.Equal(edit.Replacement.Length, edit.CaretOffset);
    }

    [Fact]
    public void Insert_mid_paragraph_puts_the_image_on_its_own_line()
    {
        var edit = MarkdownImageEditing.Insert("abcdef", 3, "a.png");

        Assert.Equal("abc\n![Рисунок](<a.png>)\ndef", MarkdownEditing.Apply("abcdef", edit));
    }

    [Fact]
    public void Insert_on_an_empty_line_adds_nothing_extra()
    {
        var edit = MarkdownImageEditing.Insert("abc\n", 4, "a.png");

        Assert.Equal("abc\n![Рисунок](<a.png>)", MarkdownEditing.Apply("abc\n", edit));
    }

    [Fact]
    public void Insert_encodes_spaces_and_parentheses_in_the_path()
    {
        const string path = "Математика/Рисунки/Фото #1 (2).png";
        var edit = MarkdownImageEditing.Insert(string.Empty, 0, path);
        var text = MarkdownEditing.Apply(string.Empty, edit);

        Assert.Equal(path, Assert.Single(MarkdownLocalImages.Paths(text)));
    }

    [Theory]
    [InlineData(0, 32)]
    [InlineData(-5, 32)]
    [InlineData(31, 32)]
    [InlineData(2401, 2400)]
    [InlineData(int.MaxValue, 2400)]
    [InlineData(480, 480)]
    public void Set_width_clamps_instead_of_throwing(int requested, int expected)
    {
        const string source = "![a](a.png)";
        var edit = MarkdownImageEditing.SetWidth(source, Token(source), requested);

        Assert.Equal($"![a](a.png){{width={expected}}}", MarkdownEditing.Apply(source, edit!.Value));
    }

    [Fact]
    public void Set_width_to_null_removes_it_and_keeps_other_attributes()
    {
        const string source = "![a](a.png){width=200 #photo}";
        var edit = MarkdownImageEditing.SetWidth(source, Token(source), null);
        var result = MarkdownEditing.Apply(source, edit!.Value);

        Assert.DoesNotContain("width=", result);
        Assert.Contains("#photo", result);
    }

    [Fact]
    public void Set_width_keeps_other_attributes_in_place()
    {
        const string source = "![a](a.png){width=200 #photo}";
        var result = MarkdownEditing.Apply(source, MarkdownImageEditing.SetWidth(source, Token(source), 480)!.Value);

        Assert.Contains("width=480", result);
        Assert.Contains("#photo", result);
    }

    [Fact]
    public void Set_width_on_the_second_of_two_identical_images_touches_only_it()
    {
        const string link = "![a](a.png)";
        var source = link + "\n" + link;
        var result = MarkdownEditing.Apply(source, MarkdownImageEditing.SetWidth(source, Token(source, 1), 480)!.Value);

        Assert.Equal(link + "\n" + link + "{width=480}", result);
    }

    [Fact]
    public void Set_width_to_the_same_value_changes_nothing()
    {
        const string source = "![a](a.png){width=480}";

        Assert.Null(MarkdownImageEditing.SetWidth(source, Token(source), 480));
    }

    [Fact]
    public void Moving_a_token_forward_produces_one_contiguous_edit()
    {
        const string source = "one\n![a](a.png)\ntwo\nthree";
        var edit = MarkdownImageEditing.MoveToken(source, Token(source), source.Length);

        Assert.Equal("one\ntwo\nthree\n![a](a.png)", MarkdownEditing.Apply(source, edit!.Value));
    }

    [Fact]
    public void Moving_a_token_backward_produces_one_contiguous_edit()
    {
        const string source = "one\ntwo\n![a](a.png)\nthree";
        var edit = MarkdownImageEditing.MoveToken(source, Token(source), 0);

        Assert.Equal("![a](a.png)\none\ntwo\nthree", MarkdownEditing.Apply(source, edit!.Value));
    }

    [Fact]
    public void Dropping_a_token_onto_itself_is_a_no_op()
    {
        var token = Token(OwnLine);

        Assert.Null(MarkdownImageEditing.MoveToken(OwnLine, token, token.Start));
        Assert.Null(MarkdownImageEditing.MoveToken(OwnLine, token, token.Start + 4));
        Assert.Null(MarkdownImageEditing.MoveToken(OwnLine, token, token.End));
    }

    [Fact]
    public void Dropping_a_token_inside_another_one_lands_on_its_boundary()
    {
        const string source = "![a](a.png)\nx\n![b](b.png)";
        var second = Token(source, 1);
        var edit = MarkdownImageEditing.MoveToken(source, Token(source), second.Start + 4);
        var result = MarkdownEditing.Apply(source, edit!.Value);

        // Сброс пришёлся внутрь второй картинки и прилип к её началу; ссылки целы.
        Assert.Equal("x\n![a](a.png)\n![b](b.png)", result);
        Assert.Equal(2, MarkdownLocalImages.Tokens(result).Count);
    }

    [Fact]
    public void Resolve_matches_the_block_from_the_parser()
    {
        const string source = "text\n![a](a.png){width=200}\n";
        var token = Token(source);

        Assert.Equal(token, MarkdownImageEditing.Resolve(source, token.Start, token.LinkLength, "a.png"));
    }

    [Fact]
    public void Resolve_falls_back_to_a_unique_path_when_the_offset_moved()
    {
        const string source = "more text\n![a](a.png)\n";
        var token = Token(source);

        Assert.Equal(token, MarkdownImageEditing.Resolve(source, 0, token.LinkLength, "a.png"));
    }

    [Fact]
    public void Resolve_refuses_an_ambiguous_match()
    {
        const string source = "![a](a.png)\n![a](a.png)";

        Assert.Null(MarkdownImageEditing.Resolve(source, 99, 11, "a.png"));
    }

    [Fact]
    public void Resolve_returns_null_when_the_image_is_gone() =>
        Assert.Null(MarkdownImageEditing.Resolve("plain text", 0, 11, "a.png"));

    [Fact]
    public void Paths_are_compared_ignoring_case_and_separators()
    {
        Assert.True(MarkdownImageEditing.SamePath("Матан/Рисунки/A.PNG", "матан\\рисунки\\a.png"));
        Assert.False(MarkdownImageEditing.SamePath("a.png", "b.png"));
    }

    [Theory]
    [InlineData("фото [1]", "фото 1")]
    [InlineData("  обрезать  ", "обрезать")]
    [InlineData("", "Рисунок")]
    [InlineData(null, "Рисунок")]
    public void Insert_sanitises_the_caption(string? caption, string expected)
    {
        // Подпись идёт в квадратных скобках: имя файла «фото [1].png» иначе развалило бы ссылку,
        // и Markdig перестал бы видеть в ней картинку вовсе.
        var edit = MarkdownImageEditing.Insert(string.Empty, 0, "a.png", caption!);
        var text = MarkdownEditing.Apply(string.Empty, edit);

        Assert.Equal($"![{expected}](<a.png>)", text);
        Assert.Equal("a.png", Assert.Single(MarkdownLocalImages.Paths(text)));
    }
}
