using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Табличные тесты разбора CSV/TSV (new_addons.md §7.6, Phase 12.9). Разбор работает с уже
/// декодированной строкой — кодировка и файловый ввод-вывод здесь не участвуют.
/// </summary>
public sealed class CardCsvParserTests
{
    [Fact]
    public void A_simple_row_is_split_by_the_delimiter()
    {
        var result = CardCsvParser.Parse("Интеграл;Сумма бесконечно малых;матан", ';', hasHeader: false);

        var row = Assert.Single(result.Rows);
        Assert.Equal(["Интеграл", "Сумма бесконечно малых", "матан"], row.Fields);
        Assert.False(row.HasUnterminatedQuote);
    }

    [Fact]
    public void Tab_can_be_used_as_the_delimiter()
    {
        var result = CardCsvParser.Parse("Front\tBack\tTags", '\t', hasHeader: false);

        Assert.Equal(["Front", "Back", "Tags"], Assert.Single(result.Rows).Fields);
    }

    [Fact]
    public void A_quoted_field_may_contain_the_delimiter()
    {
        var result = CardCsvParser.Parse("\"А; Б; В\";определение", ';', hasHeader: false);

        Assert.Equal(["А; Б; В", "определение"], Assert.Single(result.Rows).Fields);
    }

    [Fact]
    public void A_doubled_quote_inside_a_quoted_field_is_an_escaped_quote()
    {
        var result = CardCsvParser.Parse("\"Она сказала \"\"привет\"\"\";ответ", ';', hasHeader: false);

        Assert.Equal(["Она сказала \"привет\"", "ответ"], Assert.Single(result.Rows).Fields);
    }

    [Fact]
    public void A_newline_inside_a_quoted_field_does_not_split_the_row()
    {
        var result = CardCsvParser.Parse("\"строка1\nстрока2\";определение", ';', hasHeader: false);

        var row = Assert.Single(result.Rows);
        Assert.Equal("строка1\nстрока2", row.Fields[0]);
        Assert.False(row.HasUnterminatedQuote);
    }

    [Fact]
    public void Crlf_and_lf_both_separate_rows()
    {
        var result = CardCsvParser.Parse("а;1\r\nб;2\nв;3", ';', hasHeader: false);

        Assert.Equal(3, result.Rows.Count);
        Assert.Equal("а", result.Rows[0].Fields[0]);
        Assert.Equal("б", result.Rows[1].Fields[0]);
        Assert.Equal("в", result.Rows[2].Fields[0]);
    }

    [Fact]
    public void An_unterminated_quote_swallows_the_rest_of_the_file_and_is_flagged()
    {
        var result = CardCsvParser.Parse("а;1\n\"не закрыта;и это уже не разделитель\nи это тоже часть поля", ';', hasHeader: false);

        Assert.Equal(2, result.Rows.Count);
        Assert.False(result.Rows[0].HasUnterminatedQuote);
        Assert.True(result.Rows[^1].HasUnterminatedQuote);
    }

    [Fact]
    public void Empty_lines_are_skipped_rather_than_producing_a_blank_row()
    {
        var result = CardCsvParser.Parse("а;1\n\n\nб;2", ';', hasHeader: false);

        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public void Trailing_row_without_a_final_newline_is_still_captured()
    {
        var result = CardCsvParser.Parse("а;1\nб;2", ';', hasHeader: false);

        Assert.Equal(2, result.Rows.Count);
        Assert.Equal("б", result.Rows[^1].Fields[0]);
    }

    [Fact]
    public void Header_is_split_off_when_requested()
    {
        var result = CardCsvParser.Parse("Front;Back;Tags;Deck\nИнтеграл;Определение;;", ';', hasHeader: true);

        Assert.NotNull(result.Header);
        Assert.Equal(["Front", "Back", "Tags", "Deck"], result.Header);
        Assert.Single(result.Rows);
        Assert.Equal("Интеграл", result.Rows[0].Fields[0]);
    }

    [Fact]
    public void Without_a_header_all_rows_are_data()
    {
        var result = CardCsvParser.Parse("а;1\nб;2", ';', hasHeader: false);

        Assert.Null(result.Header);
        Assert.Equal(2, result.Rows.Count);
    }

    [Fact]
    public void Empty_input_produces_no_rows_and_no_header()
    {
        var result = CardCsvParser.Parse(string.Empty, ';', hasHeader: true);

        Assert.Null(result.Header);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void Line_numbers_track_the_physical_start_of_each_row()
    {
        var result = CardCsvParser.Parse("а;1\nб;2\n\"через\nдве строки\";3", ';', hasHeader: false);

        Assert.Equal(1, result.Rows[0].LineNumber);
        Assert.Equal(2, result.Rows[1].LineNumber);
        Assert.Equal(3, result.Rows[2].LineNumber);
    }

    [Fact]
    public void A_quote_that_does_not_start_a_field_is_treated_literally()
    {
        var result = CardCsvParser.Parse("метр(\")вес;определение", ';', hasHeader: false);

        Assert.Equal("метр(\")вес", result.Rows[0].Fields[0]);
    }

    [Fact]
    public void Garbage_input_never_throws()
    {
        string[] garbage =
        [
            "\"\"\"\"\"\"", ";;;;;;", "\"", new string('"', 500), "\r\n\r\n\r\n", "а;\"б\nв\";г",
        ];

        foreach (var value in garbage)
        {
            var result = CardCsvParser.Parse(value, ';', hasHeader: false);
            Assert.NotNull(result.Rows);
        }
    }
}
