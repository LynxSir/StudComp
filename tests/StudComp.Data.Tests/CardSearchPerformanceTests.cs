using System.Diagnostics;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using Xunit.Abstractions;

namespace StudComp.Data.Tests;

/// <summary>
/// Замер поиска на пятитысячном корпусе (new_addons.md §9, DoD фазы 12.6). Прецедента замеров в
/// тестах в репозитории не было — поэтому порог взят с большим запасом относительно цели в 50 мс:
/// тест обязан ловить деградацию на порядок, а не мигать красным на загруженной машине.
/// Фактическое время печатается в вывод теста.
/// </summary>
public sealed class CardSearchPerformanceTests(ITestOutputHelper output) : DatabaseTestBase
{
    private const int CardCount = 5000;

    /// <summary>Потолок, выше которого поиск считается сломанным. Цель §9 — 50 мс.</summary>
    private const int BudgetMs = 250;

    private static readonly string[] Words =
    [
        "интеграл", "производная", "предел", "ряд", "функция", "матрица", "вектор", "множество",
        "теорема", "лемма", "формула", "определение", "свойство", "признак", "критерий",
        "непрерывность", "дифференцируемость", "сходимость", "ортогональность", "линейность",
    ];

    [Fact]
    public async Task Search_over_five_thousand_cards_stays_within_the_budget()
    {
        await SeedAsync();

        var search = new CardSearchRepository(Factory);

        // Прогрев: первый запрос платит за открытие соединения и разбор плана.
        await search.SearchAsync(CardQuery.Parse("интеграл"), new CardSearchOptions());

        var queries = new[]
        {
            "интеграл",                 // широкий префиксный — самый тяжёлый случай
            "\"теорема о среднем\"",    // фразовый
            "функция -матрица",         // с исключением
            "интеграл #формулы",        // с фильтром по метке
            "тип:формула трудные",      // вообще без полнотекстовой части
        };

        foreach (var query in queries)
        {
            var spec = CardQuery.Parse(query);
            var best = long.MaxValue;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var stopwatch = Stopwatch.StartNew();
                await search.SearchAsync(spec, new CardSearchOptions());
                stopwatch.Stop();
                best = Math.Min(best, stopwatch.ElapsedMilliseconds);
            }

            output.WriteLine($"{query,-28} {best,4} мс");
            Assert.True(best < BudgetMs, $"Запрос «{query}» занял {best} мс при потолке {BudgetMs} мс");
        }
    }

    [Fact]
    public async Task Deep_pages_stay_within_the_budget()
    {
        await SeedAsync();

        var search = new CardSearchRepository(Factory);
        var spec = CardQuery.Parse("интеграл");

        // Прогрев: первый запрос платит за открытие соединения и разбор плана.
        await search.SearchAsync(spec, new CardSearchOptions());

        // Сетка догружает страницы по мере прокрутки, и на пяти тысячах карточек их набирается
        // сотня. Замер показывает честную картину: глубокая страница дороже первой (LIMIT/OFFSET
        // заставляет ранжировать и пропускать всё, что выше), но остаётся в бюджете и уходит
        // асинхронно, не трогая UI-поток. Тест стережёт именно деградацию на порядок.
        foreach (var offset in new[] { 0, 500, 1000, 2000 })
        {
            var best = long.MaxValue;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var stopwatch = Stopwatch.StartNew();
                await search.SearchAsync(spec, new CardSearchOptions(Limit: 50, Offset: offset));
                stopwatch.Stop();
                best = Math.Min(best, stopwatch.ElapsedMilliseconds);
            }

            output.WriteLine($"страница со смещением {offset,5}: {best,4} мс");
            Assert.True(best < BudgetMs, $"Страница со смещением {offset} заняла {best} мс");
        }
    }

    [Fact]
    public async Task Sorting_over_five_thousand_cards_stays_within_the_budget()
    {
        await SeedAsync();

        var search = new CardSearchRepository(Factory);
        var spec = CardQuery.Parse("интеграл");
        await search.SearchAsync(spec, new CardSearchOptions());

        foreach (var sort in Enum.GetValues<CardSortOrder>())
        {
            var best = long.MaxValue;

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var stopwatch = Stopwatch.StartNew();
                await search.SearchAsync(spec, new CardSearchOptions(Sort: sort));
                stopwatch.Stop();
                best = Math.Min(best, stopwatch.ElapsedMilliseconds);
            }

            output.WriteLine($"{sort,-16} {best,4} мс");
            Assert.True(best < BudgetMs, $"Сортировка {sort} заняла {best} мс при потолке {BudgetMs} мс");
        }
    }

    [Fact]
    public async Task Rebuilding_the_index_over_five_thousand_cards_is_not_a_coffee_break()
    {
        await SeedAsync();

        var search = new CardSearchRepository(Factory);
        var stopwatch = Stopwatch.StartNew();
        var indexed = await search.RebuildAsync();
        stopwatch.Stop();

        output.WriteLine($"перестроение индекса: {stopwatch.ElapsedMilliseconds} мс на {indexed} карточек");

        Assert.Equal(CardCount, indexed);
        Assert.True(
            stopwatch.ElapsedMilliseconds < 10_000,
            $"Перестроение заняло {stopwatch.ElapsedMilliseconds} мс");
    }

    private async Task SeedAsync()
    {
        var random = new Random(20260906);
        var subject = TestData.Subject();
        var tag = TestData.CardTag("формулы");

        var cards = new List<Card>(CardCount);
        for (var i = 0; i < CardCount; i++)
        {
            var card = TestData.Card(
                subject.Id,
                front: $"{Pick(random)} {Pick(random)} {i}",
                back: string.Join(' ', Enumerable.Range(0, 40).Select(_ => Pick(random))));
            card.Kind = (CardKind)(i % 5);
            card.Lapses = i % 7;
            cards.Add(card);
        }

        await using var context = CreateContext();
        context.Subjects.Add(subject);
        context.CardTags.Add(tag);
        context.Cards.AddRange(cards);
        await context.SaveChangesAsync();

        // Метка на каждой десятой — чтобы у фильтра по меткам было что отсеивать.
        context.CardTagLinks.AddRange(cards
            .Where((_, index) => index % 10 == 0)
            .Select(card => new CardTagLink { CardId = card.Id, TagId = tag.Id }));
        await context.SaveChangesAsync();

        static string Pick(Random random) => Words[random.Next(Words.Length)];
    }
}
