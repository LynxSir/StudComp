namespace StudComp.Core.Abstractions.ReportForge;

/// <summary>
/// Генерация отчёта целиком: прочитать источник, собрать модель, подобрать профиль стиля, отрендерить
/// <c>.docx</c> и записать <c>ReportJob</c> (ARCHITECTURE §10.2, §10.3).
/// Единственная точка входа для ViewModel'ей — напрямую к builder'у и рендереру они не ходят.
/// </summary>
public interface IReportPipeline
{
    Task<ReportJobResult> RunAsync(ReportJobRequest request, CancellationToken ct);
}
