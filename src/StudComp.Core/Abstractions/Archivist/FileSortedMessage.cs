namespace StudComp.Core.Abstractions.Archivist;

/// <summary>
/// Архивариус успешно разложил файл (ARCHITECTURE §9.3, §11.4). Рассылается через <c>IMessenger</c> —
/// это единственная точка, где Архивариус и Органайзер соприкасаются семантически: Органайзер
/// подписывается и предлагает привязать файл к похожему дедлайну, прямого вызова между модулями нет.
/// </summary>
/// <remarks>
/// Запись лежит в <c>Core</c>, а не в модуле, ровно потому, что её читают оба модуля. Сам
/// <c>IMessenger</c> (CommunityToolkit) сюда не тянется — <c>Core</c> остаётся на голом BCL
/// (ADR §16.52).
/// </remarks>
/// <param name="FileRecordId">Запись учёта разложенного файла (<c>FILE_RECORD</c>).</param>
/// <param name="SubjectId">Предмет, в который уехал файл; <see langword="null"/> — правило без предмета.</param>
/// <param name="FinalPath">Абсолютный путь, по которому файл лежит после сортировки.</param>
/// <param name="FileName">Итоговое имя файла (уже после применения шаблона переименования).</param>
/// <param name="SortedAt">Момент завершения операции.</param>
public sealed record FileSortedMessage(
    Guid FileRecordId,
    Guid? SubjectId,
    string FinalPath,
    string FileName,
    DateTimeOffset SortedAt);
