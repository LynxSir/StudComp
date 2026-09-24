namespace StudComp.Core.Abstractions.ReportForge;

/// <summary>
/// Пишет <see cref="ReportDocumentModel"/> в поток <c>.docx</c> по профилю стиля (ARCHITECTURE §10.3).
/// Результат обязан проходить схемную валидацию OpenXML (ARCHITECTURE §12).
/// </summary>
public interface IGostDocxRenderer
{
    /// <summary>Рендерит <paramref name="model"/> по стилю <paramref name="profile"/> в <paramref name="output"/>.</summary>
    Task RenderAsync(ReportDocumentModel model, GostStyleProfile profile, Stream output, CancellationToken ct);
}
