using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Modules.ReportForge.Services;

namespace StudComp.Modules.ReportForge.Tests;

/// <summary>
/// Общая обвязка тестов рендерера: собрать документ в память, вытащить нужную часть пакета,
/// прогнать схемную валидацию (ARCHITECTURE §12).
/// </summary>
internal static class DocxTestHelpers
{
    internal static readonly XNamespace W =
        "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    internal static GostStyleProfile DefaultProfile { get; } =
        new GostStyleProfileProvider(new EmptyTemplateRepository()).GetDefault();

    internal static string DefaultProfileJson { get; } =
        new GostStyleProfileProvider(new EmptyTemplateRepository()).GetDefaultJson();

    internal static IMarkdownDocumentModelBuilder Builder { get; } = new MarkdownDocumentModelBuilder();

    internal static IGostDocxRenderer Renderer { get; } =
        new GostDocxRenderer(NullLogger<GostDocxRenderer>.Instance);

    /// <summary>Рендерит модель в память и отдаёт байты готового <c>.docx</c>.</summary>
    internal static async Task<byte[]> RenderAsync(ReportDocumentModel model, GostStyleProfile? profile = null)
    {
        using var stream = new MemoryStream();
        await Renderer.RenderAsync(model, profile ?? DefaultProfile, stream, CancellationToken.None);
        return stream.ToArray();
    }

    /// <summary>Разбирает markdown и сразу рендерит — самый частый путь в тестах.</summary>
    internal static Task<byte[]> RenderMarkdownAsync(
        string markdown,
        bool tableOfContents = false,
        TitlePageInfo? titlePage = null,
        GostStyleProfile? profile = null)
    {
        var model = Builder.Build(markdown) with
        {
            TitlePage = titlePage,
            GenerateTableOfContents = tableOfContents,
        };

        return RenderAsync(model, profile);
    }

    internal static XDocument ReadDocument(byte[] docx) =>
        Read(docx, document => document.MainDocumentPart!);

    internal static XDocument ReadStyles(byte[] docx) =>
        Read(docx, document => document.MainDocumentPart!.StyleDefinitionsPart!);

    internal static XDocument ReadNumbering(byte[] docx) =>
        Read(docx, document => document.MainDocumentPart!.NumberingDefinitionsPart!);

    internal static XDocument ReadSettings(byte[] docx) =>
        Read(docx, document => document.MainDocumentPart!.DocumentSettingsPart!);

    /// <summary>XML всех колонтитулов документа в порядке их частей.</summary>
    internal static IReadOnlyList<XDocument> ReadFooters(byte[] docx)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);

        return [.. document.MainDocumentPart!.FooterParts.Select(part =>
        {
            using var content = part.GetStream();
            return XDocument.Load(content);
        })];
    }

    /// <summary>
    /// Ошибки схемной валидации OOXML, сразу приведённые к строкам: пустой список — обязательное
    /// условие (ARCHITECTURE §12). Форматируем внутри, пока пакет открыт — <c>ValidationErrorInfo.Path</c>
    /// после закрытия пакета обращаться к частям уже не может.
    /// </summary>
    internal static IReadOnlyList<string> Validate(byte[] docx)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);

        return [.. new OpenXmlValidator()
            .Validate(document)
            .Select(error => $"{error.Path?.XPath}: {error.Description}")];
    }

    /// <summary>Показывает ошибки валидации так, чтобы по падению теста было понятно, что чинить.</summary>
    internal static string Describe(IReadOnlyList<string> errors) =>
        string.Join(Environment.NewLine, errors);

    /// <summary>
    /// Сравнивает профили по значению. Прямой <c>Assert.Equal</c> тут не годится: record сравнивает
    /// <see cref="GostStyleProfile.HeadingRules"/> по ссылке, а списки после разбора JSON всегда разные.
    /// </summary>
    internal static void AssertSameProfile(GostStyleProfile expected, GostStyleProfile actual)
    {
        Assert.Equal(expected with { HeadingRules = [] }, actual with { HeadingRules = [] });
        Assert.Equal(expected.HeadingRules, actual.HeadingRules);
    }

    /// <summary>Текст документа по абзацам — для проверок вида «подпись такая-то на месте».</summary>
    internal static IReadOnlyList<string> ReadParagraphs(byte[] docx) =>
        [.. ReadDocument(docx).Descendants(W + "p").Select(ParagraphText)];

    internal static string ReadText(byte[] docx) => string.Join("\n", ReadParagraphs(docx));

    internal static string ParagraphText(XElement paragraph) =>
        string.Concat(paragraph.Descendants(W + "t").Select(text => text.Value));

    private static XDocument Read(byte[] docx, Func<WordprocessingDocument, OpenXmlPart> selector)
    {
        using var stream = new MemoryStream(docx, writable: false);
        using var document = WordprocessingDocument.Open(stream, isEditable: false);
        using var content = selector(document).GetStream();
        return XDocument.Load(content);
    }

    /// <summary>
    /// Заглушка репозитория: профиль по умолчанию читается из встроенного ресурса и в базу не ходит.
    /// </summary>
    private sealed class EmptyTemplateRepository : IReportTemplateRepository
    {
        public Task<IReadOnlyList<ReportTemplate>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ReportTemplate>>([]);

        public Task<ReportTemplate?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<ReportTemplate?>(null);

        public Task<ReportTemplate?> GetByGostVariantAsync(string gostVariant, CancellationToken ct = default) =>
            Task.FromResult<ReportTemplate?>(null);

        public Task AddAsync(ReportTemplate template, CancellationToken ct = default) => Task.CompletedTask;

        public Task UpdateAsync(ReportTemplate template, CancellationToken ct = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
