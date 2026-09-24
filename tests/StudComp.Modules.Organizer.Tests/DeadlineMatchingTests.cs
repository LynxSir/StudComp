using StudComp.Modules.Organizer.Services;

namespace StudComp.Modules.Organizer.Tests;

/// <summary>
/// Похожесть «имя файла ↔ дедлайн» (ARCHITECTURE §9.3). Чистая функция, поэтому тесты табличные и
/// без БД — тот же подход, что у <c>WeekParityCalculatorTests</c>.
/// </summary>
public sealed class DeadlineMatchingTests
{
    private static readonly DateTimeOffset Sorted = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);
    private const int Window = 21;

    private static double Score(string fileName, string title, int dueOffsetDays = 2) =>
        DeadlineMatching.Score(fileName, title, Sorted.AddDays(dueOffsetDays), Sorted, Window);

    [Fact]
    public void Matching_work_number_and_subject_scores_high()
    {
        // Канонический пример из §9.3: «Похоже, это ЛР4 по Матану».
        Assert.True(Score("ЛР4_Матан.docx", "ЛР4 по матанализу") >= 0.45);
    }

    [Fact]
    public void Unrelated_file_scores_low()
    {
        Assert.True(Score("отпускные_фото.zip", "ЛР4 по матанализу") < 0.45);
    }

    [Fact]
    public void Different_work_number_vetoes_the_match_entirely()
    {
        // «ЛР4» и «ЛР5» — разные работы, как бы ни совпадал остальной текст. Без жёсткого вето
        // одного лишь текстового сходства («лр» + «матан») хватало бы, чтобы перевалить порог.
        Assert.True(Score("ЛР4_Матан.docx", "ЛР4 Матан") >= 0.45);
        Assert.Equal(0, Score("ЛР5_Матан.docx", "ЛР4 Матан"));
    }

    [Fact]
    public void Number_on_one_side_only_is_not_a_veto()
    {
        // «Отчёт 2026.docx» против «Отчёт по практике» — год в имени не повод отказывать.
        Assert.True(Score("Отчёт 2026.docx", "Отчёт по практике") > 0);
    }

    [Fact]
    public void Any_overlapping_number_is_enough()
    {
        Assert.True(Score("ЛР4_Матан.docx", "ЛР4 и ЛР5 по матану") > 0);
    }

    [Fact]
    public void Matching_number_raises_the_score()
    {
        var withNumber = Score("ЛР4 матан.docx", "ЛР4 матан");
        var withoutNumber = Score("ЛР матан.docx", "ЛР4 матан");

        Assert.True(withNumber > withoutNumber);
    }

    [Fact]
    public void Closer_due_date_raises_the_score()
    {
        var near = Score("ЛР4_Матан.docx", "ЛР4 Матан", dueOffsetDays: 0);
        var far = Score("ЛР4_Матан.docx", "ЛР4 Матан", dueOffsetDays: 20);

        Assert.True(near > far);
    }

    [Fact]
    public void Date_outside_the_window_adds_nothing()
    {
        var inside = Score("ЛР4_Матан.docx", "ЛР4 Матан", dueOffsetDays: 20);
        var outside = Score("ЛР4_Матан.docx", "ЛР4 Матан", dueOffsetDays: 40);

        Assert.True(inside > outside);
        // Знак разницы не важен: работу сдают и заранее, и в последний момент.
        Assert.Equal(outside, Score("ЛР4_Матан.docx", "ЛР4 Матан", dueOffsetDays: -40), 6);
    }

    [Theory]
    [InlineData("", "ЛР4")]
    [InlineData("ЛР4.docx", "")]
    [InlineData("   ", "ЛР4")]
    [InlineData("...docx", "ЛР4")]
    public void Empty_or_meaningless_input_scores_zero(string fileName, string title)
    {
        Assert.Equal(0, Score(fileName, title));
    }

    [Theory]
    [InlineData("ЛР4_Матан.docx", "ЛР4 по матанализу")]
    [InlineData("совершенно_другое.zip", "Экзамен по физике")]
    [InlineData("ЛР4.docx", "ЛР4")]
    [InlineData("ЛР5_Матан.docx", "ЛР4 Матан")]
    public void Score_always_stays_within_the_unit_range(string fileName, string title)
    {
        var score = Score(fileName, title);

        Assert.InRange(score, 0, 1);
    }

    [Fact]
    public void Extension_does_not_participate_in_matching()
    {
        // Иначе «.docx» в имени файла тянуло бы счёт вверх на любом дедлайне со словом «docx».
        Assert.Equal(Score("ЛР4_Матан.docx", "ЛР4 Матан"), Score("ЛР4_Матан.pdf", "ЛР4 Матан"), 6);
    }

    [Fact]
    public void Case_and_separators_do_not_matter()
    {
        Assert.Equal(Score("ЛР4_Матан.docx", "ЛР4 Матан"), Score("лр4-матан.docx", "ЛР4  Матан"), 6);
    }

    [Fact]
    public void Yo_is_folded_to_ye()
    {
        // «Зачёт» и «Зачет» в именах файлов встречаются одинаково часто.
        Assert.Equal(Score("зачёт_матан.docx", "Зачет матан"), Score("зачет_матан.docx", "Зачет матан"), 6);
    }

    [Theory]
    [InlineData("ЛР4", new[] { "лр", "4" })]
    [InlineData("ЛР04", new[] { "лр", "4" })]           // ведущие нули — тот же номер работы
    [InlineData("Лекция 3 про пределы", new[] { "лекция", "3", "про", "пределы" })]
    [InlineData("отчёт", new[] { "отчет" })]
    [InlineData("a-b-c", new string[0])]                 // одиночные буквы — шум
    public void Tokenizer_splits_letters_and_digits(string value, string[] expected)
    {
        Assert.Equal(expected.OrderBy(t => t), DeadlineMatching.Tokenize(value).OrderBy(t => t));
    }

    [Fact]
    public void Zero_window_disables_the_date_component()
    {
        var score = DeadlineMatching.Score("ЛР4.docx", "ЛР4", Sorted, Sorted, windowDays: 0);

        Assert.InRange(score, 0, 1);
    }
}
