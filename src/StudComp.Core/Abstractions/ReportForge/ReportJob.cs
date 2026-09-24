using StudComp.Core.Common;

namespace StudComp.Core.Abstractions.ReportForge;

/// <summary>
/// Заявка на генерацию отчёта, отдаваемая в <see cref="IReportPipeline"/> (ARCHITECTURE §7.1 <c>REPORT_JOB</c>, §10.3).
/// </summary>
/// <param name="TemplateId">Шаблон, чей профиль стиля берётся; <see langword="null"/> — профиль ГОСТ по умолчанию.</param>
/// <param name="SubjectId">Предмет, к которому относится отчёт; <see langword="null"/> для отчёта вне предмета.</param>
/// <param name="MarkdownSource">Текст, набранный в приложении. Игнорируется, если задан <paramref name="SourcePath"/>.</param>
/// <param name="SourcePath">Путь импортированного <c>.md</c>-файла; <see langword="null"/>, если текст пришёл в <paramref name="MarkdownSource"/>.</param>
/// <param name="OutputPath">Абсолютный путь <c>.docx</c>, который нужно создать.</param>
/// <param name="TitlePage">Данные титульного листа либо <see langword="null"/>, чтобы его не делать.</param>
/// <param name="GenerateTableOfContents">Вставлять ли поле оглавления.</param>
public record ReportJobRequest(
    Guid? TemplateId,
    Guid? SubjectId,
    string? MarkdownSource,
    string? SourcePath,
    string OutputPath,
    TitlePageInfo? TitlePage,
    bool GenerateTableOfContents);

/// <summary>
/// Чем закончилась генерация отчёта (ARCHITECTURE §10.3).
/// </summary>
/// <param name="ReportJobId">Id сохранённой записи <c>ReportJob</c>.</param>
/// <param name="Succeeded">Записан ли документ.</param>
/// <param name="OutputPath">Путь готового <c>.docx</c>; при ошибке <see langword="null"/>.</param>
/// <param name="Error">Описание ошибки, когда <paramref name="Succeeded"/> равно <see langword="false"/>.</param>
public record ReportJobResult(
    Guid ReportJobId,
    bool Succeeded,
    string? OutputPath,
    Error? Error);
