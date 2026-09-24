using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Data.Repositories;

namespace StudComp.Modules.ReportForge.Services;

/// <summary>
/// Отдаёт профиль оформления: либо привязанный к шаблону, либо заводской ГОСТ 7.32-2017
/// (ARCHITECTURE §10.4).
/// </summary>
public interface IGostStyleProfileProvider
{
    /// <summary>Заводской профиль из встроенного <c>gost-7.32-2017.json</c>. Разбирается один раз и кешируется.</summary>
    GostStyleProfile GetDefault();

    /// <summary>Заводской профиль в исходном JSON-виде — им сидируется <c>ReportTemplate</c>.</summary>
    string GetDefaultJson();

    /// <summary>
    /// Профиль шаблона <paramref name="templateId"/>; если шаблон не найден или его JSON битый —
    /// заводской профиль (генерация отчёта не должна падать из-за настроек оформления).
    /// </summary>
    Task<GostStyleProfile> GetForTemplateAsync(Guid? templateId, CancellationToken ct = default);
}

internal sealed class GostStyleProfileProvider(IReportTemplateRepository templates) : IGostStyleProfileProvider
{
    /// <summary>Имя встроенного ресурса — путь к файлу в проекте с точками вместо разделителей.</summary>
    private const string DefaultProfileResource = "StudComp.Modules.ReportForge.Templates.gost-7.32-2017.json";

    /// <summary>Настройки общие для чтения встроенного профиля и профилей из БД — форма JSON одна и та же.</summary>
    internal static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly Lazy<string> DefaultJson = new(ReadEmbeddedProfile, isThreadSafe: true);
    private static readonly Lazy<GostStyleProfile> Default = new(
        () => Deserialize(DefaultJson.Value)
              ?? throw new InvalidOperationException($"Встроенный профиль {DefaultProfileResource} не разобрался."),
        isThreadSafe: true);

    public GostStyleProfile GetDefault() => Default.Value;

    public string GetDefaultJson() => DefaultJson.Value;

    public async Task<GostStyleProfile> GetForTemplateAsync(Guid? templateId, CancellationToken ct = default)
    {
        if (templateId is not { } id)
        {
            return GetDefault();
        }

        var template = await templates.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (template is null || string.IsNullOrWhiteSpace(template.StyleProfileJson))
        {
            return GetDefault();
        }

        return Deserialize(template.StyleProfileJson) ?? GetDefault();
    }

    private static GostStyleProfile? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<GostStyleProfile>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ReadEmbeddedProfile()
    {
        var assembly = typeof(GostStyleProfileProvider).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(DefaultProfileResource)
            ?? throw new InvalidOperationException(
                $"Встроенный ресурс {DefaultProfileResource} не найден — проверь EmbeddedResource в .csproj.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
