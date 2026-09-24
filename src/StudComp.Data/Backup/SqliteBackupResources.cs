using Microsoft.Data.Sqlite;
using StudComp.Core.Abstractions.Data;
using StudComp.Core.Domain;

namespace StudComp.Data.Backup;

internal sealed partial class SqliteDatabaseBackupPort
{
    // Explicit columns keep unrelated text and historical operation records untouched.
    private static readonly (string Table, string Column, bool Collect)[] ResourceColumns =
    [
        ("Subjects", "FolderPath", true), ("FileRecords", "CurrentPath", true),
        ("Notes", "LinkedPath", false), ("ReportJobs", "SourcePath", true),
        ("ReportJobs", "OutputPath", true), ("ArchivistRules", "WatchedFolder", false),
        ("ActivityLog", "Path", false),
    ];

    public async Task<IReadOnlyList<string>> GetResourcePathsAsync(string snapshotPath, CancellationToken ct = default, string? studyRootPath = null)
    {
        await using var connection = OpenSnapshot(snapshotPath);
        await connection.OpenAsync(ct);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (table, column, collect) in ResourceColumns.Where(x => x.Collect))
        {
            if (!await HasColumnAsync(connection, table, column, ct)) continue;
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT DISTINCT \"{column}\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var path = reader.GetString(0);
                if (Path.IsPathRooted(path)) result.Add(path);
            }
        }
        if (await HasColumnAsync(connection, "Notes", "ContentMarkdown", ct))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT ContentMarkdown FROM Notes";
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                foreach (var image in MarkdownLocalImages.Paths(reader.GetString(0)))
                {
                    if (!Path.IsPathRooted(image) && string.IsNullOrWhiteSpace(studyRootPath)) continue;
                    var path = Path.GetFullPath(Path.IsPathRooted(image) ? image : Path.Combine(studyRootPath!, image));
                    if (!File.Exists(path)) throw new FileNotFoundException($"Не найдено изображение заметки: {path}", path);
                    result.Add(path);
                    var drawing = Path.ChangeExtension(path, ".json");
                    if (File.Exists(drawing)) result.Add(drawing);
                }
        }
        return result.ToArray();
    }

    public async Task RemapResourcePathsAsync(string snapshotPath, IReadOnlyList<BackupPathMapping> mappings, CancellationToken ct = default, string? sourceStudyRoot = null)
    {
        if (mappings.Count == 0) return;
        await using var connection = OpenSnapshot(snapshotPath);
        await connection.OpenAsync(ct);
        using var transaction = connection.BeginTransaction();
        foreach (var (table, column, _) in ResourceColumns.Append(("Notes", "ContentMarkdown", false)))
        {
            if (!await HasColumnAsync(connection, table, column, ct, transaction)) continue;
            var updates = new List<(string Id, string Value)>();
            using (var read = connection.CreateCommand())
            {
                read.Transaction = transaction;
                read.CommandText = $"SELECT Id, \"{column}\" FROM \"{table}\" WHERE \"{column}\" IS NOT NULL";
                await using var reader = await read.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    var old = reader.GetString(1);
                    var value = column == "ContentMarkdown"
                        ? MarkdownLocalImages.Rewrite(old, path =>
                        {
                            if (Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(sourceStudyRoot))
                                return BackupPathMapping.Remap(path, mappings);
                            var absolute = Path.GetFullPath(Path.Combine(sourceStudyRoot, path));
                            var mapped = BackupPathMapping.Remap(absolute, mappings);
                            var newRoot = BackupPathMapping.Remap(sourceStudyRoot, mappings);
                            return Path.GetRelativePath(newRoot, mapped);
                        })
                        : BackupPathMapping.Remap(old, mappings);
                    if (old != value) updates.Add((reader.GetString(0), value));
                }
            }
            foreach (var (id, value) in updates)
            {
                using var write = connection.CreateCommand();
                write.Transaction = transaction;
                write.CommandText = $"UPDATE \"{table}\" SET \"{column}\" = $value WHERE Id = $id";
                write.Parameters.AddWithValue("$value", value);
                write.Parameters.AddWithValue("$id", id);
                await write.ExecuteNonQueryAsync(ct);
            }
        }
        await transaction.CommitAsync(ct);
    }

    private static SqliteConnection OpenSnapshot(string path) => new(new SqliteConnectionStringBuilder
    { DataSource = path, Pooling = false, Mode = SqliteOpenMode.ReadWrite }.ToString());

    private static async Task<bool> HasColumnAsync(SqliteConnection connection, string table, string column,
        CancellationToken ct, SqliteTransaction? transaction = null)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"PRAGMA table_info(\"{table}\")";
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            if (reader.GetString(1) == column) return true;
        return false;
    }
}
