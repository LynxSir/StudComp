using StudComp.Core.Domain;

namespace StudComp.Modules.Archivist.Services;

/// <summary>Один файл в предпросмотре правила: что с ним станет, если правило применить.</summary>
/// <param name="FileName">Имя файла в наблюдаемой папке.</param>
/// <param name="CurrentPath">Полный путь сейчас.</param>
/// <param name="ProjectedName">Имя после переименования по шаблону правила.</param>
/// <param name="ProjectedTargetDirectory">Папка, куда правило переместит файл.</param>
/// <param name="AlreadyHandledByOtherRule">
/// <see langword="true"/>, если какое-то включённое правило с бОльшим приоритетом заберёт файл раньше —
/// то есть на практике это правило до файла не доберётся.
/// </param>
public sealed record RulePreviewItem(
    string FileName,
    string CurrentPath,
    string ProjectedName,
    string ProjectedTargetDirectory,
    bool AlreadyHandledByOtherRule);

/// <summary>
/// Сухой прогон правила по текущему содержимому наблюдаемой папки (ARCHITECTURE §8.7, live-preview):
/// какие файлы попадут под правило и во что превратятся их имена. Файловую систему только читает —
/// ничего не перемещает.
/// </summary>
public interface IRulePreviewService
{
    Task<IReadOnlyList<RulePreviewItem>> PreviewAsync(ArchivistRule draft, CancellationToken ct = default);
}
