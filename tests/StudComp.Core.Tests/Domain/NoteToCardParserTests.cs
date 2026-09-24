using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты разбора заметки на карточки (new_addons.md §7.3, Phase 12.9). Вход — уже готовая
/// блочная модель (как её строит <c>IMarkdownDocumentModelBuilder</c>), а не сырой Markdown: разбор в
/// <c>Core</c> не тянет Markdig.
/// </summary>
public sealed class NoteToCardParserTests
{
    private static ReportDocumentModel Model(params IReportBlock[] blocks) => new(null, blocks, false);

    private static ParagraphBlock Plain(string text) => new([new InlineRun(text)]);

    private static ParagraphBlock BoldThen(string bold, string rest) =>
        new([new InlineRun(bold, InlineStyle.Bold), new InlineRun(rest)]);

    [Fact]
    public void Bold_term_with_a_dash_becomes_a_term_card()
    {
        var model = Model(BoldThen("Интеграл", " — предел интегральных сумм."));

        var candidates = NoteToCardParser.Parse(model);

        var candidate = Assert.Single(candidates);
        Assert.Equal("Интеграл", candidate.Front);
        Assert.Equal("предел интегральных сумм.", candidate.Back);
        Assert.Equal(CardKind.Term, candidate.Kind);
        Assert.Equal(NoteCardPattern.BoldTerm, candidate.Pattern);
    }

    [Theory]
    [InlineData("—")]
    [InlineData("–")]
    [InlineData("-")]
    [InlineData(":")]
    public void Bold_term_accepts_any_of_the_usual_separators(string separator)
    {
        var model = Model(BoldThen("Термин", $"{separator} определение"));

        var candidate = Assert.Single(NoteToCardParser.Parse(model));
        Assert.Equal("определение", candidate.Back);
    }

    [Fact]
    public void Bold_text_without_a_separator_is_not_a_candidate()
    {
        // «Жирный текст просто так» — не обязательно термин с определением.
        var model = Model(BoldThen("Важно", " не забыть про область определения."));

        Assert.Empty(NoteToCardParser.Parse(model));
    }

    [Fact]
    public void Bold_run_alone_without_a_following_run_is_not_a_candidate()
    {
        var model = Model(new ParagraphBlock([new InlineRun("Одинокий жирный текст", InlineStyle.Bold)]));

        Assert.Empty(NoteToCardParser.Parse(model));
    }

    [Fact]
    public void Heading_followed_by_a_paragraph_becomes_a_card()
    {
        var model = Model(new HeadingBlock(2, "Теорема Стокса"), Plain("Связывает поток ротора и циркуляцию."));

        var candidate = Assert.Single(NoteToCardParser.Parse(model));
        Assert.Equal("Теорема Стокса", candidate.Front);
        Assert.Equal("Связывает поток ротора и циркуляцию.", candidate.Back);
        Assert.Equal(NoteCardPattern.HeadingAndParagraph, candidate.Pattern);
    }

    [Fact]
    public void A_heading_without_a_following_paragraph_is_not_a_candidate()
    {
        var model = Model(new HeadingBlock(2, "Заголовок без пары"));

        Assert.Empty(NoteToCardParser.Parse(model));
    }

    [Fact]
    public void Two_heading_paragraph_pairs_in_a_row_both_become_candidates()
    {
        // Курсор должен двигаться только вперёд: второй абзац не должен «прилипнуть» к первому заголовку.
        var model = Model(
            new HeadingBlock(2, "Термин А"),
            Plain("Определение А."),
            new HeadingBlock(2, "Термин Б"),
            Plain("Определение Б."));

        var candidates = NoteToCardParser.Parse(model);

        Assert.Equal(2, candidates.Count);
        Assert.Equal("Термин А", candidates[0].Front);
        Assert.Equal("Термин Б", candidates[1].Front);
    }

    [Fact]
    public void A_list_item_with_a_colon_becomes_a_term_card()
    {
        var model = Model(new ListBlock(false, [Model(Plain("Семафор: примитив синхронизации потоков"))]));

        var candidate = Assert.Single(NoteToCardParser.Parse(model));
        Assert.Equal("Семафор", candidate.Front);
        Assert.Equal("примитив синхронизации потоков", candidate.Back);
        Assert.Equal(NoteCardPattern.ListTerm, candidate.Pattern);
    }

    [Fact]
    public void A_list_item_without_a_colon_is_not_a_candidate()
    {
        var model = Model(new ListBlock(false, [Model(Plain("Просто пункт списка без определения"))]));

        Assert.Empty(NoteToCardParser.Parse(model));
    }

    [Fact]
    public void A_colon_deep_inside_a_sentence_does_not_count_as_a_term_definition()
    {
        // Многовато обычного текста перед двоеточием, да ещё и точка перед ним — это не «Термин: определение».
        var model = Model(new ListBlock(false, [Model(Plain(
            "Здесь идёт обычное предложение с точкой. И только потом: двоеточие"))]));

        Assert.Empty(NoteToCardParser.Parse(model));
    }

    [Fact]
    public void Multiple_list_items_each_produce_their_own_candidate()
    {
        var model = Model(new ListBlock(
            false,
            [
                Model(Plain("Термин1: определение1")),
                Model(Plain("Термин2: определение2")),
            ]));

        var candidates = NoteToCardParser.Parse(model);

        Assert.Equal(2, candidates.Count);
    }

    [Fact]
    public void A_question_paragraph_followed_by_an_answer_becomes_a_question_card()
    {
        var model = Model(Plain("Что такое семафор?"), Plain("Примитив синхронизации потоков."));

        var candidate = Assert.Single(NoteToCardParser.Parse(model));
        Assert.Equal("Что такое семафор?", candidate.Front);
        Assert.Equal("Примитив синхронизации потоков.", candidate.Back);
        Assert.Equal(CardKind.Question, candidate.Kind);
        Assert.Equal(NoteCardPattern.Question, candidate.Pattern);
    }

    [Fact]
    public void A_question_without_a_following_paragraph_is_not_a_candidate()
    {
        var model = Model(Plain("Вопрос без ответа?"));

        Assert.Empty(NoteToCardParser.Parse(model));
    }

    [Fact]
    public void A_plain_paragraph_that_does_not_end_with_a_question_mark_is_not_a_question()
    {
        var model = Model(Plain("Обычный абзац."), Plain("И ещё один."));

        Assert.Empty(NoteToCardParser.Parse(model));
    }

    [Fact]
    public void Unrecognized_blocks_are_skipped_without_throwing()
    {
        var model = Model(
            new TableBlock(["A", "B"], [["1", "2"]]),
            new CodeBlock("csharp", "var x = 1;"),
            new ImageBlock("pic.png", null));

        var candidates = NoteToCardParser.Parse(model);

        Assert.Empty(candidates);
    }

    [Fact]
    public void An_empty_document_produces_no_candidates()
    {
        Assert.Empty(NoteToCardParser.Parse(Model()));
    }
}
