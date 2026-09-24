using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StudComp.Core.Abstractions.Data;
using StudComp.Core.Common;

namespace StudComp.Data.Backup;

/// <summary>
/// Реализация <see cref="IDatabaseBackupPort"/> поверх <c>Microsoft.Data.Sqlite</c>. Единственное
/// место в коде, где файл БД трогается напрямую, минуя EF.
/// </summary>
internal sealed partial class SqliteDatabaseBackupPort(
    IDbContextFactory<StudCompDbContext> contextFactory,
    ILogger<SqliteDatabaseBackupPort> logger) : IDatabaseBackupPort
{
    public async Task WriteSnapshotAsync(string destinationFilePath, CancellationToken ct = default)
    {
        var directory = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await using var context = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        var source = (SqliteConnection)context.Database.GetDbConnection();

        var wasOpen = source.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
        {
            await source.OpenAsync(ct).ConfigureAwait(false);
        }

        try
        {
            // Pooling=False — иначе Dispose ниже возвращает нативное соединение в пул вместо
            // настоящего закрытия файла, и вызывающий код (хэш/упаковка staged-файла сразу после
            // WriteSnapshotAsync) падает на «файл занят другим процессом» (найдено round-trip
            // тестом Phase 13.8 — до него у этого порта не было ни одного теста на реальном движке).
            await using var target = new SqliteConnection($"Data Source={destinationFilePath};Pooling=False");
            await target.OpenAsync(ct).ConfigureAwait(false);

            // SQLite Online Backup API: консистентный снимок на живых писателях, без sidecar-файлов.
            source.BackupDatabase(target);
            logger.LogInformation("Снимок БД записан в {Path}", destinationFilePath);
        }
        finally
        {
            if (!wasOpen)
            {
                await source.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    public Task ReplaceDatabaseAsync(string sourceFilePath, string? destinationFilePath = null, CancellationToken ct = default)
    {
        var destination = destinationFilePath ?? RubricaPaths.DatabaseFile;

        // Пул держит файловый хендл открытым между вызовами — без сброса замена не «прилипнет».
        SqliteConnection.ClearAllPools();

        File.Copy(sourceFilePath, destination, overwrite: true);

        foreach (var sidecar in new[] { "-wal", "-shm", "-journal" })
        {
            var path = destination + sidecar;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        logger.LogInformation("Рабочая БД заменена из {Path}; требуется перезапуск", sourceFilePath);
        return Task.CompletedTask;
    }
}
