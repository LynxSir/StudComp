namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Настройки комбайна отчётов. Секция конфигурации <c>Rubrica:ReportForge</c> (ARCHITECTURE §10.1, §11.2).
/// </summary>
public sealed class ReportForgeOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:ReportForge";

    /// <summary>
    /// Куда складывать сгенерированные <c>.docx</c>. Пусто — <c>RubricaPaths.ReportsDirectory</c>
    /// (<c>%LocalAppData%\Rubrica\Reports</c>).
    /// </summary>
    public string OutputFolder { get; set; } = string.Empty;

    /// <summary>Открывать ли готовый файл системным приложением сразу после генерации.</summary>
    public bool OpenAfterGenerate { get; set; } = true;

    /// <summary>Тип работы, подставляемый в форму нового отчёта по умолчанию (поле титульного листа).</summary>
    public string DefaultWorkType { get; set; } = "Отчёт по лабораторной работе";

    /// <summary>
    /// Предлагать подпапку «Отчёты» внутри папки предмета как путь вывода, когда выбран предмет и
    /// задана учебная папка (new_addons.md §7 §7). Иначе используется <see cref="OutputFolder"/>.
    /// </summary>
    public bool PreferSubjectSubfolder { get; set; } = true;

    /// <summary>
    /// Профиль оформления по умолчанию для формы нового отчёта (<see langword="null"/> — заводской).
    /// Учитывается, если у выбранного предмета нет своего профиля.
    /// </summary>
    public Guid? DefaultProfileId { get; set; }
}
