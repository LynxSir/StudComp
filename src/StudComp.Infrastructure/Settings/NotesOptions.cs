namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Настройки редактора заметок. Секция конфигурации <c>Rubrica:Notes</c>.
/// </summary>
/// <remarks>
/// В <c>appsettings.json</c> не дублируется намеренно: это не заводской дефолт, а состояние
/// конкретной установки — как линия резервных копий в <see cref="DataOptions"/>.
/// </remarks>
public sealed class NotesOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:Notes";

    /// <summary>
    /// Откуда в прошлый раз брали картинку — отдельно по каждому предмету (ключ: его идентификатор
    /// в формате <c>N</c>). Первый раз диалог открывается в папке предмета, дальше — там, где
    /// пользователь был в последний раз, чтобы не искать путь заново.
    /// </summary>
    public Dictionary<string, string> LastImageFolders { get; set; } = [];
}
