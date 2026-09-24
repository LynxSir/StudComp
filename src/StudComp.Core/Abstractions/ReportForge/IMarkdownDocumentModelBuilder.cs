namespace StudComp.Core.Abstractions.ReportForge;

/// <summary>
/// Превращает Markdown в <see cref="ReportDocumentModel"/>, не зависящий от рендерера (ARCHITECTURE §10.2, §10.3).
/// Ничего не знает ни о ГОСТ, ни об OpenXML — ради этого промежуточная модель и заводилась (ADR §16.4).
/// </summary>
public interface IMarkdownDocumentModelBuilder
{
    /// <summary>Разбирает <paramref name="markdown"/> в модель документа. Синхронный: разбор целиком в памяти.</summary>
    ReportDocumentModel Build(string markdown);
}
