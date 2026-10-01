using System.Collections.Concurrent;
using System.IO;
using Microsoft.Extensions.Logging;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Domain;
using StudComp.Infrastructure.FileSystem;

namespace StudComp.Modules.Organizer.Services;

/// <summary>
/// Двухэтапная корзина картинок заметки (new_addons.md §12). Картинка, на которую текст больше не
/// ссылается, мгновенно уезжает в скрытую папку внутри учебной папки — это обратимо, поэтому
/// <c>Ctrl+Z</c> возвращает и ссылку, и файл. В «Корзину» Windows она попадает позже, когда стек
/// отмены заметки всё равно уже недостижим.
/// </summary>
/// <remarks>
/// Ни один метод не бросает исключений: потеря картинки не должна ронять редактор. Худший исход —
/// файл остался там, где лежал, о чём будет запись в журнале.
/// </remarks>
public interface INoteImageTrash
{
    /// <summary>
    /// Убрать картинку из папки предмета в скрытую корзину. Трогаются только файлы внутри папки
    /// картинок самой заметки: путь, введённый руками на чужой файл, остаётся на месте.
    /// </summary>
    Task<bool> StageAsync(Guid noteId, string relativeImagePath, CancellationToken ct = default);

    /// <summary>Вернуть картинку на место — это и есть <c>Ctrl+Z</c> для файла.</summary>
    Task<bool> RestoreAsync(Guid noteId, string relativeImagePath, CancellationToken ct = default);

    /// <summary>
    /// Стек отмены заметки закончился: всё, что осталось в её корзине, уходит в «Корзину» Windows.
    /// Возвращает число отправленных файлов.
    /// </summary>
    Task<int> CommitAsync(Guid noteId, CancellationToken ct = default);

    /// <summary>
    /// Заметка открывается и ссылается на картинку, которой нет на диске, но которая лежит в её
    /// корзине — вернуть. Закрывает падение приложения между удалением и сохранением текста.
    /// </summary>
    Task<int> RestoreReferencedAsync(Guid noteId, string? markdown, CancellationToken ct = default);

    /// <summary>Осиротевшее после падения: сессии старше порога уходят в «Корзину» Windows.</summary>
    Task<int> SweepOrphansAsync(TimeSpan olderThan, CancellationToken ct = default);
}

