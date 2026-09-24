using StudComp.Core.Domain;

namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Настройки Картотеки (new_addons.md §11). Секция конфигурации <c>Rubrica:Cards</c>.
/// </summary>
/// <remarks>
/// Значения читают <c>IReviewQueueService</c>, <c>IStudySessionService</c> и напоминание о
/// повторении; правит их одиннадцатый раздел модалки настроек.
/// </remarks>
public sealed class CardsOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:Cards";

    // ---- Повторение --------------------------------------------------------------------------

    /// <summary>Интервальное повторение включено: очередь дня, бейдж, напоминания.</summary>
    public bool ReviewEnabled { get; set; } = true;

    /// <summary>Сколько новых карточек показывать в день.</summary>
    public int NewCardsPerDay { get; set; } = 20;

    /// <summary>
    /// Сколько повторений в день. Без лимита после недельного перерыва пользователь открывает
    /// очередь на шестьсот карточек и закрывает приложение навсегда (new_addons.md §6.3).
    /// </summary>
    public int ReviewsPerDay { get; set; } = 200;

    /// <summary>Потолок интервала повторения в днях.</summary>
    public int MaxIntervalDays { get; set; } = 365;

    /// <summary>Порядок карточек в очереди дня.</summary>
    public StudyOrder QueueOrder { get; set; } = StudyOrder.DueFirst;

    /// <summary>Возвращать провалившиеся карточки в той же сессии, а не откладывать на день.</summary>
    public bool RelearnFailedInSameSession { get; set; } = true;

    /// <summary>Через сколько минут показывать провалившуюся карточку снова.</summary>
    public int RelearnMinutes { get; set; } = 10;

    /// <summary>Доля новых карточек в очереди дня.</summary>
    public double NewCardShare { get; set; } = 0.25;

    /// <summary>
    /// Час, с которого начинаются «учебные сутки». Ответ в час ночи должен считаться вчерашним
    /// вечером, иначе полуночная сессия обнуляет дневные лимиты посреди работы.
    /// </summary>
    public int DayRolloverHour { get; set; } = 4;

    /// <summary>Ключ keyed-стратегии планирования по умолчанию.</summary>
    public string SchedulerName { get; set; } = "sm2";

    // ---- Тренировка --------------------------------------------------------------------------

    /// <summary>Способ проверки, подставляемый в конструкторе сессии.</summary>
    public StudyCheckMode DefaultCheckMode { get; set; } = StudyCheckMode.SelfAssessment;

    /// <summary>Показывать оборот и спрашивать лицо («наоборот») по умолчанию.</summary>
    public bool ReverseByDefault { get; set; }

    /// <summary>Порог, с которого введённый ответ считается «почти верным».</summary>
    public double TypedAnswerThreshold { get; set; } = AnswerMatching.DefaultCloseThreshold;

    /// <summary>Писать на кнопках оценки, каким станет интервал.</summary>
    public bool ShowNextIntervalOnButtons { get; set; } = true;

    /// <summary>Влияет ли режим «Тренировка» на расписание повторений.</summary>
    public bool PracticeAffectsScheduling { get; set; }

    /// <summary>Сколько карточек предлагать в конструкторе пробного экзамена.</summary>
    public int DefaultExamCardCount { get; set; } = 40;

    /// <summary>Сколько раз стремиться показать каждую карточку в режиме аврала.</summary>
    public int CramMinShows { get; set; } = 3;

    // ---- Напоминания -------------------------------------------------------------------------

    /// <summary>Напоминать о повторении раз в день.</summary>
    public bool ReminderEnabled { get; set; }

    /// <summary>Во сколько напоминать. Тихие часы из <see cref="NotificationOptions"/> сдвигают показ.</summary>
    public TimeOnly ReminderTime { get; set; } = new(19, 0);

    // ---- Поиск --------------------------------------------------------------------------------

    /// <summary>Искать в теле (обороте) карточки, а не только в лицевой стороне и метках.</summary>
    public bool SearchInBody { get; set; } = true;

    /// <summary>Включать мягко удалённые карточки в результаты поиска (не только явный просмотр корзины).</summary>
    public bool SearchInTrash { get; set; }

    // ---- Компактный режим -----------------------------------------------------------------------

    /// <summary>
    /// Вызывать компактное окно-шпаргалку системной горячей клавишей из любого приложения
    /// (new_addons.md §8.6, §14 вопрос 3). Выключено по умолчанию: <c>RegisterHotKey</c> может
    /// молча конфликтовать с другим приложением.
    /// </summary>
    public bool GlobalHotkeyEnabled { get; set; }

    /// <summary>Комбинация клавиш в текстовом виде («Ctrl+Alt+K») — разбирается при регистрации.</summary>
    public string? GlobalHotkeyGesture { get; set; } = "Ctrl+Alt+K";
}
