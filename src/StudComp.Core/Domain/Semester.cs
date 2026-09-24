namespace StudComp.Core.Domain;

/// <summary>
/// Учебный семестр — источник дат для чётности недели и расчёта часов (new_addons.md §5).
/// До Phase 12.3 эти данные жили двумя настройками в <c>usersettings.json</c>; семестров может быть
/// несколько, активный ровно один (<see cref="IsActive"/>).
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class Semester
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    /// <summary>Человеческое название, например «Осень 2026».</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Номер курса, к которому относится семестр.</summary>
    public int CourseNumber { get; set; } = 1;

    /// <summary>Дата начала — точка отсчёта номера и чётности недели.</summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// Дата окончания. <see langword="null"/> — неизвестна (так приходит семестр, перенесённый из
    /// старых настроек, где её просто не было). Без неё нельзя посчитать часы за семестр, поэтому
    /// UI честно просит её задать, а не подставляет выдуманный срок.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Считать ли неделю, содержащую <see cref="StartDate"/>, нечётной («числителем»). Дальше
    /// числитель и знаменатель чередуются строго по очереди до конца семестра — это единственный
    /// вход, от которого считается чётность любой даты (<see cref="WeekParityCalculator"/>).
    /// </summary>
    public bool FirstWeekIsOdd { get; set; } = true;

    /// <summary>Активный семестр — тот, по которому фильтруются списки Органайзера. Он ровно один.</summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Сетка звонков семестра в JSON (<see cref="PairSlots"/>). Хранится колонкой-значением, а не
    /// отдельной таблицей: это value-object, целиком принадлежащий одному семестру, без собственных
    /// запросов и связей (прецедент — <see cref="ReportTemplate.StyleProfileJson"/>).
    /// </summary>
    public string PairSlotsJson { get; set; } = "[]";

    /// <summary>
    /// Обычный перерыв между парами, минуты (new_addons.md §5.1) — используется, когда время новой
    /// пары подсказывается автоматически как «конец предыдущей + перерыв». Дефолт 10 мин зеркалит
    /// уже существующий генератор сетки звонков в редакторе семестра.
    /// </summary>
    public int DefaultBreakMinutes { get; set; } = 10;

    /// <summary>
    /// Начало обеденного перерыва. <see langword="null"/> — не задан, подсказка времени пары его не
    /// учитывает. Заполняется вместе с <see cref="LunchBreakEnd"/> — оба или ни одного.
    /// </summary>
    public TimeOnly? LunchBreakStart { get; set; }

    /// <summary>Конец обеденного перерыва (new_addons.md §5.1).</summary>
    public TimeOnly? LunchBreakEnd { get; set; }
}