/// <inheritdoc cref="INoteImageTrash"/>
internal sealed class NoteImageTrash(
    INoteService notes,
    IStudyWorkspace workspace,
    IFileSystem fileSystem,
    IRecycleBinPort recycleBin,
    ILogger<NoteImageTrash> logger) : INoteImageTrash
{
    /// <summary>Сессия правки на заметку: одна папка в корзине на всё время, пока заметка открыта.</summary>
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _sessions = new();

    public async Task<bool> StageAsync(Guid noteId, string relativeImagePath, CancellationToken ct = default)
    {
        try
        {
            if (await ResolveOwnedFileAsync(noteId, relativeImagePath, ct).ConfigureAwait(false) is not { } absolute)
            {
                return false;
            }

            if (!fileSystem.FileExists(absolute))
            {
                // Файла уже нет — убирать нечего, повторный вызов безвреден.
                return false;
            }

            EnsureHiddenServiceFolder();

            var stamp = _sessions.GetOrAdd(noteId, _ => DateTimeOffset.UtcNow);
            if (NoteImageTrashLayout.StagePathFor(workspace.StudyRootPath, noteId, stamp, relativeImagePath)
                is not { } staged)
            {
                return false;
            }

            MoveAside(absolute, staged);
            MoveSidecarAside(absolute, staged);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(exception, "Не удалось убрать картинку заметки {NoteId}: {Path}", noteId, relativeImagePath);
            return false;
        }
    }

    public async Task<bool> RestoreAsync(Guid noteId, string relativeImagePath, CancellationToken ct = default)
    {
        try
        {
            if (await ResolveOwnedFileAsync(noteId, relativeImagePath, ct).ConfigureAwait(false) is not { } absolute)
            {
                return false;
            }

            if (fileSystem.FileExists(absolute))
            {
                // Файл уже на месте: возвращать нечего.
                return false;
            }

            // Свежая сессия вероятнее: перебираем от новых к старым.
            foreach (var stamp in SessionsOf(noteId).OrderByDescending(x => x))
            {
                if (NoteImageTrashLayout.StagePathFor(workspace.StudyRootPath, noteId, stamp, relativeImagePath)
                    is not { } staged || !fileSystem.FileExists(staged))
                {
                    continue;
                }

                MoveAside(staged, absolute);
                MoveSidecarAside(staged, absolute);
                return true;
            }

            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.LogWarning(exception, "Не удалось вернуть картинку заметки {NoteId}: {Path}", noteId, relativeImagePath);
            return false;
        }
    }

    public Task<int> CommitAsync(Guid noteId, CancellationToken ct = default)
    {
        _sessions.TryRemove(noteId, out _);

        if (!workspace.HasStudyRoot)
        {
            return Task.FromResult(0);
        }

        var sent = 0;
        foreach (var session in SessionDirectories(NoteImageTrashLayout.NoteDirectory(workspace.StudyRootPath, noteId)))
        {
            ct.ThrowIfCancellationRequested();
            sent += SendSessionToRecycleBin(session);
        }

        recycleBin.TryRemoveEmptyDirectory(NoteImageTrashLayout.NoteDirectory(workspace.StudyRootPath, noteId));
        return Task.FromResult(sent);
    }

    public async Task<int> RestoreReferencedAsync(Guid noteId, string? markdown, CancellationToken ct = default)
    {
        if (!workspace.HasStudyRoot || !MarkdownImageReconciliation.MightContainImages(markdown))
        {
            return 0;
        }

        var restored = 0;
        foreach (var path in MarkdownImageReconciliation.CountReferences(markdown).Keys)
        {
            ct.ThrowIfCancellationRequested();
            if (await RestoreAsync(noteId, path, ct).ConfigureAwait(false))
            {
                restored++;
            }
        }

        if (restored > 0)
        {
            logger.LogInformation("Возвращено картинок заметки {NoteId} из корзины: {Count}", noteId, restored);
        }

        return restored;
    }

    public Task<int> SweepOrphansAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        if (!workspace.HasStudyRoot)
        {
            return Task.FromResult(0);
        }

        var root = NoteImageTrashLayout.TrashRoot(workspace.StudyRootPath);
        if (!fileSystem.DirectoryExists(root))
        {
            return Task.FromResult(0);
        }

        var threshold = DateTimeOffset.UtcNow - olderThan;
        var sent = 0;

        foreach (var noteDirectory in Enumerate(() => fileSystem.EnumerateDirectories(root)))
        {
            ct.ThrowIfCancellationRequested();

            foreach (var session in SessionDirectories(noteDirectory))
            {
                // Метка времени живёт в имени папки; разобрать её можно по любому файлу внутри.
                if (!TryStampOf(session, out var stamp) || stamp > threshold)
                {
                    continue;
                }

                sent += SendSessionToRecycleBin(session);
            }

            recycleBin.TryRemoveEmptyDirectory(noteDirectory);
        }

        if (sent > 0)
        {
            logger.LogInformation("Отправлено в «Корзину» осиротевших картинок заметок: {Count}", sent);
        }

        return Task.FromResult(sent);
    }

    /// <summary>
    /// Абсолютный путь картинки, если она действительно принадлежит этой заметке. Всё остальное
    /// (чужая папка, рабочий стол, путь, набранный руками) не наше дело и не трогается.
    /// </summary>
    private async Task<string?> ResolveOwnedFileAsync(Guid noteId, string relativeImagePath, CancellationToken ct)
    {
        if (!workspace.HasStudyRoot || string.IsNullOrWhiteSpace(relativeImagePath)
            || Path.IsPathRooted(relativeImagePath))
        {
            return null;
        }

        var storage = await notes.GetStorageAsync(noteId, ct).ConfigureAwait(false);
        if (storage is null)
        {
            return null;
        }

        var absolute = Path.GetFullPath(Path.Combine(workspace.StudyRootPath, relativeImagePath));
        var images = Path.GetFullPath(storage.ImagesDirectory);
        if (!images.EndsWith(Path.DirectorySeparatorChar))
        {
            images += Path.DirectorySeparatorChar;
        }

        return absolute.StartsWith(images, StringComparison.OrdinalIgnoreCase) ? absolute : null;
    }

    /// <summary>
    /// Служебная папка помечается скрытой: пользователь просил не засорять папки предметов, а точка
    /// в начале имени в проводнике Windows ничего не скрывает. Атрибут ставится напрямую, минуя
    /// <see cref="IFileSystem"/> — операция ничего не разрушает и потому не ослабляет гарантию
    /// «в абстракции нет удаления» (ADR §16.30).
    /// </summary>
    private void EnsureHiddenServiceFolder()
    {
        var service = Path.Combine(workspace.StudyRootPath, NoteImageTrashLayout.ServiceFolder);
        var existed = fileSystem.DirectoryExists(service);
        fileSystem.CreateDirectory(service);

        if (existed)
        {
            return;
        }

        try
        {
            var info = new DirectoryInfo(service);
            info.Attributes |= FileAttributes.Hidden;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Косметика: не спрятали — и ладно.
        }
    }

    /// <summary>Перемещение в пределах тома — мгновенное, поэтому уборка картинки ничего не стоит.</summary>
    private void MoveAside(string from, string to)
    {
        var directory = Path.GetDirectoryName(to);
        if (!string.IsNullOrEmpty(directory))
        {
            fileSystem.CreateDirectory(directory);
        }

        try
        {
            fileSystem.Move(from, to);
        }
        catch (IOException) when (fileSystem.FileExists(to))
        {
            // Цель занята прошлой попыткой — считаем, что файл уже там, где нужно.
        }
    }

    /// <summary>
    /// Рядом с рисунком лежит его векторная копия <c>.json</c>. Без неё восстановленный рисунок
    /// молча терял бы возможность правки, поэтому она ездит вместе с картинкой.
    /// </summary>
    private void MoveSidecarAside(string from, string to)
    {
        var sidecar = Path.ChangeExtension(from, ".json");
        if (!string.Equals(sidecar, from, StringComparison.OrdinalIgnoreCase) && fileSystem.FileExists(sidecar))
        {
            MoveAside(sidecar, Path.ChangeExtension(to, ".json"));
        }
    }

    private int SendSessionToRecycleBin(string session)
    {
        var sent = 0;

        foreach (var file in Enumerate(() => fileSystem.EnumerateFiles(session, "*", SearchOption.AllDirectories)))
        {
            if (recycleBin.TrySendToRecycleBin(file))
            {
                sent++;
            }
        }

        // Пустые папки убираем снизу вверх; непустую реализация порта не тронет по построению.
        foreach (var directory in Enumerate(() => fileSystem.EnumerateDirectories(session, "*", SearchOption.AllDirectories))
            .OrderByDescending(x => x.Length))
        {
            recycleBin.TryRemoveEmptyDirectory(directory);
        }

        recycleBin.TryRemoveEmptyDirectory(session);
        return sent;
    }

    private IEnumerable<string> SessionDirectories(string noteDirectory) =>
        fileSystem.DirectoryExists(noteDirectory)
            ? Enumerate(() => fileSystem.EnumerateDirectories(noteDirectory)).ToList()
            : [];

    private IEnumerable<DateTimeOffset> SessionsOf(Guid noteId)
    {
        if (!workspace.HasStudyRoot)
        {
            return [];
        }

        var stamps = new List<DateTimeOffset>();
        foreach (var session in SessionDirectories(NoteImageTrashLayout.NoteDirectory(workspace.StudyRootPath, noteId)))
        {
            if (TryStampOf(session, out var stamp))
            {
                stamps.Add(stamp);
            }
        }

        return stamps;
    }

    /// <summary>Метка времени сессии — из имени её папки, через разбор любого пути внутри.</summary>
    private bool TryStampOf(string session, out DateTimeOffset stamp) =>
        NoteImageTrashLayout.TryParse(
            workspace.StudyRootPath, Path.Combine(session, "x"), out _, out stamp, out _);

    /// <summary>Перечисление каталога может упасть на ходу — для нас это просто «ничего нет».</summary>
    private static IReadOnlyList<string> Enumerate(Func<IEnumerable<string>> source)
    {
        try
        {
            return [.. source()];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
