using StudComp.Core.Abstractions.Organizer;

namespace StudComp.Core.Domain;

/// <summary>
/// Учебный предмет — центральная сущность, к которой привязано почти всё: расписание, дедлайны,
/// оценки, правила архивариуса, отчёты (ARCHITECTURE §7.1 <c>SUBJECT</c>, §7.2).
/// POCO, ничего не знающий о персистентности; маппится из <c>StudComp.Data</c> (ADR §16.10).
/// </summary>
public sealed class Subject
{
    /// <summary>Первичный ключ, генерируется в коде, а не базой (ARCHITECTURE §7.2).</summary>
    public Guid Id { get; set; }

    /// <summary>Название предмета, например «Математический анализ».</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Короткий код/аббревиатура предмета, например «МатАн».</summary>
    public string Code { get; set; } = string.Empty;

    public SubjectAssessment Assessment { get; set; }

    /// <summary>
    /// Преподаватель по умолчанию (new_addons.md §5.2). <see langword="null"/> — не задан, поле
    /// пары остаётся свободным для ввода вручную на каждой паре, как раньше. Заданное значение
    /// подставляется в форму пары и блокируется там — «своя» пара по-прежнему может переопределить.
    /// </summary>
    public string? TeacherFullName { get; set; }

    /// <summary>
    /// Семестр, к которому относится предмет (new_addons.md §5). <see langword="null"/> — предмет
    /// не привязан ни к одному семестру и виден при любом выборе. При удалении семестра обнуляется
    /// (<c>SET NULL</c>) — предмет вместе с оценками и файлами переживает удаление семестра.
    /// </summary>
    public Guid? SemesterId { get; set; }

    /// <summary>
    /// Папка предмета — цель, куда архивариус раскладывает его файлы (ARCHITECTURE §8.1).
    /// С Phase 13.2 трактуется тремя способами: пусто — папка по названию предмета внутри учебной
    /// папки; относительное значение — имя подпапки в ней; абсолютный путь — «своя папка».
    /// Считать путь только через <see cref="SubjectFolder"/> (new_addons.md §4).
    /// </summary>
    public string FolderPath { get; set; } = string.Empty;

    /// <summary>Цвет предмета в интерфейсе, в формате <c>#RRGGBB</c> или <c>#AARRGGBB</c>.</summary>
    public string ColorHex { get; set; } = string.Empty;

    /// <summary>
    /// Система оценивания предмета — по ней прогноз мапит долю в итоговый балл (ARCHITECTURE §9.4).
    /// По умолчанию 5-балльная; шкалу не хардкодим — факультеты расходятся по методикам.
    /// </summary>
    public GradeScaleKind GradeScaleKind { get; set; } = GradeScaleKind.FivePoint;

    /// <summary>
    /// Верхняя граница произвольной шкалы. Используется только при <see cref="GradeScaleKind.Custom"/>;
    /// <see langword="null"/> — берётся ориентир 100.
    /// </summary>
    public decimal? GradeScaleMax { get; set; }

    /// <summary>
    /// Порог сдачи произвольной шкалы. Используется только при <see cref="GradeScaleKind.Custom"/>;
    /// <see langword="null"/> — берётся ориентир 60.
    /// </summary>
    public decimal? GradeScalePassThreshold { get; set; }

    /// <summary>
    /// Ключ keyed-стратегии прогноза (<see cref="StudComp.Core.Abstractions.Organizer.IGradeForecastStrategy.Name"/>).
    /// Пусто — стратегия по умолчанию (средневзвешенная). Задел под выбор стратегии в Phase 11.
    /// </summary>
    public string ForecastStrategyName { get; set; } = string.Empty;

    /// <summary>
    /// Профиль оформления отчётов предмета — ссылка на <see cref="ReportTemplate"/> (ARCHITECTURE §10.4).
    /// <see langword="null"/> — используется заводской ГОСТ-профиль. При удалении шаблона обнуляется
    /// (<c>SET NULL</c>). Задаётся в редакторе предмета, применяется на вкладке «Новый отчёт» (Phase 9).
    /// </summary>
    public Guid? ReportTemplateId { get; set; }
}
