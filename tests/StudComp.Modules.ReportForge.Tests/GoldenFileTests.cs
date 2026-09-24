using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.Modules.ReportForge.Tests;

/// <summary>
/// Golden-file тесты (ARCHITECTURE §12): каждый markdown из <c>Fixtures/</c> → нормализованный
/// <c>document.xml</c> сравнивается с эталоном <c>Golden/&lt;имя&gt;.document.xml</c> и обязан
/// проходить <c>OpenXmlValidator</c> без ошибок. Ловят незамеченные изменения разметки, которых
/// точечные проверки не видят.
/// </summary>
/// <remarks>
/// Эталоны обновляются осознанно: <c>RUBRICA_UPDATE_GOLDEN=1 dotnet test</c> перезапишет файлы в
/// исходниках, после чего разницу нужно просмотреть в git diff (ADR §16.34). Phase 12 расширил
/// набор фикстур: вложенные списки, длинные таблицы, кириллица в именах изображений, пограничная
/// инлайн-разметка.
/// </remarks>
public partial class GoldenFileTests
{
    private const string UpdateVariable = "RUBRICA_UPDATE_GOLDEN";

    private static readonly TitlePageInfo TitlePage = new(
        University: "Технологический университет",
        Faculty: "Институт информационных технологий",
        Department: "Программной инженерии",
        WorkType: "Отчёт по лабораторной работе",
        SubjectName: "Математический анализ",
        StudentName: "Иванов И. И.",
        StudentGroup: "ИВТ-201",
        SupervisorName: "Петров П. П.",
        City: "Новосибирск",
        Year: 2026);

    public static IEnumerable<object[]> Fixtures =>
        Directory.EnumerateFiles("Fixtures", "*.md")
            .Select(path => new object[] { Path.GetFileNameWithoutExtension(path) });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Fixture_matches_the_snapshot_and_passes_schema_validation(string stem)
    {
        var (docx, actual) = await RenderFixtureAsync(stem);

        var validationErrors = DocxTestHelpers.Validate(docx);
        Assert.True(
            validationErrors.Count == 0,
            $"Фикстура «{stem}» не прошла схемную валидацию OOXML:{Environment.NewLine}"
            + DocxTestHelpers.Describe(validationErrors));

        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            await File.WriteAllTextAsync(SourceGoldenPath(stem), actual);
        }

        var expectedPath = Path.Combine("Golden", $"{stem}.document.xml");
        Assert.True(
            File.Exists(expectedPath),
            $"Нет эталона {expectedPath}. Создать: {UpdateVariable}=1 dotnet test");

        var expected = Normalize(await File.ReadAllTextAsync(expectedPath));

        Assert.Equal(expected, actual);
    }

    private static async Task<(byte[] Docx, string NormalizedXml)> RenderFixtureAsync(string stem)
    {
        var markdown = await File.ReadAllTextAsync(Path.Combine("Fixtures", $"{stem}.md"));

        var model = DocxTestHelpers.Builder.Build(markdown) with
        {
            TitlePage = TitlePage,
            GenerateTableOfContents = true,
        };

        // Любая картинка подставляется data-URI: снапшот не должен зависеть от путей на машине.
        // Разбор кириллического пути в alt/подписи всё равно попадает в снапшот из самого markdown.
        model = model with
        {
            Blocks =
            [
                .. model.Blocks.Select(block => block is ImageBlock image
                    ? image with { PathOrBase64 = TestImages.SampleDataUri }
                    : block),
            ],
        };

        var docx = await DocxTestHelpers.RenderAsync(model);
        return (docx, Normalize(DocxTestHelpers.ReadDocument(docx).ToString()));
    }

    /// <summary>
    /// Приводит XML к сравнимому виду: одинаковые переводы строк и стабильные идентификаторы связей.
    /// OpenXML SDK генерирует их случайно (<c>Rf3a1…</c>), поэтому они заменяются на порядковые —
    /// структура ссылок при этом остаётся проверяемой.
    /// </summary>
    private static string Normalize(string xml)
    {
        var normalized = xml.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd() + "\n";
        var replacements = new Dictionary<string, string>(StringComparer.Ordinal);

        return RelationshipIdPattern().Replace(normalized, match =>
        {
            if (!replacements.TryGetValue(match.Value, out var stable))
            {
                stable = $"rel{replacements.Count + 1}";
                replacements[match.Value] = stable;
            }

            return stable;
        });
    }

    [GeneratedRegex("R[0-9a-f]{16}")]
    private static partial Regex RelationshipIdPattern();

    /// <summary>
    /// Путь к эталону в исходниках, а не в каталоге сборки: перезаписывать надо тот файл, который
    /// лежит в git.
    /// </summary>
    private static string SourceGoldenPath(string stem, [CallerFilePath] string testFilePath = "") =>
        Path.Combine(Path.GetDirectoryName(testFilePath)!, "Golden", $"{stem}.document.xml");
}
