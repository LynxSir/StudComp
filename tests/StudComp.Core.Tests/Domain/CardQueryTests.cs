using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Разбор строки поиска картотеки (new_addons.md §4.4). Главный инвариант, ради которого этот класс
/// вообще вынесен в <c>Core</c>: <b>ни одна пользовательская строка не приводит к исключению</b> —
/// человек печатает в поле поиска что угодно, включая спецсимволы FTS5.
/// </summary>
public sealed class CardQueryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Empty_input_gives_empty_spec(string? input)
    {
        var spec = CardQuery.Parse(input);

        Assert.True(spec.IsEmpty);
        Assert.False(spec.HasFullTextPart);
        Assert.Empty(spec.Terms);
    }

    [Fact]
    public void Words_become_prefix_terms_in_order()
    {
        var spec = CardQuery.Parse("интеграл частям");

        Assert.Equal(2, spec.Terms.Count);
        Assert.Equal(new CardQueryTerm("интеграл", CardQueryTermKind.Prefix), spec.Terms[0]);
        Assert.Equal(new CardQueryTerm("частям", CardQueryTermKind.Prefix), spec.Terms[1]);
        Assert.True(spec.HasFullTextPart);
        Assert.False(spec.IsEmpty);
    }

    [Fact]
    public void Quoted_text_becomes_a_phrase()
    {
        var spec = CardQuery.Parse("\"по частям\"");

        var term = Assert.Single(spec.Terms);
        Assert.Equal(new CardQueryTerm("по частям", CardQueryTermKind.Phrase), term);
    }

    [Fact]
    public void Phrase_keeps_inner_spaces_but_collapses_runs()
    {
        var spec = CardQuery.Parse("\"по   частям\"");

        Assert.Equal("по частям", Assert.Single(spec.Terms).Text);
    }

    [Fact]
    public void Minus_prefix_excludes_a_word()
    {
        var spec = CardQuery.Parse("интеграл -лейбниц");

        Assert.Equal(CardQueryTermKind.Prefix, spec.Terms[0].Kind);
        Assert.Equal(new CardQueryTerm("лейбниц", CardQueryTermKind.Exclude), spec.Terms[1]);
    }

    [Fact]
    public void Minus_before_a_phrase_excludes_the_phrase()
    {
        var spec = CardQuery.Parse("-\"по частям\"");

        Assert.Equal(new CardQueryTerm("по частям", CardQueryTermKind.Exclude), Assert.Single(spec.Terms));
    }

    [Theory]
    [InlineData("#формулы", "формулы")]
    [InlineData("#Формулы", "формулы")]
    [InlineData("#ФОРМУЛЫ", "формулы")]
    [InlineData("метка:формулы", "формулы")]
    public void Tags_are_normalized_to_lower_case(string input, string expected)
    {
        var spec = CardQuery.Parse(input);

        Assert.Equal(expected, Assert.Single(spec.Tags));
        Assert.Empty(spec.Terms);
    }

    [Fact]
    public void Tag_with_yo_is_folded_to_ye_like_the_index_does()
    {
        var spec = CardQuery.Parse("#ёмкость");

        Assert.Equal("емкость", Assert.Single(spec.Tags));
    }

    [Fact]
    public void Minus_before_a_tag_excludes_it()
    {
        var spec = CardQuery.Parse("-#формулы");

        Assert.Empty(spec.Tags);
        Assert.Equal("формулы", Assert.Single(spec.ExcludedTags));
    }

    [Fact]
    public void Repeated_tags_are_collapsed()
    {
        var spec = CardQuery.Parse("#формулы #Формулы #формулы");

        Assert.Equal("формулы", Assert.Single(spec.Tags));
    }

    [Theory]
    [InlineData("@матан", "матан")]
    [InlineData("@МатАн", "МатАн")]
    [InlineData("предмет:матан", "матан")]
    public void Subject_token_is_kept_verbatim_for_the_data_layer(string input, string expected)
    {
        // Регистр не трогаем: предмет резолвится по Name/Code уже в StudComp.Data.
        Assert.Equal(expected, CardQuery.Parse(input).SubjectToken);
    }

    [Fact]
    public void Deck_token_supports_quoted_values()
    {
        var spec = CardQuery.Parse("колода:\"к экзамену\"");

        Assert.Equal("к экзамену", spec.DeckToken);
        Assert.Empty(spec.Terms);
    }

    [Theory]
    [InlineData("тип:термин", CardKind.Term)]
    [InlineData("тип:вопросы", CardKind.Question)]
    [InlineData("тип:формула", CardKind.Formula)]
    [InlineData("тип:КОД", CardKind.Code)]
    [InlineData("тип:факт", CardKind.Fact)]
    public void Kind_filter_is_parsed(string input, CardKind expected)
    {
        Assert.Equal(expected, CardQuery.Parse(input).Kind);
    }

    [Theory]
    [InlineData("сложность:лёгкая", CardDifficulty.Easy)]
    [InlineData("сложность:легкая", CardDifficulty.Easy)]
    [InlineData("сложность:обычная", CardDifficulty.Normal)]
    [InlineData("сложность:трудная", CardDifficulty.Hard)]
    public void Difficulty_filter_is_parsed(string input, CardDifficulty expected)
    {
        Assert.Equal(expected, CardQuery.Parse(input).Difficulty);
    }

    [Theory]
    [InlineData("есть:подсказка", CardQueryFlags.HasHint)]
    [InlineData("нет:метки", CardQueryFlags.NoTags)]
    [InlineData("нет:предмета", CardQueryFlags.NoSubject)]
    [InlineData("есть:колода", CardQueryFlags.HasDeck)]
    [InlineData("есть:источник", CardQueryFlags.HasSource)]
    [InlineData("трудные", CardQueryFlags.Hard)]
    [InlineData("новые", CardQueryFlags.New)]
    [InlineData("сегодня", CardQueryFlags.DueToday)]
    [InlineData("закреплённые", CardQueryFlags.Pinned)]
    [InlineData("закрепленные", CardQueryFlags.Pinned)]
    [InlineData("отложенные", CardQueryFlags.Suspended)]
    [InlineData("корзина", CardQueryFlags.Trash)]
    public void Flags_are_parsed(string input, CardQueryFlags expected)
    {
        var spec = CardQuery.Parse(input);

        Assert.Equal(expected, spec.Flags);
        Assert.Empty(spec.Terms);
    }

    [Fact]
    public void Flags_accumulate()
    {
        var spec = CardQuery.Parse("трудные новые нет:метки");

        Assert.Equal(CardQueryFlags.Hard | CardQueryFlags.New | CardQueryFlags.NoTags, spec.Flags);
    }

    [Fact]
    public void Everything_combines_in_one_query()
    {
        var spec = CardQuery.Parse("@матан #формулы колода:экзамен тип:формула трудные \"по частям\" -ряд");

        Assert.Equal("матан", spec.SubjectToken);
        Assert.Equal("формулы", Assert.Single(spec.Tags));
        Assert.Equal("экзамен", spec.DeckToken);
        Assert.Equal(CardKind.Formula, spec.Kind);
        Assert.Equal(CardQueryFlags.Hard, spec.Flags);
        Assert.Equal(2, spec.Terms.Count);
        Assert.Equal(new CardQueryTerm("по частям", CardQueryTermKind.Phrase), spec.Terms[0]);
        Assert.Equal(new CardQueryTerm("ряд", CardQueryTermKind.Exclude), spec.Terms[1]);
    }

    [Fact]
    public void Unknown_operator_falls_back_to_full_text()
    {
        var spec = CardQuery.Parse("автор:пушкин");

        Assert.Null(spec.DeckToken);
        Assert.Equal("автор:пушкин", Assert.Single(spec.Terms).Text);
    }

    [Fact]
    public void Known_operator_with_nonsense_value_falls_back_to_full_text()
    {
        var spec = CardQuery.Parse("тип:енот");

        Assert.Null(spec.Kind);
        Assert.Equal("тип:енот", Assert.Single(spec.Terms).Text);
    }

    [Fact]
    public void Preset_word_after_minus_is_a_plain_exclusion_not_a_flag()
    {
        var spec = CardQuery.Parse("-новые");

        Assert.Equal(CardQueryFlags.None, spec.Flags);
        Assert.Equal(new CardQueryTerm("новые", CardQueryTermKind.Exclude), Assert.Single(spec.Terms));
    }

    [Fact]
    public void Unterminated_quote_swallows_the_rest_without_throwing()
    {
        var spec = CardQuery.Parse("\"по частям");

        Assert.Equal(new CardQueryTerm("по частям", CardQueryTermKind.Phrase), Assert.Single(spec.Terms));
    }

    [Theory]
    [InlineData("-")]
    [InlineData("---")]
    [InlineData("\"\"")]
    [InlineData("***")]
    [InlineData("()")]
    [InlineData("^")]
    [InlineData(":")]
    [InlineData("#")]
    [InlineData("@")]
    public void Punctuation_only_input_produces_nothing_to_search(string input)
    {
        var spec = CardQuery.Parse(input);

        Assert.Empty(spec.Terms);
        Assert.Empty(spec.Tags);
        Assert.True(spec.IsEmpty);
    }

    [Theory]
    [InlineData("\"")]
    [InlineData("\"\"\"")]
    [InlineData("a\"b\"c")]
    [InlineData("*")]
    [InlineData("интеграл*")]
    [InlineData("NEAR(a b)")]
    [InlineData("a AND b OR NOT c")]
    [InlineData("';DROP TABLE Cards;--")]
    [InlineData("#")]
    [InlineData("##")]
    [InlineData("@@@")]
    [InlineData("колода:")]
    [InlineData(":значение")]
    [InlineData("тип::")]
    [InlineData("-\"")]
    [InlineData("😀 карточка")]
    [InlineData("   \t  интеграл   \t ")]
    public void Any_user_input_parses_without_throwing(string input)
    {
        var spec = CardQuery.Parse(input);

        Assert.NotNull(spec);
        Assert.Equal(input, spec.Raw);
        Assert.All(spec.Terms, term => Assert.NotEmpty(term.Text));
        Assert.All(spec.Tags, tag => Assert.NotEmpty(tag));
    }

    [Fact]
    public void Raw_string_is_preserved_for_the_empty_state_prompt()
    {
        const string query = "теорема Стокса";

        Assert.Equal(query, CardQuery.Parse(query).Raw);
    }

    [Fact]
    public void Quotes_inside_a_phrase_are_unescaped()
    {
        // Внутренние кавычки пользователь удваивает — как в самом FTS5.
        var spec = CardQuery.Parse("\"кавычка \"\" внутри\"");

        Assert.Equal(CardQueryTermKind.Phrase, Assert.Single(spec.Terms).Kind);
        Assert.Contains("кавычка", spec.Terms[0].Text);
    }
}
