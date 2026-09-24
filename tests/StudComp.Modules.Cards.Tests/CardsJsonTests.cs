using StudComp.Core.Domain;
using StudComp.Modules.Cards.Services;

namespace StudComp.Modules.Cards.Tests;

/// <summary>Формат экспорта/импорта картотеки (new_addons.md §7.6): версионированный конверт, по образцу <c>RulesJsonTests</c>.</summary>
public sealed class CardsJsonTests
{
    [Fact]
    public void Round_trip_preserves_every_field()
    {
        var payload = new CardsExportPayload(
            [
                new CardExportDto
                {
                    Id = Guid.NewGuid(),
                    SubjectName = "Матан",
                    DeckName = "К экзамену",
                    Kind = CardKind.Formula,
                    Front = "Теорема Стокса",
                    Back = "∮_C ω = ∫_S dω",
                    Hint = "Обобщение формулы Ньютона–Лейбница",
                    Source = "Лекция 12.09",
                    IsPinned = true,
                    IsSuspended = false,
                    Difficulty = CardDifficulty.Hard,
                    Tags = ["формулы", "анализ"],
                    CreatedAt = DateTimeOffset.Parse("2026-01-01T10:00:00+00:00"),
                    UpdatedAt = DateTimeOffset.Parse("2026-02-01T10:00:00+00:00"),
                },
            ],
            [
                new DeckExportDto
                {
                    Name = "К экзамену",
                    SubjectName = "Матан",
                    Description = "Материалы к сессии",
                    ColorHex = "#8E2434",
                    SortOrder = 3,
                    QueryExpression = null,
                },
            ],
            [
                new TagExportDto { Name = "формулы", DisplayName = "Формулы", ColorHex = "#123456" },
            ]);

        var json = CardsJson.Serialize(payload);
        var parsed = CardsJson.Deserialize(json);

        Assert.True(parsed.IsSuccess);
        var card = Assert.Single(parsed.Value.Cards);
        Assert.Equal(payload.Cards[0].Id, card.Id);
        Assert.Equal("Матан", card.SubjectName);
        Assert.Equal("К экзамену", card.DeckName);
        Assert.Equal(CardKind.Formula, card.Kind);
        Assert.Equal("Теорема Стокса", card.Front);
        Assert.Equal("∮_C ω = ∫_S dω", card.Back);
        Assert.Equal("Обобщение формулы Ньютона–Лейбница", card.Hint);
        Assert.Equal("Лекция 12.09", card.Source);
        Assert.True(card.IsPinned);
        Assert.Equal(CardDifficulty.Hard, card.Difficulty);
        Assert.Equal(["формулы", "анализ"], card.Tags);

        var deck = Assert.Single(parsed.Value.Decks);
        Assert.Equal("К экзамену", deck.Name);
        Assert.Equal("Матан", deck.SubjectName);
        Assert.Equal("Материалы к сессии", deck.Description);
        Assert.Equal("#8E2434", deck.ColorHex);
        Assert.Equal(3, deck.SortOrder);
        Assert.Null(deck.QueryExpression);

        var tag = Assert.Single(parsed.Value.Tags);
        Assert.Equal("формулы", tag.Name);
        Assert.Equal("Формулы", tag.DisplayName);
        Assert.Equal("#123456", tag.ColorHex);
    }

    [Fact]
    public void A_smart_deck_keeps_its_query_expression()
    {
        var payload = new CardsExportPayload(
            [],
            [new DeckExportDto { Name = "Трудные по матану", QueryExpression = "@матан трудные" }],
            []);

        var parsed = CardsJson.Deserialize(CardsJson.Serialize(payload));

        Assert.Equal("@матан трудные", Assert.Single(parsed.Value.Decks).QueryExpression);
    }

    [Fact]
    public void Enum_is_written_as_a_name_not_a_number()
    {
        var payload = new CardsExportPayload(
            [new CardExportDto { Front = "Ф", Back = "О", Kind = CardKind.Question }], [], []);

        var json = CardsJson.Serialize(payload);

        Assert.Contains("\"Question\"", json);
        Assert.DoesNotContain("\"kind\": 1", json);
    }

    [Fact]
    public void Unknown_and_missing_fields_are_tolerated()
    {
        const string json = """
            { "schema": "rubrica.cards.export", "version": 1, "extra": "ignored", "cards": [
              { "front": "Ф", "back": "О" }
            ] }
            """;

        var parsed = CardsJson.Deserialize(json);

        Assert.True(parsed.IsSuccess);
        var card = Assert.Single(parsed.Value.Cards);
        Assert.Equal("Ф", card.Front);
        Assert.Null(card.SubjectName);
        Assert.Empty(card.Tags);
    }

    [Theory]
    [InlineData("{ \"not\": \"ours\" }")]
    [InlineData("не json вовсе")]
    [InlineData("{ \"schema\": \"something.else\", \"cards\": [] }")]
    public void Foreign_or_broken_content_is_rejected(string json)
    {
        var parsed = CardsJson.Deserialize(json);

        Assert.True(parsed.IsFailure);
        Assert.Equal("cards.import_bad_format", parsed.Error.Code);
    }

    [Fact]
    public void Empty_payload_round_trips_to_empty_lists()
    {
        var parsed = CardsJson.Deserialize(CardsJson.Serialize(new CardsExportPayload([], [], [])));

        Assert.True(parsed.IsSuccess);
        Assert.Empty(parsed.Value.Cards);
        Assert.Empty(parsed.Value.Decks);
        Assert.Empty(parsed.Value.Tags);
    }
}
