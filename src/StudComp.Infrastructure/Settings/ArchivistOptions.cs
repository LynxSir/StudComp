namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Настройки Архивариуса. Секция <c>Rubrica:Archivist</c> (ARCHITECTURE §8.7, §11.2).
/// </summary>
public sealed class ArchivistOptions
{
    public const string SectionName = "Rubrica:Archivist";

    /// <summary>Включено ли наблюдение за папкой. По умолчанию выключено — пока пользователь не выбрал папку.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Наблюдать учебную папку (new_addons.md §4): её корень работает «входящими» — то, что туда
    /// положили, раскладывается по папкам предметов. Включено по умолчанию, чтобы указанную один раз
    /// учебную папку не приходилось дублировать в списке ниже.
    /// </summary>
    /// <remarks>
    /// Наблюдение нерекурсивное (<c>IncludeSubdirectories = false</c>), поэтому уже разложенные по
    /// подпапкам предметов файлы под наблюдение не попадают и повторно не обрабатываются.
    /// </remarks>
    public bool WatchStudyRoot { get; set; } = true;

    /// <summary>
    /// Дополнительные наблюдаемые папки вне учебной (обычно «Загрузки») — на каждую поднимается свой
    /// <c>FileSystemWatcher</c>. Правило может быть привязано к конкретной папке через
    /// <c>ArchivistRule.WatchedFolder</c> (ADR §16.54).
    /// </summary>
    public List<string> WatchedFolders { get; set; } = [];

    /// <summary>
    /// Корень архива — <b>переопределение</b> учебной папки: задан, значит файлы ложатся в
    /// <c>ArchiveRootFolder\Имя предмета</c>. Пустая строка (обычный случай с Phase 13.2) — цель
    /// считается от учебной папки, ровно как её показывает Хаб предмета.
    /// </summary>
    public string ArchiveRootFolder { get; set; } = string.Empty;

    /// <summary>
    /// Игнорируемые имена файлов, простые маски (ARCHITECTURE §8.4 п.1): временные файлы браузеров и
    /// офисных пакетов, служебные файлы Windows.
    /// </summary>
    public List<string> IgnoredPatterns { get; set; } =
    [
        "*.tmp",
        "*.crdownload",
        "*.part",
        "*.partial",
        "*.download",
        "~$*",
        "*.lnk",
        "desktop.ini",
        "thumbs.db",
    ];

    /// <summary>Файлы мельче этого размера считаются «ещё пишется», если только что созданы (§8.4 п.1).</summary>
    public int MinFileSizeBytes { get; set; } = 1024;

    /// <summary>Возраст, младше которого мелкий файл не трогаем (§8.4 п.1).</summary>
    public int MinFileAgeSeconds { get; set; } = 2;

    /// <summary>Сколько всего ждать стабилизации файла, прежде чем сдаться (§8.4 п.2).</summary>
    public int StabilityTimeoutSeconds { get; set; } = 60;

    /// <summary>Интервал между двумя замерами размера при проверке стабильности (§8.4 п.2).</summary>
    public int StabilityProbeIntervalMs { get; set; } = 500;

    /// <summary>Потолок времени на матчинг одного regex-правила — защита от катастрофического бэктрекинга (§8.4 п.3).</summary>
    public int RegexMatchTimeoutMs { get; set; } = 200;

    /// <summary>Базовая пауза перед первым ретраем залоченного файла; дальше растёт экспоненциально (§8.6).</summary>
    public int DeferredRetryInitialSeconds { get; set; } = 30;

    /// <summary>Сколько раз пробовать разобрать залоченный файл, прежде чем пометить его проблемным (§8.6).</summary>
    public int DeferredRetryMaxAttempts { get; set; } = 6;

    /// <summary>Как часто фоновый цикл проверяет, не пора ли повторить отложенные файлы (§8.6).</summary>
    public int DeferredRetryPollSeconds { get; set; } = 30;

    /// <summary>Верхняя граница паузы между ретраями залоченного файла (§8.6).</summary>
    public int DeferredRetryMaxIntervalSeconds { get; set; } = 1800;

    /// <summary>
    /// Период полного скана наблюдаемых папок и сверки со <c>FileRecord</c> (ARCHITECTURE §8.2, §8.5):
    /// страховка на случай, когда <c>FileSystemWatcher</c> потерял события при массовом копировании.
    /// </summary>
    public int ReconciliationIntervalMinutes { get; set; } = 10;

    /// <summary>
    /// Возраст, после которого запись журнала в статусе <c>Planned</c> считается зависшей (след падения
    /// между журналом и <c>File.Move</c>) и разбирается reconciliation. Слишком малое значение заставит
    /// сверку вмешиваться в живую операцию (§8.6).
    /// </summary>
    public int ReconciliationStalePlannedMinutes { get; set; } = 5;

    /// <summary>
    /// Период проверки живости наблюдателей: папка на месте и <c>EnableRaisingEvents</c> взведён.
    /// Сбойный watcher пересоздаётся (ARCHITECTURE §8.5).
    /// </summary>
    public int WatcherHealthCheckSeconds { get; set; } = 60;

    /// <summary>
    /// Пропускать нематериализованные облачные файлы (OneDrive placeholder'ы, ARCHITECTURE §8.5).
    /// Иначе чтение содержимого заставит облако скачать файл целиком.
    /// </summary>
    public bool SkipCloudPlaceholders { get; set; } = true;

    /// <summary>Предлагать привязку отсортированного файла к похожему дедлайну (ARCHITECTURE §9.3).</summary>
    public bool DeadlineLinkSuggestionEnabled { get; set; } = true;

    /// <summary>Порог похожести «имя файла ↔ дедлайн» в диапазоне [0, 1], ниже которого не предлагаем (§9.3).</summary>
    public double DeadlineLinkMinScore { get; set; } = 0.45;

    /// <summary>Окно ±дней вокруг срока дедлайна, в котором файл считается его кандидатом (§9.3).</summary>
    public int DeadlineLinkWindowDays { get; set; } = 21;

    /// <summary>
    /// Предлагать предметы-кандидаты в «Неразобранном» по похожести имени файла (new_addons.md §4,
    /// ARCHITECTURE §8.4 п.7).
    /// </summary>
    public bool UnsortedSuggestionsEnabled { get; set; } = true;

    /// <summary>Порог похожести «имя файла ↔ предмет» в [0, 1], ниже которого подсказка не показывается.</summary>
    public double UnsortedSuggestionMinScore { get; set; } = 0.35;

    /// <summary>Максимум предметов-кандидатов на одну строку «Неразобранного» (DoD: «1–3 предмета»).</summary>
    public int UnsortedSuggestionMaxCandidates { get; set; } = 3;
}
