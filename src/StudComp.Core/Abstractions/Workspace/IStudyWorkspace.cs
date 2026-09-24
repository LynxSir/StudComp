using StudComp.Core.Domain;

namespace StudComp.Core.Abstractions.Workspace;

/// <summary>
/// Учебная папка (Study Root) — одна папка на диске, внутри которой всё по учёбе разложено по
/// подпапкам-предметам (new_addons.md §1.1). Единая точка правды по путям предметов: ею пользуются
/// Органайзер (Хаб предмета), Архивариус (цель сортировки) и Отчёты (папка вывода).
/// </summary>
/// <remarks>
/// Контракт лежит в <c>Core</c>, потому что его потребители — три разных модуля, а прямые ссылки
/// между <c>Modules.*</c> запрещены (§5.1). Сигнатуры — только BCL и <see cref="Subject"/>, чтобы
/// <c>Core</c> оставался без внешних зависимостей (<c>CoreDependenciesTests</c>). Реализация
/// (<c>StudyWorkspace</c>) живёт в <c>Infrastructure</c> поверх <c>IFileSystem</c> +
/// <c>IOptionsMonitor&lt;WorkspaceOptions&gt;</c>.
/// </remarks>
public interface IStudyWorkspace
{
    /// <summary>Абсолютный путь к учебной папке. Пустая строка — папка ещё не выбрана.</summary>
    string StudyRootPath { get; }

    /// <summary>Выбрана ли учебная папка и существует ли она на диске.</summary>
    bool HasStudyRoot { get; }

    /// <summary>
    /// Папка предмета: <see cref="Subject.FolderPath"/>, если задан; иначе учебная папка + подпапка с
    /// санитизированным именем предмета. Пустая строка, если учебная папка не выбрана и своя не задана.
    /// </summary>
    string GetSubjectDirectory(Subject subject);

    /// <summary>
    /// Создаёт папку предмета и скелет подпапок из <c>WorkspaceOptions.SubjectFolderTemplate</c>.
    /// Ничего не удаляет и не перезаписывает (§14). No-op, если учебная папка не выбрана.
    /// </summary>
    void EnsureSubjectScaffold(Subject subject);

    /// <summary>
    /// Абсолютные пути файлов внутри папки предмета (или её подпапки <paramref name="subPath"/>).
    /// Пустой список, если папки нет.
    /// </summary>
    IReadOnlyList<string> EnumerateSubjectFiles(Subject subject, string? subPath = null);

    /// <summary>
    /// Путь <paramref name="absolutePath"/> относительно учебной папки, либо <see langword="null"/>,
    /// если он вне её (или папка не выбрана).
    /// </summary>
    string? ResolveRelative(string absolutePath);
}
