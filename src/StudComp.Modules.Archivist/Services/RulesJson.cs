using System.Text.Json;
using System.Text.Json.Serialization;
using StudComp.Core.Common;
using StudComp.Core.Domain;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Формат обмена правилами сортировки (ARCHITECTURE §8.7): версионированный конверт, предмет — по
/// <b>имени</b>, а не по <see cref="Guid"/> (Guid машинно-зависим и на другом ПК бессмыслен).
/// </summary>
internal static class RulesJson
{
    private const string SchemaTag = "rubrica.archivist.rules";
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize(IReadOnlyList<RuleExportDto> rules) =>
        JsonSerializer.Serialize(
            new RulesEnvelope { Schema = SchemaTag, Version = CurrentVersion, Rules = [.. rules] },
            Options);

    public static Result<IReadOnlyList<RuleExportDto>> Deserialize(string json)
    {
        RulesEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<RulesEnvelope>(json, Options);
        }
        catch (JsonException)
        {
            return Result<IReadOnlyList<RuleExportDto>>.Failure(
                "archivist.import_bad_format", "Файл не похож на набор правил Rubrica.");
        }

        if (envelope is null
            || !string.Equals(envelope.Schema, SchemaTag, StringComparison.OrdinalIgnoreCase))
        {
            return Result<IReadOnlyList<RuleExportDto>>.Failure(
                "archivist.import_bad_format", "Файл не похож на набор правил Rubrica.");
        }

        return Result<IReadOnlyList<RuleExportDto>>.Success(envelope.Rules ?? []);
    }
}

/// <summary>Конверт файла экспорта: тег схемы + версия + сами правила.</summary>
internal sealed class RulesEnvelope
{
    public string Schema { get; set; } = string.Empty;

    public int Version { get; set; }

    public List<RuleExportDto> Rules { get; set; } = [];
}

/// <summary>Правило в переносимом виде: без <see cref="Guid"/>, предмет — по имени.</summary>
internal sealed class RuleExportDto
{
    public string Pattern { get; set; } = string.Empty;

    public RuleMatchType MatchType { get; set; }

    public int Priority { get; set; }

    public bool Enabled { get; set; } = true;

    public string? WorkType { get; set; }

    public string RenameTemplate { get; set; } = string.Empty;

    public string? SubjectName { get; set; }

    /// <summary>
    /// Папка, в которой действует правило; <see langword="null"/> — во всех. Путь машинно-зависим, как
    /// и <see cref="Guid"/> предмета, но подставить вместо него нечего — при импорте на другом ПК он
    /// просто не совпадёт ни с одной наблюдаемой папкой, и это видно в редакторе правила.
    /// Поле добавлено в Phase 10; версия конверта не менялась — старые файлы его просто не несут.
    /// </summary>
    public string? WatchedFolder { get; set; }
}
