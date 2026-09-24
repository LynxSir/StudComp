using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StudComp.Core.Abstractions.Data;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Settings;

namespace StudComp.Infrastructure.Backup;

/// <summary>Пути, которые бэкапит и восстанавливает <see cref="BackupService"/>.</summary>
public sealed record BackupLayout(string DatabaseFilePath, string SettingsFilePath)
{
    /// <summary>Рабочая раскладка: файлы из <see cref="RubricaPaths"/>.</summary>
    public static BackupLayout Default { get; } =
        new(RubricaPaths.DatabaseFile, RubricaPaths.UserSettingsFile);
}

/// <inheritdoc cref="IBackupService"/>
public sealed partial class BackupService : IBackupService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    // Заголовок файла SQLite: "SQLite format 3\0".
    private static readonly byte[] SqliteMagic =
        "SQLite format 3\0"u8.ToArray();

    private readonly IDatabaseBackupPort _database;
    private readonly UserSettingsProvider _settings;
    private readonly ILogger<BackupService> _logger;
    private readonly BackupLayout _layout;

    /// <summary>Рабочий конструктор — раскладка по умолчанию.</summary>
    public BackupService(IDatabaseBackupPort database, UserSettingsProvider settings, ILogger<BackupService> logger)
        : this(database, settings, logger, BackupLayout.Default)
    {
    }

    /// <summary>Конструктор с явной раскладкой — для тестов.</summary>
    public BackupService(
        IDatabaseBackupPort database, UserSettingsProvider settings, ILogger<BackupService> logger, BackupLayout layout)
    {
        _database = database;
        _settings = settings;
        _logger = logger;
        _layout = layout;
    }

    public async Task<Result> BackupAsync(
        string destinationZipPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default)
    {
        var staging = Path.Combine(Path.GetTempPath(), "rubrica-backup-" + Guid.NewGuid().ToString("N"));
        var tempZip = destinationZipPath + ".tmp";

        try
        {
            progress?.Report(new BackupProgress("Подготовка", 0.0));
            Directory.CreateDirectory(staging);
            var destDirectory = Path.GetDirectoryName(destinationZipPath);
            if (!string.IsNullOrEmpty(destDirectory))
            {
                Directory.CreateDirectory(destDirectory);
            }

            progress?.Report(new BackupProgress("Снимок базы данных", 0.15));
            var stagedDb = Path.Combine(staging, BackupManifest.DatabaseEntryName);
            await _database.WriteSnapshotAsync(stagedDb, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            var files = new List<string> { BackupManifest.DatabaseEntryName };
            progress?.Report(new BackupProgress("Учебные файлы и вложения", 0.35));
            var resources = await StageResourcesAsync(stagedDb, staging, destinationZipPath, tempZip, ct).ConfigureAwait(false);

            progress?.Report(new BackupProgress("Настройки", 0.55));
            if (File.Exists(_layout.SettingsFilePath))
            {
                File.Copy(_layout.SettingsFilePath, Path.Combine(staging, BackupManifest.SettingsEntryName));
                files.Add(BackupManifest.SettingsEntryName);
            }

            progress?.Report(new BackupProgress("Манифест", 0.65));
            var data = _settings.Get<DataOptions>(DataOptions.SectionName);
            var lineageId = data.BackupLineageId == Guid.Empty ? Guid.NewGuid() : data.BackupLineageId;
            var nextVersion = data.BackupVersion + 1;

            files = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(staging, path).Replace('\\', '/')).ToList();
            var hashes = files.ToDictionary(path => path, path => Sha256(Path.Combine(staging, path)));
            var manifest = new BackupManifest(
                BackupManifest.CurrentSchemaVersion,
                Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
                DateTimeOffset.UtcNow,
                Sha256(stagedDb),
                files,
                lineageId,
                nextVersion, resources, hashes);
            await File.WriteAllTextAsync(
                Path.Combine(staging, BackupManifest.ManifestEntryName),
                JsonSerializer.Serialize(manifest, JsonOptions),
                ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            progress?.Report(new BackupProgress("Упаковка", 0.8));
            if (File.Exists(tempZip))
            {
                File.Delete(tempZip);
            }

            ZipFile.CreateFromDirectory(staging, tempZip, CompressionLevel.Optimal, includeBaseDirectory: false);

            // Атомарная замена: существующий архив не портится при сбое до этого момента.
            File.Move(tempZip, destinationZipPath, overwrite: true);

            // Версию/линию фиксируем только после успешной записи архива — при сбое раньше номер
            // не «сгорает», следующая попытка получит тот же nextVersion.
            _settings.Update<DataOptions>(DataOptions.SectionName, o =>
            {
                o.BackupLineageId = lineageId;
                o.BackupVersion = nextVersion;
            });

            progress?.Report(new BackupProgress("Готово", 1.0));
            _logger.LogInformation("Резервная копия создана: {Path} (линия {Lineage}, версия {Version})",
                destinationZipPath, lineageId, nextVersion);
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure("backup.cancelled", "Резервное копирование отменено.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось создать резервную копию в {Path}", destinationZipPath);
            return Result.Failure("backup.failed", $"Не удалось создать резервную копию: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(staging);
            TryDeleteFile(tempZip);
        }
    }

    public async Task<Result<BackupRestoreOutcome>> RestoreAsync(
        string sourceZipPath,
        IProgress<BackupProgress>? progress = null,
        CancellationToken ct = default,
        string? studyParentDirectory = null)
    {
        var staging = Path.Combine(Path.GetTempPath(), "rubrica-restore-" + Guid.NewGuid().ToString("N"));

        try
        {
            progress?.Report(new BackupProgress("Чтение архива", 0.05));
            if (!File.Exists(sourceZipPath))
            {
                return Result<BackupRestoreOutcome>.Failure("backup.invalid", "Файл резервной копии не найден.");
            }

            Directory.CreateDirectory(staging);

            BackupManifest? manifest = null;
            using (var archive = ZipFile.OpenRead(sourceZipPath))
            {
                var dbEntry = archive.GetEntry(BackupManifest.DatabaseEntryName);
                if (dbEntry is null)
                {
                    return Result<BackupRestoreOutcome>.Failure(
                        "backup.invalid", "В архиве нет файла базы данных — это не резервная копия Rubrica.");
                }

                dbEntry.ExtractToFile(Path.Combine(staging, BackupManifest.DatabaseEntryName), overwrite: true);

                var settingsEntry = archive.GetEntry(BackupManifest.SettingsEntryName);
                settingsEntry?.ExtractToFile(Path.Combine(staging, BackupManifest.SettingsEntryName), overwrite: true);

                manifest = TryReadManifest(archive);
                ExtractResources(archive, staging, manifest, ct);
            }

            ct.ThrowIfCancellationRequested();

            progress?.Report(new BackupProgress("Проверка", 0.25));
            var restoredDb = Path.Combine(staging, BackupManifest.DatabaseEntryName);
            if (!LooksLikeSqlite(restoredDb))
            {
                return Result<BackupRestoreOutcome>.Failure(
                    "backup.corrupt", "Файл базы данных в архиве повреждён.");
            }

            if (manifest is not null && !Sha256(restoredDb).Equals(manifest.DatabaseSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Контрольная сумма базы данных не совпадает.");

            var mappings = new List<BackupPathMapping>();
            var restoredRoot = RestoreResourceFiles(staging, manifest, mappings, ct, studyParentDirectory);
            await _database.RemapResourcePathsAsync(restoredDb, mappings, ct,
                manifest?.Resources?.FirstOrDefault(x => x.ArchivePath == "workspace")?.SourcePath).ConfigureAwait(false);
            var restoredSettings = PrepareRestoredSettings(staging, manifest, restoredRoot, mappings);
            ct.ThrowIfCancellationRequested();

            progress?.Report(new BackupProgress("Страховочная копия", 0.45));
            var safetyCopy = _layout.DatabaseFilePath + $".bak-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
            if (File.Exists(_layout.DatabaseFilePath))
            {
                await _database.WriteSnapshotAsync(safetyCopy, ct).ConfigureAwait(false);
            }

            var settingsSafety = safetyCopy + ".settings";
            var hadSettings = File.Exists(_layout.SettingsFilePath);
            if (hadSettings) File.Copy(_layout.SettingsFilePath, settingsSafety);
            var settingsTemp = _layout.SettingsFilePath + $".{Guid.NewGuid():N}.restore";
            Directory.CreateDirectory(Path.GetDirectoryName(_layout.SettingsFilePath)!);
            File.Copy(restoredSettings, settingsTemp);
            progress?.Report(new BackupProgress("Замена базы данных и настроек", 0.85));
            try
            {
                // Once commit begins, finish it or roll it back; cancellation cannot split the pair.
                await _database.ReplaceDatabaseAsync(restoredDb, _layout.DatabaseFilePath, CancellationToken.None).ConfigureAwait(false);
                File.Move(settingsTemp, _layout.SettingsFilePath, overwrite: true);
            }
            catch
            {
                if (File.Exists(safetyCopy))
                    await _database.ReplaceDatabaseAsync(safetyCopy, _layout.DatabaseFilePath, CancellationToken.None).ConfigureAwait(false);
                if (hadSettings) File.Copy(settingsSafety, _layout.SettingsFilePath, overwrite: true);
                throw;
            }
            finally { TryDeleteFile(settingsTemp); }

            progress?.Report(new BackupProgress("Готово", 1.0));
            _logger.LogInformation("Восстановление из {Path} завершено; страховочная копия: {Safety}",
                sourceZipPath, safetyCopy);
            return Result<BackupRestoreOutcome>.Success(new BackupRestoreOutcome(RestartRequired: true, safetyCopy, restoredRoot));
        }
        catch (OperationCanceledException)
        {
            return Result<BackupRestoreOutcome>.Failure("backup.cancelled", "Восстановление отменено.");
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Повреждённый архив резервной копии: {Path}", sourceZipPath);
            return Result<BackupRestoreOutcome>.Failure("backup.corrupt", "Архив резервной копии повреждён.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось восстановить из {Path}", sourceZipPath);
            return Result<BackupRestoreOutcome>.Failure("backup.restore_failed", $"Не удалось восстановить: {ex.Message}");
        }
        finally
        {
            TryDeleteDirectory(staging);
        }
    }

    public Task<Result<BackupInspection>> InspectAsync(string sourceZipPath, CancellationToken ct = default)
    {
        if (!File.Exists(sourceZipPath))
        {
            return Task.FromResult(Result<BackupInspection>.Failure("backup.invalid", "Файл резервной копии не найден."));
        }

        try
        {
            using var archive = ZipFile.OpenRead(sourceZipPath);
            if (archive.GetEntry(BackupManifest.DatabaseEntryName) is null)
            {
                return Task.FromResult(Result<BackupInspection>.Failure(
                    "backup.invalid", "В архиве нет файла базы данных — это не резервная копия Rubrica."));
            }

            var manifest = TryReadManifest(archive);
            var local = _settings.Get<DataOptions>(DataOptions.SectionName);
            var lineageId = manifest?.LineageId ?? Guid.Empty;
            var version = manifest?.Version ?? 0;
            var relation = BackupLineageComparison.Classify(lineageId, version, local.BackupLineageId, local.BackupVersion);

            return Task.FromResult(Result<BackupInspection>.Success(new BackupInspection(
                HasManifest: manifest is not null,
                LineageId: lineageId,
                Version: version,
                CreatedUtc: manifest?.CreatedUtc ?? default,
                AppVersion: manifest?.AppVersion ?? "?",
                LocalLineageId: local.BackupLineageId,
                LocalVersion: local.BackupVersion,
                Relation: relation,
                IncludesFiles: manifest?.Resources is { Count: > 0 })));
        }
        catch (InvalidDataException ex)
        {
            _logger.LogWarning(ex, "Повреждённый архив резервной копии: {Path}", sourceZipPath);
            return Task.FromResult(Result<BackupInspection>.Failure("backup.corrupt", "Архив резервной копии повреждён."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось прочитать резервную копию {Path}", sourceZipPath);
            return Task.FromResult(Result<BackupInspection>.Failure("backup.invalid", "Не удалось прочитать архив резервной копии."));
        }
    }

    private static BackupManifest? TryReadManifest(ZipArchive archive)
    {
        var entry = archive.GetEntry(BackupManifest.ManifestEntryName);
        if (entry is null)
        {
            return null;
        }

        try
        {
            using var stream = entry.Open();
            return JsonSerializer.Deserialize<BackupManifest>(stream, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Манифест резервной копии повреждён.", ex);
        }
    }

    private static bool LooksLikeSqlite(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[16];
            return stream.Read(header) == 16 && header.SequenceEqual(SqliteMagic);
        }
        catch
        {
            return false;
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // временный каталог — не критично
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // временный файл — не критично
        }
    }
}
