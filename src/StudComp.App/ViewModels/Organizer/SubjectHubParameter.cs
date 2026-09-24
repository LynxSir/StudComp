namespace StudComp.ViewModels.Organizer;

/// <summary>Вкладки Хаба предмета (new_addons.md §5).</summary>
public enum SubjectHubTab
{
    Overview = 0,
    Files = 1,
    Notes = 2,
    Schedule = 3,
    Grades = 4,

    /// <summary>Карточки предмета (new_addons.md §2.3).</summary>
    Cards = 5,

    /// <summary>Дедлайны предмета (new_addons.md §7.2).</summary>
    Deadlines = 6,
}

/// <summary>
/// Перевод вкладки в позицию <c>TabItem</c> внутри <c>SubjectHubPage.xaml</c>.
/// </summary>
/// <remarks>
/// Порядок вкладок на экране (Обзор · Файлы · Расписание · Заметки · Оценки · Карточки) не совпадает
/// с порядком значений enum, и прямой каст <c>(int)tab</c> открывал не ту вкладку: «Заметки» вели на
/// «Расписание» и наоборот. Чиним именно картой, а не перестановкой значений — перестановка тихо
/// сломала бы уже написанные дип-линки (new_addons.md §2.3).
/// </remarks>
public static class SubjectHubTabs
{
    /// <summary>Индекс вкладки на экране. Неизвестное значение открывает «Обзор».</summary>
    public static int ToIndex(SubjectHubTab tab) => tab switch
    {
        SubjectHubTab.Files => 1,
        SubjectHubTab.Schedule => 2,
        SubjectHubTab.Notes => 3,
        SubjectHubTab.Grades => 4,
        SubjectHubTab.Cards => 5,
        SubjectHubTab.Deadlines => 6,
        _ => 0,
    };
}

/// <summary>
/// Параметр навигации в Хаб предмета. <see langword="record"/> — чтобы навигация могла сравнить
/// «тот же предмет или другой» структурно (<c>NavigationService</c>).
/// </summary>
/// <param name="SubjectId">Предмет, который открываем.</param>
/// <param name="HighlightScheduleEntryId">Пара, с которой пришли, — Хаб её подсветит.</param>
/// <param name="Tab">Вкладка, на которой открыть Хаб.</param>
/// <param name="NoteId">Заметка, которую сразу открыть в редакторе.</param>
public sealed record SubjectHubParameter(
    Guid SubjectId,
    Guid? HighlightScheduleEntryId = null,
    SubjectHubTab Tab = SubjectHubTab.Overview,
    Guid? NoteId = null);
