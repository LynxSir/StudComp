using System.Text.Json;
using System.Text.Json.Serialization;
using StudComp.Core.Common;
using StudComp.Core.Domain;

namespace StudComp.Modules.Cards.Services;

/// <summary>
/// Формат обмена картотекой (new_addons.md §7.6): версионированный конверт, предмет и колода — по
/// <b>имени</b>, а не по <see cref="Guid"/> (Guid машинно-зависим и на другом ПК бессмыслен). Ровно
/// тот же приём, что у <c>RulesJson</c> Архивариуса (ADR §16.46).
/// </summary>
internal static class CardsJson
{
    private const string SchemaTag = "rubrica.cards.export";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(CardsExportPayload payload) =>
        JsonSerializer.Serialize(
            new CardsEnvelope
            {
                Schema = SchemaTag,
                Version = CurrentVersion,
                Cards = [.. payload.Cards],
                Decks = [.. payload.Decks],
                Tags = [.. payload.Tags],
            },
            Options);

    public static Result<CardsExportPayload> Deserialize(string json)
    {
        CardsEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<CardsEnvelope>(json, Options);
        }
        catch (JsonException)
        {
            return Result<CardsExportPayload>.Failure(
                "cards.import_bad_format", "Файл не похож на экспорт картотеки Rubrica.");
        }

        if (envelope is null || !string.Equals(envelope.Schema, SchemaTag, StringComparison.OrdinalIgnoreCase))
        {
            return Result<CardsExportPayload>.Failure(
                "cards.import_bad_format", "Файл не похож на экспорт картотеки Rubrica.");
        }

        return Result<CardsExportPayload>.Success(
            new CardsExportPayload(envelope.Cards ?? [], envelope.Decks ?? [], envelope.Tags ?? []));
    }
}

/// <summary>Конверт файла экспорта: тег схемы + версия + три параллельных списка сущностей.</summary>
internal sealed class CardsEnvelope
{
    public string Schema { get; set; } = string.Empty;

    public int Version { get; set; }

    public List<CardExportDto> Cards { get; set; } = [];

    public List<DeckExportDto> Decks { get; set; } = [];

    public List<TagExportDto> Tags { get; set; } = [];
}

/// <summary>
/// Карточка в переносимом виде. <see cref="Id"/> сохраняется только для честного сравнения в
/// round-trip тестах — при импорте он не используется как ключ существования: каждая запись всегда
/// создаёт (либо в режиме «Объединить» — обновляет по совпадению <see cref="Front"/>) новую сущность,
/// а не подставляет чужой первичный ключ буквально.
/// </summary>
internal sealed class CardExportDto
{
    public Guid Id { get; set; }

    public string? SubjectName { get; set; }

    public string? DeckName { get; set; }

    public CardKind Kind { get; set; }

    public string Front { get; set; } = string.Empty;

    public string Back { get; set; } = string.Empty;

    public string? Hint { get; set; }

    public string? Source { get; set; }

    public bool IsPinned { get; set; }

    public bool IsSuspended { get; set; }

    public CardDifficulty Difficulty { get; set; }

    public List<string> Tags { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Колода в переносимом виде. Непустой <see cref="QueryExpression"/> — умная подборка: её карточки не
/// хранятся отдельно (они и так не лежат в колоде физически), выражение просто пересчитается на новом
/// месте, если там найдутся подходящие предметы/метки.
/// </summary>
internal sealed class DeckExportDto
{
    public string Name { get; set; } = string.Empty;

    public string? SubjectName { get; set; }

    public string? Description { get; set; }

    public string? ColorHex { get; set; }

    public int SortOrder { get; set; }

    public string? QueryExpression { get; set; }
}

/// <summary>
/// Метка в переносимом виде — отдельным списком от карточек, чтобы оформление (цвет) метки не
/// терялось, если ни одна экспортируемая карточка её не задела.
/// </summary>
internal sealed class TagExportDto
{
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string? ColorHex { get; set; }
}

/// <summary>Разобранное содержимое конверта — то, с чем работает <see cref="ICardImportExportService"/>.</summary>
internal sealed record CardsExportPayload(
    IReadOnlyList<CardExportDto> Cards,
    IReadOnlyList<DeckExportDto> Decks,
    IReadOnlyList<TagExportDto> Tags);
