namespace StudComp.Core.Domain;

/// <summary>
/// Состояние задачи генерации отчёта (ARCHITECTURE §7.1 <c>REPORT_JOB.Status</c>).
/// </summary>
public enum ReportJobStatus
{
    Pending = 0,
    Rendered = 1,
    Failed = 2,
}

/// <summary>
/// Одна выполненная (или запланированная) генерация <c>.docx</c>-отчёта
/// (ARCHITECTURE §7.1 <c>REPORT_JOB</c>, §10.3 <c>IReportPipeline</c>).
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// Не путать с <c>StudComp.Core.Abstractions.ReportForge.ReportJobResult</c> — это доменная запись учёта.
/// </summary>
public sealed class ReportJob
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    public Guid TemplateId { get; set; }

    /// <summary>Предмет, для которого сгенерирован отчёт, либо <see langword="null"/>.</summary>
    public Guid? SubjectId { get; set; }

    /// <summary>Путь к исходному markdown-файлу (либо пусто, если текст набирался в приложении).</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Путь к сгенерированному <c>.docx</c>.</summary>
    public string OutputPath { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public ReportJobStatus Status { get; set; }
}
