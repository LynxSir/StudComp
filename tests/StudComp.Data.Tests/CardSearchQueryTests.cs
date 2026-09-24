using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Data.Tests;

/// <summary>
/// Поведение поиска (new_addons.md §4.1, §4.4, §4.5). Каждый факт прогоняется <b>через обе</b>
/// реализации: быструю на FTS5 и деградированный фолбэк. Вторая тут не для полноты, а как эталон —
/// она написана очевидно правильно, и расхождение между ними означает ошибку в SQL.
/// </summary>
public sealed class CardSearchQueryTests : DatabaseTestBase
{
    /// <summary><c>true</c> — путь через FTS5, <c>false</c> — фолбэк без индекса.</summary>
    public static TheoryData<bool> Engines => new() { true, false };

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Cyrillic_case_is_folded_both_ways(bool fullText)
    {
        var card = TestData.Card(front: "Интеграл Римана");
        await SeedAsync(card);

        Assert.Contains(card.Id, await FindAsync(fullText, "ИНТЕГРАЛ"));
        Assert.Contains(card.Id, await FindAsync(fullText, "интеграл"));
        Assert.Contains(card.Id, await FindAsync(fullText, "ИнТеГрАл"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Yo_and_ye_are_the_same_letter(bool fullText)
    {
        var withYo = TestData.Card(front: "Ёмкость конденсатора");
        var withYe = TestData.Card(front: "Емкость аккумулятора");
        await SeedAsync(withYo, withYe);

        Assert.Contains(withYo.Id, await FindAsync(fullText, "емкость"));
        Assert.Contains(withYe.Id, await FindAsync(fullText, "ёмкость"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Words_are_matched_by_prefix(bool fullText)
    {
        var card = TestData.Card(front: "Интегрирование по частям");
        await SeedAsync(card);

        Assert.Contains(card.Id, await FindAsync(fullText, "интегр"));
        Assert.Empty(await FindAsync(fullText, "дифферен"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Quoted_phrase_matches_only_the_exact_wording(bool fullText)
    {
        var match = TestData.Card(front: "Интегрирование", back: "Считается по частям.");
        var noMatch = TestData.Card(front: "Части речи", back: "Разбор по составу.");
        await SeedAsync(match, noMatch);

        var hits = await FindAsync(fullText, "\"по частям\"");

        Assert.Contains(match.Id, hits);
        Assert.DoesNotContain(noMatch.Id, hits);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Minus_excludes_matching_cards(bool fullText)
    {
        var keep = TestData.Card(front: "Интеграл Римана", back: "Через суммы Дарбу.");
        var drop = TestData.Card(front: "Интеграл Лейбница", back: "Формула Ньютона-Лейбница.");
        await SeedAsync(keep, drop);

        var hits = await FindAsync(fullText, "интеграл -лейбница");

        Assert.Contains(keep.Id, hits);
        Assert.DoesNotContain(drop.Id, hits);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Tag_filter_narrows_the_result(bool fullText)
    {
        var tagged = TestData.Card(front: "Интеграл Римана");
        var untagged = TestData.Card(front: "Интеграл Лебега");
        var tag = TestData.CardTag("формулы");
        await SeedAsync(tagged, untagged);

        await using (var arrange = CreateContext())
        {
            arrange.CardTags.Add(tag);
            arrange.CardTagLinks.Add(new CardTagLink { CardId = tagged.Id, TagId = tag.Id });
            await arrange.SaveChangesAsync();
        }

        var hits = await FindAsync(fullText, "интеграл #формулы");

        Assert.Contains(tagged.Id, hits);
        Assert.DoesNotContain(untagged.Id, hits);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Subject_operator_matches_by_name_and_by_code(bool fullText)
    {
        var subject = TestData.Subject("Математический анализ");
        var mine = TestData.Card(subject.Id, front: "Интеграл Римана");
        var other = TestData.Card(front: "Интеграл Лебега");

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(subject);
            arrange.Cards.AddRange(mine, other);
            await arrange.SaveChangesAsync();
        }

        Assert.Equal([mine.Id], await FindAsync(fullText, "интеграл @\"Математический анализ\""));
        Assert.Equal([mine.Id], await FindAsync(fullText, "интеграл @ма"));
        Assert.Empty(await FindAsync(fullText, "интеграл @физика"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Kind_and_state_filters_work_without_any_text(bool fullText)
    {
        var formula = TestData.Card(front: "Интеграл Римана");
        var question = TestData.Card(front: "Что такое семафор?");
        question.Kind = CardKind.Question;
        question.Lapses = 3;
        await SeedAsync(formula, question);

        Assert.Equal([formula.Id], await FindAsync(fullText, "тип:формула"));
        Assert.Equal([question.Id], await FindAsync(fullText, "тип:вопрос"));
        Assert.Equal([question.Id], await FindAsync(fullText, "трудные"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Presence_filters_find_cards_needing_cleanup(bool fullText)
    {
        var withHint = TestData.Card(front: "Интеграл Римана");
        withHint.Hint = "Через суммы";
        var withoutHint = TestData.Card(front: "Интеграл Лебега");
        await SeedAsync(withHint, withoutHint);

        Assert.Equal([withHint.Id], await FindAsync(fullText, "есть:подсказка"));
        Assert.Equal([withoutHint.Id], await FindAsync(fullText, "нет:подсказка"));
        Assert.Equal(2, (await FindAsync(fullText, "нет:метки")).Count);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Soft_deleted_cards_are_visible_only_in_the_trash(bool fullText)
    {
        var live = TestData.Card(front: "Интеграл Римана");
        var trashed = TestData.Card(front: "Интеграл Лебега");
        trashed.DeletedAt = DateTimeOffset.UtcNow;
        await SeedAsync(live, trashed);

        Assert.Equal([live.Id], await FindAsync(fullText, "интеграл"));
        Assert.Equal([trashed.Id], await FindAsync(fullText, "интеграл корзина"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Pinned_card_outranks_an_ordinary_one(bool fullText)
    {
        var ordinary = TestData.Card(front: "Интеграл Лебега");
        var pinned = TestData.Card(front: "Интеграл Римана");
        pinned.IsPinned = true;
        await SeedAsync(ordinary, pinned);

        var hits = await FindAsync(fullText, "интеграл");

        Assert.Equal(pinned.Id, hits[0]);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Suspended_card_sinks_below_an_ordinary_one(bool fullText)
    {
        var suspended = TestData.Card(front: "Интеграл Римана");
        suspended.IsSuspended = true;
        var ordinary = TestData.Card(front: "Интеграл Лебега");
        await SeedAsync(suspended, ordinary);

        var hits = await FindAsync(fullText, "интеграл");

        Assert.Equal(ordinary.Id, hits[0]);
        Assert.Equal(suspended.Id, hits[1]);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Empty_query_returns_recent_cards_instead_of_nothing(bool fullText)
    {
        var first = TestData.Card(front: "Интеграл Римана");
        var second = TestData.Card(front: "Интеграл Лебега");
        await SeedAsync(first, second);

        Assert.Equal(2, (await FindAsync(fullText, "   ")).Count);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Query_made_of_fts_special_characters_does_not_blow_up(bool fullText)
    {
        var card = TestData.Card(front: "Интеграл Римана");
        await SeedAsync(card);

        foreach (var query in new[] { "\"", "*", "^", "()", "NEAR(a b)", "a AND b", "';DROP TABLE Cards;--" })
        {
            var hits = await FindAsync(fullText, query);
            Assert.NotNull(hits);
        }

        // Карточка на месте: ни один из этих запросов ничего не сломал.
        await using var assert = CreateContext();
        Assert.Equal(1, assert.Cards.Count());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public async Task Limit_and_offset_page_through_the_result(bool fullText)
    {
        var cards = Enumerable.Range(0, 5)
            .Select(i => TestData.Card(front: $"Интеграл номер {i}"))
            .ToArray();
        await SeedAsync(cards);

        var page1 = await SearchAsync(fullText, "интеграл", new CardSearchOptions(Limit: 2));
        var page2 = await SearchAsync(fullText, "интеграл", new CardSearchOptions(Limit: 2, Offset: 2));

        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.Empty(page1.Select(x => x.CardId).Intersect(page2.Select(x => x.CardId)));
    }

    [Fact]
    public async Task Active_subject_lifts_its_cards_up()
    {
        var active = TestData.Subject("Математический анализ");
        var mine = TestData.Card(active.Id, front: "Интеграл Лебега");
        var other = TestData.Card(front: "Интеграл Римана");

        await using (var arrange = CreateContext())
        {
            arrange.Subjects.Add(active);
            arrange.Cards.AddRange(mine, other);
            await arrange.SaveChangesAsync();
        }

        var hits = await SearchAsync(
            fullText: true,
            "интеграл",
            new CardSearchOptions(ActiveSubjectId: active.Id));

        Assert.Equal(mine.Id, hits[0].CardId);
    }

    [Fact]
    public async Task Card_whose_title_is_exactly_the_query_comes_first()
    {
        // Регистр намеренно другой: складывать кириллицу приходится в памяти, в SQL этого не сделать.
        var exact = TestData.Card(front: "Интеграл", back: "Короткое определение.");
        var wordy = TestData.Card(
            front: "Интеграл Римана и интеграл Лебега",
            back: "Интеграл, интеграл, ещё раз интеграл — чтобы обогнать по частоте слова.");
        await SeedAsync(wordy, exact);

        var hits = await FindAsync(fullText: true, "интеграл");

        Assert.Equal(exact.Id, hits[0]);
    }

    [Fact]
    public async Task Snippet_marks_the_match_for_the_view_model()
    {
        var card = TestData.Card(
            front: "Интеграл",
            back: "Определённый интеграл считается по формуле Ньютона-Лейбница.");
        await SeedAsync(card);

        var hits = await SearchAsync(fullText: true, "ньютона", new CardSearchOptions());

        Assert.Contains(CardSearchHit.HighlightStart, hits[0].Snippet);
        Assert.Contains(CardSearchHit.HighlightEnd, hits[0].Snippet);
    }

    private ICardSearchRepository Engine(bool fullText) => fullText
        ? new CardSearchRepository(Factory)
        : new FallbackCardSearchRepository(Factory);

    private async Task SeedAsync(params Card[] cards)
    {
        await using var context = CreateContext();
        context.Cards.AddRange(cards);
        await context.SaveChangesAsync();
    }

    private async Task<IReadOnlyList<Guid>> FindAsync(bool fullText, string query)
    {
        var hits = await SearchAsync(fullText, query, new CardSearchOptions());
        return hits.Select(x => x.CardId).ToList();
    }

    private Task<IReadOnlyList<CardSearchHit>> SearchAsync(
        bool fullText,
        string query,
        CardSearchOptions options) =>
        Engine(fullText).SearchAsync(CardQuery.Parse(query), options);
}
