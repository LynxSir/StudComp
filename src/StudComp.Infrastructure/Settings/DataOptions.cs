namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Настройки хранения данных и ретеншна (new_addons.md §7 §9). Секция конфигурации
/// <c>Rubrica:Data</c>.
/// </summary>
public sealed class DataOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:Data";

    /// <summary>Сколько дней хранить записи ленты активности сверх <see cref="ActivityRetentionKeepCount"/>.</summary>
    public int ActivityRetentionDays { get; set; } = 90;

    /// <summary>Сколько последних записей ленты активности держать всегда, независимо от возраста.</summary>
    public int ActivityRetentionKeepCount { get; set; } = 500;

    /// <summary>
    /// Сколько дней хранить журнал файловых операций Архивариуса. Задел: прунинг журнала операций
    /// подключается отдельной задачей, значение пока только сохраняется.
    /// </summary>
    public int OperationLogRetentionDays { get; set; } = 180;

    /// <summary>
    /// Через сколько дней после «Удалить» карточка вычищается из корзины насовсем
    /// (new_addons.md §3.5, §11) — читает <c>CardTrashRetentionHostedService</c>.
    /// </summary>
    public int CardTrashRetentionDays { get; set; } = 30;

    /// <summary>
    /// Идентификатор линии резервных копий этой установки (Phase 13.8, new_addons.md §13.2) —
    /// генерируется лениво при первом <c>BackupAsync</c>, не при установке программы.
    /// <see cref="Guid.Empty"/> — бэкапов ещё не было. Это генерируемое состояние конкретной
    /// установки, а не настраиваемый дефолт — в отличие от остальных полей этого класса, сюда
    /// сознательно не дублируется значение в <c>appsettings.json</c>.
    /// </summary>
    public Guid BackupLineageId { get; set; } = Guid.Empty;

    /// <summary>Монотонный номер последней созданной или восстановленной резервной копии этой линии.</summary>
    public int BackupVersion { get; set; }
}
