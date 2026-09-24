using System.IO.Compression;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Core.Abstractions.Data;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Backup;
using StudComp.Infrastructure.Settings;

namespace StudComp.Infrastructure.Tests;

/// <summary>
/// Резервное копирование (new_addons.md §7 §9 §13). Порт БД подменяется файловым фейком (Sqlite в
/// этом проекте нет). Каждый сценарий проверяет, что исходные данные не потеряны и не перезаписаны
/// молча.
/// </summary>
public sealed class BackupServiceTests : IDisposable
{
    // Заголовок настоящего файла SQLite — валидатор восстановления его проверяет.
    private static readonly byte[] SqliteHeader = "SQLite format 3\0"u8.ToArray();

    private readonly string _root = Path.Combine(Path.GetTempPath(), "rubrica-backup-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _dbPath;
    private readonly string _settingsPath;
    private readonly UserSettingsProvider _settings;
    private readonly BackupService _service;

    public BackupServiceTests()
    {
        Directory.CreateDirectory(_root);
        _dbPath = Path.Combine(_root, "live", "rubrica.db");
        _settingsPath = Path.Combine(_root, "live", "usersettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);

        WriteDb(_dbPath, "исходная база");
        File.WriteAllText(_settingsPath, "{\"Rubrica\":{\"Appearance\":{\"Theme\":\"Dark\"}}}");

        _settings = new UserSettingsProvider(NullLogger<UserSettingsProvider>.Instance, _settingsPath);
        _service = new BackupService(
            new FakeBackupPort(_dbPath),
            _settings,
            NullLogger<BackupService>.Instance,
            new BackupLayout(_dbPath, _settingsPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task Backup_creates_zip_with_db_settings_and_manifest()
    {
        var zip = Path.Combine(_root, "backup.zip");

        var result = await _service.BackupAsync(zip);

        Assert.True(result.IsSuccess);
        using var archive = ZipFile.OpenRead(zip);
        Assert.NotNull(archive.GetEntry("rubrica.db"));
        Assert.NotNull(archive.GetEntry("usersettings.json"));
        Assert.NotNull(archive.GetEntry("manifest.json"));
    }

    [Fact]
    public async Task Study_files_restore_to_a_new_folder_without_overwriting_local_files()
    {
        var study = Path.Combine(_root, "Учёба");
        Directory.CreateDirectory(Path.Combine(study, "Предмет", "Рисунки"));
        var picture = Path.Combine(study, "Предмет", "Рисунки", "photo.png");
        await File.WriteAllBytesAsync(picture, [1, 2, 3, 4]);
        _settings.Update<WorkspaceOptions>(WorkspaceOptions.SectionName, x => x.StudyRootPath = study);
        _settings.Update<ArchivistOptions>(ArchivistOptions.SectionName, x => { x.Enabled = true; x.WatchedFolders = [study]; });
        var zip = Path.Combine(study, "backup.zip");
        Assert.True((await _service.BackupAsync(zip)).IsSuccess);
        await File.WriteAllBytesAsync(picture, [9]);

        var result = await _service.RestoreAsync(zip);

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        Assert.NotEqual(study, result.Value.StudyRootPath);
        Assert.Equal(new byte[] { 9 }, await File.ReadAllBytesAsync(picture));
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, await File.ReadAllBytesAsync(Path.Combine(result.Value.StudyRootPath!, "Предмет", "Рисунки", "photo.png")));
        Assert.Equal(result.Value.StudyRootPath, _settings.Get<WorkspaceOptions>(WorkspaceOptions.SectionName).StudyRootPath);
        Assert.False(_settings.Get<ArchivistOptions>(ArchivistOptions.SectionName).Enabled);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Null(archive.GetEntry("workspace/backup.zip"));
    }

    [Fact]
    public async Task Corrupt_attachment_is_rejected_before_replacing_data()
    {
        var study = Path.Combine(_root, "study");
        Directory.CreateDirectory(study);
        await File.WriteAllTextAsync(Path.Combine(study, "a.png"), "original");
        _settings.Update<WorkspaceOptions>(WorkspaceOptions.SectionName, x => x.StudyRootPath = study);
        var zip = Path.Combine(_root, "copy.zip");
        await _service.BackupAsync(zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            archive.GetEntry("workspace/a.png")!.Delete();
            await using var writer = new StreamWriter(archive.CreateEntry("workspace/a.png").Open());
            await writer.WriteAsync("corrupt");
        }
        var before = Snapshot(Path.GetDirectoryName(_dbPath)!);
        Assert.True((await _service.RestoreAsync(zip)).IsFailure);
        Assert.Equal(before, Snapshot(Path.GetDirectoryName(_dbPath)!));
    }

    [Fact]
    public async Task Archive_cannot_extract_outside_its_staging_directory()
    {
        var zip = Path.Combine(_root, "copy.zip");
        await _service.BackupAsync(zip);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Update))
        {
            var entry = archive.GetEntry("manifest.json")!;
            BackupManifest manifest;
            using (var stream = entry.Open()) manifest = System.Text.Json.JsonSerializer.Deserialize<BackupManifest>(stream)!;
            entry.Delete();
            manifest = manifest with { Files = [.. manifest.Files, "workspace/../../outside.txt"] };
            await using var writer = new StreamWriter(archive.CreateEntry("manifest.json").Open());
            await writer.WriteAsync(System.Text.Json.JsonSerializer.Serialize(manifest));
        }
        var before = Sha256(_dbPath);
        Assert.True((await _service.RestoreAsync(zip)).IsFailure);
        Assert.Equal(before, Sha256(_dbPath));
    }

    [Fact]
    public async Task Backup_leaves_the_live_database_file_byte_for_byte_identical()
    {
        var before = Sha256(_dbPath);

        await _service.BackupAsync(Path.Combine(_root, "backup.zip"));

        Assert.Equal(before, Sha256(_dbPath));
    }

    [Fact]
    public async Task Backup_only_adds_its_own_lineage_section_to_settings_leaving_the_rest_untouched()
    {
        // Единственный намеренный побочный эффект бэкапа (Phase 13.8): линия/версия пишутся в
        // usersettings.json — иначе монотонному счётчику негде пережить перезапуск приложения.
        // Остальное содержимое файла настроек при этом не трогается.
        await _service.BackupAsync(Path.Combine(_root, "backup.zip"));

        Assert.Contains("Dark", File.ReadAllText(_settingsPath));
        var data = _settings.Get<DataOptions>(DataOptions.SectionName);
        Assert.NotEqual(Guid.Empty, data.BackupLineageId);
        Assert.Equal(1, data.BackupVersion);
    }

    [Fact]
    public async Task Cancelled_backup_does_not_touch_existing_archive()
    {
        var zip = Path.Combine(_root, "backup.zip");
        await _service.BackupAsync(zip);
        var existing = Sha256(zip);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var result = await _service.BackupAsync(zip, progress: null, cts.Token);

        Assert.True(result.IsFailure);
        Assert.Equal(existing, Sha256(zip));
    }

    [Fact]
    public async Task Backup_then_restore_round_trips()
    {
        var zip = Path.Combine(_root, "backup.zip");
        await _service.BackupAsync(zip);

        var dbBefore = Sha256(_dbPath);
        WriteDb(_dbPath, "испорчено");
        File.WriteAllText(_settingsPath, "мусор");

        var result = await _service.RestoreAsync(zip);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.RestartRequired);
        Assert.Equal(dbBefore, Sha256(_dbPath));
        Assert.Contains("Dark", File.ReadAllText(_settingsPath));
    }

    [Fact]
    public async Task Restore_makes_timestamped_safety_copy()
    {
        var zip = Path.Combine(_root, "backup.zip");
        await _service.BackupAsync(zip);

        var result = await _service.RestoreAsync(zip);

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.Value.SafetyCopyPath));
        Assert.StartsWith(_dbPath + ".bak-", result.Value.SafetyCopyPath);
    }

    [Fact]
    public async Task Restore_rejects_zip_without_database_and_leaves_data_untouched()
    {
        var zip = Path.Combine(_root, "broken.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("manifest.json");
            await using var writer = new StreamWriter(entry.Open());
            await writer.WriteAsync("{}");
        }

        var before = Snapshot(Path.GetDirectoryName(_dbPath)!);
        var result = await _service.RestoreAsync(zip);

        Assert.True(result.IsFailure);
        Assert.Equal("backup.invalid", result.Error.Code);
        Assert.Equal(before, Snapshot(Path.GetDirectoryName(_dbPath)!));
    }

    [Fact]
    public async Task Restore_of_corrupt_database_is_reported_not_thrown()
    {
        var zip = Path.Combine(_root, "corrupt.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("rubrica.db");
            await using var stream = entry.Open();
            await stream.WriteAsync("не база данных"u8.ToArray());
        }

        var before = Snapshot(Path.GetDirectoryName(_dbPath)!);
        var result = await _service.RestoreAsync(zip);

        Assert.True(result.IsFailure);
        Assert.Equal("backup.corrupt", result.Error.Code);
        Assert.Equal(before, Snapshot(Path.GetDirectoryName(_dbPath)!));
    }

    [Fact]
    public async Task Missing_settings_file_still_produces_valid_backup()
    {
        File.Delete(_settingsPath);
        var zip = Path.Combine(_root, "backup.zip");

        var result = await _service.BackupAsync(zip);

        Assert.True(result.IsSuccess);
        using var archive = ZipFile.OpenRead(zip);
        Assert.NotNull(archive.GetEntry("rubrica.db"));
        Assert.Null(archive.GetEntry("usersettings.json"));
    }

    [Fact]
    public async Task Backup_assigns_a_lineage_id_on_first_export_and_reuses_it_on_subsequent_exports()
    {
        var first = Path.Combine(_root, "first.zip");
        var second = Path.Combine(_root, "second.zip");

        await _service.BackupAsync(first);
        var firstManifest = ReadManifest(first);

        await _service.BackupAsync(second);
        var secondManifest = ReadManifest(second);

        Assert.NotEqual(Guid.Empty, firstManifest.LineageId);
        Assert.Equal(firstManifest.LineageId, secondManifest.LineageId);
        Assert.Equal(1, firstManifest.Version);
        Assert.Equal(2, secondManifest.Version);
    }

    [Fact]
    public async Task Restore_inherits_lineage_and_version_from_the_archives_manifest()
    {
        var zip = Path.Combine(_root, "backup.zip");
        await _service.BackupAsync(zip);
        var manifest = ReadManifest(zip);

        // Другая "чистая" установка — своя линия/версия локально ещё не заведены.
        var freshSettingsPath = Path.Combine(_root, "fresh-usersettings.json");
        var freshSettings = new UserSettingsProvider(NullLogger<UserSettingsProvider>.Instance, freshSettingsPath);
        var freshDbPath = Path.Combine(_root, "fresh", "rubrica.db");
        Directory.CreateDirectory(Path.GetDirectoryName(freshDbPath)!);
        WriteDb(freshDbPath, "чистая установка");
        var freshService = new BackupService(
            new FakeBackupPort(freshDbPath), freshSettings, NullLogger<BackupService>.Instance,
            new BackupLayout(freshDbPath, freshSettingsPath));

        var result = await freshService.RestoreAsync(zip);

        Assert.True(result.IsSuccess);
        var restored = freshSettings.Get<DataOptions>(DataOptions.SectionName);
        Assert.Equal(manifest.LineageId, restored.BackupLineageId);
        Assert.Equal(manifest.Version, restored.BackupVersion);
    }

    [Fact]
    public async Task Restore_of_legacy_backup_without_manifest_leaves_local_lineage_untouched()
    {
        await _service.BackupAsync(Path.Combine(_root, "seed.zip")); // назначает локальную линию v1

        var localBefore = _settings.Get<DataOptions>(DataOptions.SectionName);

        var legacyZip = Path.Combine(_root, "legacy.zip");
        using (var archive = ZipFile.Open(legacyZip, ZipArchiveMode.Create))
        {
            var dbEntry = archive.CreateEntry("rubrica.db");
            await using (var stream = dbEntry.Open())
            {
                await stream.WriteAsync(SqliteHeader);
            }
            // Специально без manifest.json — имитация архива, сделанного до Phase 13.8.
        }

        var result = await _service.RestoreAsync(legacyZip);

        Assert.True(result.IsSuccess);
        var localAfter = _settings.Get<DataOptions>(DataOptions.SectionName);
        Assert.Equal(localBefore.BackupLineageId, localAfter.BackupLineageId);
        Assert.Equal(localBefore.BackupVersion, localAfter.BackupVersion);
    }

    [Fact]
    public async Task Inspect_reports_same_lineage_newer_version()
    {
        var v1 = Path.Combine(_root, "v1.zip");
        await _service.BackupAsync(v1); // локальная версия -> 1
        var v2 = Path.Combine(_root, "v2.zip");
        await _service.BackupAsync(v2); // локальная версия -> 2

        // Откат на v1 переводит локальную версию обратно на 1 (унаследовано из манифеста v1) —
        // теперь v2 действительно новее текущей локальной установки.
        await _service.RestoreAsync(v1);

        var inspection = await _service.InspectAsync(v2);

        Assert.True(inspection.IsSuccess);
        Assert.True(inspection.Value.HasManifest);
        Assert.Equal(BackupLineageRelation.SameLineageNewer, inspection.Value.Relation);
    }

    [Fact]
    public async Task Inspect_reports_same_lineage_older_version()
    {
        var older = Path.Combine(_root, "older.zip");
        await _service.BackupAsync(older); // версия 1, лениво заводит локальную линию
        await _service.BackupAsync(Path.Combine(_root, "newer.zip")); // локально уже версия 2

        var inspection = await _service.InspectAsync(older);

        Assert.True(inspection.IsSuccess);
        Assert.Equal(BackupLineageRelation.SameLineageOlderOrEqual, inspection.Value.Relation);
        Assert.Equal(1, inspection.Value.Version);
        Assert.Equal(2, inspection.Value.LocalVersion);
    }

    [Fact]
    public async Task Inspect_reports_different_lineage()
    {
        var zip = Path.Combine(_root, "backup.zip");
        await _service.BackupAsync(zip);

        var otherSettingsPath = Path.Combine(_root, "other-usersettings.json");
        var otherSettings = new UserSettingsProvider(NullLogger<UserSettingsProvider>.Instance, otherSettingsPath);
        var otherService = new BackupService(
            new FakeBackupPort(_dbPath), otherSettings, NullLogger<BackupService>.Instance,
            new BackupLayout(_dbPath, otherSettingsPath));
        await otherService.BackupAsync(Path.Combine(_root, "other.zip")); // заводит свою, другую линию

        var inspection = await otherService.InspectAsync(zip);

        Assert.True(inspection.IsSuccess);
        Assert.Equal(BackupLineageRelation.DifferentOrUnknownLineage, inspection.Value.Relation);
    }

    [Fact]
    public async Task Inspect_of_backup_without_manifest_reports_unknown_lineage()
    {
        var legacyZip = Path.Combine(_root, "legacy.zip");
        using (var archive = ZipFile.Open(legacyZip, ZipArchiveMode.Create))
        {
            var dbEntry = archive.CreateEntry("rubrica.db");
            await using var stream = dbEntry.Open();
            await stream.WriteAsync(SqliteHeader);
        }

        var inspection = await _service.InspectAsync(legacyZip);

        Assert.True(inspection.IsSuccess);
        Assert.False(inspection.Value.HasManifest);
        Assert.Equal(BackupLineageRelation.DifferentOrUnknownLineage, inspection.Value.Relation);
    }

    [Fact]
    public async Task Inspect_of_missing_file_fails()
    {
        var result = await _service.InspectAsync(Path.Combine(_root, "не-существует.zip"));

        Assert.True(result.IsFailure);
        Assert.Equal("backup.invalid", result.Error.Code);
    }

    private static BackupManifest ReadManifest(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        var entry = archive.GetEntry("manifest.json")!;
        using var stream = entry.Open();
        return System.Text.Json.JsonSerializer.Deserialize<BackupManifest>(stream)!;
    }

    private static void WriteDb(string path, string payload)
    {
        using var stream = File.Create(path);
        stream.Write(SqliteHeader);
        stream.Write(System.Text.Encoding.UTF8.GetBytes(payload));
    }

    private static Dictionary<string, string> Snapshot(string directory)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            result[Path.GetRelativePath(directory, file)] = Sha256(file);
        }

        return result;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Фейк порта БД: снимок = копия файла, замена = обратная копия.</summary>
    private sealed class FakeBackupPort(string databaseFilePath) : IDatabaseBackupPort
    {
        public Task WriteSnapshotAsync(string destinationFilePath, CancellationToken ct = default)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFilePath)!);
            File.Copy(databaseFilePath, destinationFilePath, overwrite: true);
            return Task.CompletedTask;
        }

        public Task ReplaceDatabaseAsync(string sourceFilePath, string? destinationFilePath = null, CancellationToken ct = default)
        {
            File.Copy(sourceFilePath, destinationFilePath ?? databaseFilePath, overwrite: true);
            return Task.CompletedTask;
        }
    }
}
