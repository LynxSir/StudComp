namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Что делать при смене учебной папки (new_addons.md §7 §3). Автоперенос содержимого не делается —
/// это зона §14 (никогда не терять файл), поэтому доступны только «ничего» и «предложить открыть
/// обе папки».
/// </summary>
public enum RootChangeBehavior
{
    /// <summary>Ничего не делать — новые файлы просто будут раскладываться в новую папку.</summary>
    DoNothing = 0,

    /// <summary>Показать подсказку с обеими папками, чтобы пользователь перенёс файлы сам.</summary>
    OfferMove = 1,
}

/// <summary>
/// Учебная папка (Study Root) и скелет подпапок предмета. Секция конфигурации <c>Rubrica:Workspace</c>
/// (new_addons.md §1.1). Унифицирована с архивариусом: при первом запуске новой версии значение
/// <c>ArchivistOptions.ArchiveRootFolder</c> копируется сюда, если оно задано, а корень ещё нет
/// (<c>SettingsMigrations</c>).
/// </summary>
public sealed class WorkspaceOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:Workspace";

    /// <summary>
    /// Абсолютный путь к учебной папке. Пустая строка — папка ещё не выбрана (Дашборд и Хаб предмета
    /// показывают онбординг-CTA).
    /// </summary>
    public string StudyRootPath { get; set; } = string.Empty;

    /// <summary>
    /// Подпапки, создаваемые внутри папки предмета при её появлении. Список редактируется в настройках.
    /// </summary>
    public List<string> SubjectFolderTemplate { get; set; } =
        ["Лекции", "Практики", "Проекты", "Отчёты", "Исходники"];

    /// <summary>Поведение при смене корня учебной папки. По умолчанию — ничего не делать.</summary>
    public RootChangeBehavior OnRootChange { get; set; } = RootChangeBehavior.DoNothing;
}
