using Microsoft.Extensions.Logging;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;

namespace StudComp.Modules.ReportForge.Services;

/// <summary>
/// Единственная точка входа генерации отчёта (ARCHITECTURE §10.3): собрать источник → модель →
/// профиль → <c>.docx</c>, попутно заведя запись <c>ReportJob</c>.
/// </summary>
internal sealed class ReportPipeline(
    IMarkdownDocumentModelBuilder builder,
    IGostDocxRenderer renderer,
    IGostStyleProfileProvider profiles,
    IReportTemplateService templates,
    IReportJobRepository jobs,
    IFileSystem fileSystem,
    ILogger<ReportPipeline> logger) : IReportPipeline
{
    public async Task<ReportJobResult> RunAsync(ReportJobRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.OutputPath))
        {
            return Failed(Guid.Empty, new Error("reportforge.output_path_required", "Не задан путь для .docx."));
        }

        var source = TryReadSource(request);
        if (source.IsFailure)
        {
            return Failed(Guid.Empty, source.Error);
        }

        var template = await templates.EnsureDefaultTemplateAsync(ct).ConfigureAwait(false);
        var templateId = request.TemplateId ?? template.Id;
        var profile = await profiles.GetForTemplateAsync(templateId, ct).ConfigureAwait(false);

        var model = builder.Build(source.Value);
        model = model with
        {
            TitlePage = request.TitlePage,
            GenerateTableOfContents = request.GenerateTableOfContents,
            Blocks = ResolveImagePaths(model.Blocks, ResolveBaseDirectories(request)),
        };

        // Запись заводится до рендера — как журнал операций Архивариуса (ADR §16.27): упавшая
        // генерация тоже должна оставить след, а не исчезнуть бесследно.
        var job = new ReportJob
        {
            Id = Guid.NewGuid(),
            TemplateId = templateId,
            SubjectId = request.SubjectId,
            SourcePath = request.SourcePath ?? string.Empty,
            OutputPath = request.OutputPath,
            CreatedAt = DateTimeOffset.Now,
            Status = ReportJobStatus.Pending,
        };

        await jobs.AddAsync(job, ct).ConfigureAwait(false);

        try
        {
            EnsureOutputDirectory(request.OutputPath);

            await using (var output = fileSystem.Open(
                request.OutputPath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.None))
            {
                await renderer.RenderAsync(model, profile, output, ct).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = Describe(exception);
            logger.LogError(exception, "Не удалось сгенерировать отчёт в {OutputPath}", request.OutputPath);

            job.Status = ReportJobStatus.Failed;
            await jobs.UpdateAsync(job, ct).ConfigureAwait(false);

            return Failed(job.Id, error);
        }

        job.Status = ReportJobStatus.Rendered;
        await jobs.UpdateAsync(job, ct).ConfigureAwait(false);

        logger.LogInformation("Отчёт сгенерирован: {OutputPath}", request.OutputPath);
        return new ReportJobResult(job.Id, Succeeded: true, request.OutputPath, Error: null);
    }

    private Result<string> TryReadSource(ReportJobRequest request)
    {
        // Импортированный файл важнее набранного текста — так задокументирован контракт (§10.3).
        if (!string.IsNullOrWhiteSpace(request.SourcePath))
        {
            if (!fileSystem.FileExists(request.SourcePath))
            {
                return new Error("reportforge.source_missing", $"Файл не найден: {request.SourcePath}");
            }

            try
            {
                using var stream = fileSystem.OpenRead(request.SourcePath);
                using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);
                var text = reader.ReadToEnd();

                return string.IsNullOrWhiteSpace(text)
                    ? new Error("reportforge.empty_source", "Markdown-файл пуст.")
                    : text;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return new Error("reportforge.source_unreadable", $"Не удалось прочитать файл: {exception.Message}");
            }
        }

        return string.IsNullOrWhiteSpace(request.MarkdownSource)
            ? new Error("reportforge.empty_source", "Нечего генерировать: текст отчёта пуст.")
            : request.MarkdownSource!;
    }

    private void EnsureOutputDirectory(string outputPath)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));

        if (!string.IsNullOrEmpty(directory) && !fileSystem.DirectoryExists(directory))
        {
            fileSystem.CreateDirectory(directory);
        }
    }

    /// <summary>
    /// Точки отсчёта для относительных путей картинок, в порядке предпочтения. Рендерер о них не
    /// знает (контракт §10.3 принимает только модель), поэтому пути разворачиваются здесь.
    /// </summary>
    /// <remarks>
    /// Их две, потому что текст отчёта приходит из двух разных мест: у импортированного <c>.md</c>
    /// пути отсчитываются от его собственной папки, а у набранного в приложении – от учебной папки,
    /// ровно как их пишет редактор заметок. Импортированный файл при этом может ссылаться и на
    /// картинки заметок, поэтому побеждает не первая база по списку, а первая, где файл нашёлся.
    /// </remarks>
    private static IReadOnlyList<string> ResolveBaseDirectories(ReportJobRequest request)
    {
        var bases = new List<string>(2);

        if (!string.IsNullOrWhiteSpace(request.SourcePath)
            && Path.GetDirectoryName(Path.GetFullPath(request.SourcePath)) is { Length: > 0 } sourceFolder)
        {
            bases.Add(sourceFolder);
        }

        if (!string.IsNullOrWhiteSpace(request.ImageBaseDirectory))
        {
            bases.Add(request.ImageBaseDirectory);
        }

        return bases;
    }

    private static IReadOnlyList<IReportBlock> ResolveImagePaths(
        IReadOnlyList<IReportBlock> blocks, IReadOnlyList<string> baseDirectories)
    {
        if (baseDirectories.Count == 0)
        {
            return blocks;
        }

        return [.. blocks.Select(block => block switch
        {
            ImageBlock image => image with { PathOrBase64 = ToAbsolute(image.PathOrBase64, baseDirectories) },
            ListBlock list => list with
            {
                Items = [.. list.Items.Select(item => item with
                {
                    Blocks = ResolveImagePaths(item.Blocks, baseDirectories),
                })],
            },
            _ => block,
        })];
    }

    private static string ToAbsolute(string pathOrBase64, IReadOnlyList<string> baseDirectories)
    {
        if (string.IsNullOrWhiteSpace(pathOrBase64)
            || pathOrBase64.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            || Path.IsPathRooted(pathOrBase64)
            || (Uri.TryCreate(pathOrBase64, UriKind.Absolute, out var uri) && !uri.IsFile))
        {
            return pathOrBase64;
        }

        string? first = null;

        foreach (var baseDirectory in baseDirectories)
        {
            string candidate;
            try
            {
                candidate = Path.GetFullPath(Path.Combine(baseDirectory, pathOrBase64));
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (File.Exists(candidate))
            {
                return candidate;
            }

            first ??= candidate;
        }

        // Нигде не нашлось – отдаём первый разумный путь, чтобы в заглушке рендерера было видно,
        // что именно искали.
        return first ?? pathOrBase64;
    }

    private static Error Describe(Exception exception) => exception switch
    {
        UnauthorizedAccessException => new Error(
            "reportforge.output_denied",
            "Нет прав на запись файла по указанному пути."),
        IOException => new Error(
            "reportforge.output_locked",
            "Не удалось записать файл — возможно, он открыт в другой программе."),
        _ => new Error("reportforge.render_failed", $"Ошибка при сборке документа: {exception.Message}"),
    };

    private static ReportJobResult Failed(Guid jobId, Error error) => new(jobId, Succeeded: false, null, error);
}
