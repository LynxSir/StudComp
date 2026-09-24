using Microsoft.EntityFrameworkCore;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>Доступ к записям учёта файлов архивариуса (ARCHITECTURE §7.3, §8.2).</summary>
public interface IFileRecordRepository
{
    /// <summary>Записи с данным хэшем содержимого — для обнаружения дублей (ARCHITECTURE §7.2, §8.4 п.5).</summary>
    Task<IReadOnlyList<FileRecord>> GetByContentHashAsync(string contentHash, CancellationToken ct = default);

    /// <summary>
    /// Записи в заданном состоянии, свежие сверху. <see cref="FileRecordStatus.Detected"/> —
    /// это содержимое раздела «Неразобранное» (ARCHITECTURE §8.4 п.7).
    /// </summary>
    Task<IReadOnlyList<FileRecord>> GetByStatusAsync(FileRecordStatus status, CancellationToken ct = default);

    /// <summary>
    /// Записи в любом из перечисленных состояний, свежие сверху — список раздела «Неразобранное»
    /// собирается из <c>Detected</c> и <c>Quarantined</c> (ARCHITECTURE §8.4 п.7, §8.6).
    /// </summary>
    Task<IReadOnlyList<FileRecord>> GetByStatusesAsync(
        IReadOnlyList<FileRecordStatus> statuses,
        CancellationToken ct = default);

    /// <summary>
    /// Запись по пути, на котором файл был обнаружен — защита от повторной обработки одного и того же
    /// файла при пересканировании папки (ARCHITECTURE §7.2).
    /// </summary>
    Task<FileRecord?> GetByOriginalPathAsync(string originalPath, CancellationToken ct = default);

    /// <summary>
    /// Все записи, обнаруженные внутри указанной папки. Нужна reconciliation-сверке «папка ↔ БД»:
    /// один запрос на папку вместо запроса на каждый из сотен файлов (ARCHITECTURE §8.2, Phase 10).
    /// </summary>
    Task<IReadOnlyList<FileRecord>> GetByOriginalPathPrefixAsync(
        string directoryPath,
        CancellationToken ct = default);

    Task<FileRecord?> GetByIdAsync(Guid id, CancellationToken ct = default);

    Task AddAsync(FileRecord record, CancellationToken ct = default);

    Task UpdateAsync(FileRecord record, CancellationToken ct = default);
}

internal sealed class FileRecordRepository(IDbContextFactory<StudCompDbContext> contextFactory)
    : RepositoryBase(contextFactory), IFileRecordRepository
{
    public async Task<IReadOnlyList<FileRecord>> GetByContentHashAsync(string contentHash, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.FileRecords
            .AsNoTracking()
            .Where(x => x.ContentHash == contentHash)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FileRecord>> GetByStatusAsync(FileRecordStatus status, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.FileRecords
            .AsNoTracking()
            .Where(x => x.Status == status)
            .OrderByDescending(x => x.DetectedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FileRecord>> GetByStatusesAsync(
        IReadOnlyList<FileRecordStatus> statuses,
        CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.FileRecords
            .AsNoTracking()
            .Where(x => statuses.Contains(x.Status))
            .OrderByDescending(x => x.DetectedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<FileRecord?> GetByOriginalPathAsync(string originalPath, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.FileRecords
            .AsNoTracking()
            .OrderByDescending(x => x.DetectedAt)
            .FirstOrDefaultAsync(x => x.OriginalPath == originalPath, ct)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FileRecord>> GetByOriginalPathPrefixAsync(
        string directoryPath,
        CancellationToken ct = default)
    {
        // Разделитель в конце обязателен: без него «C:\Загрузки» зацепила бы и «C:\Загрузки (старое)».
        var prefix = directoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.FileRecords
            .AsNoTracking()
            .Where(x => x.OriginalPath.StartsWith(prefix))
            .ToListAsync(ct)
            .ConfigureAwait(false);
    }

    public async Task<FileRecord?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        return await context.FileRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);
    }

    public async Task AddAsync(FileRecord record, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.FileRecords.Add(record);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task UpdateAsync(FileRecord record, CancellationToken ct = default)
    {
        await using var context = await CreateContextAsync(ct).ConfigureAwait(false);
        context.FileRecords.Update(record);
        await context.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
