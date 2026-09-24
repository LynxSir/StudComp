namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Данные пользователя для титульного листа отчётов (ARCHITECTURE §10.5). Секция
/// <c>Rubrica:UserProfile</c>.
/// </summary>
/// <remarks>
/// Заполняется карточкой «Отчёты и титульный лист» в Настройках; ReportForge подставляет эти значения
/// в <c>TitlePageInfo</c>. Руководитель и тип работы сюда не входят — они меняются от отчёта к отчёту
/// и живут полями формы (ADR §16.37).
/// </remarks>
public sealed class UserProfileSettings
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:UserProfile";

    /// <summary>ФИО студента.</summary>
    public string? FullName { get; set; }

    /// <summary>Учебная группа.</summary>
    public string? Group { get; set; }

    /// <summary>Вуз.</summary>
    public string? University { get; set; }

    /// <summary>Факультет/институт.</summary>
    public string? Faculty { get; set; }

    /// <summary>Кафедра.</summary>
    public string? Department { get; set; }

    /// <summary>Город — нижняя строка титульного листа.</summary>
    public string? City { get; set; }
}
