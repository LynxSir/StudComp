using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Domain;
using StudComp.Infrastructure.FileSystem;
using StudComp.Modules.Archivist.Services;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>
/// Ядро DoD Phase 5: ни в одном сценарии файл пользователя не удаляется и не перезаписывается
/// (ARCHITECTURE §8.4 п.5-6, §8.6, §14). Каждый тест явно проверяет сохранность содержимого.
/// </summary>
public sealed class FileOperationExecutorTests : ArchivistDatabaseTestBase, IDisposable
{
    private readonly TempFolder _source = new("exec-src");
    private readonly TempFolder _target = new("exec-dst");

    [Fact]
    public async Task File_is_moved_and_renamed()
    {
        var path = _source.WriteFile("отчёт.docx", "текст отчёта");
        var record = await SeedFileRecordAsync(path);

        var result = await Executor.MoveAndRenameAsync(Decision(record, "Матан_отчёт.docx"), CancellationToken.None);

        Assert.Equal(FileOperationOutcome.Moved, result.Outcome);

        var finalPath = _target.Combine("Матан_отчёт.docx");
        Assert.True(File.Exists(finalPath));
        Assert.Equal("текст отчёта", await File.ReadAllTextAsync(finalPath));
        Assert.False(File.Exists(path));

        var stored = await FileRecordRepo.GetByIdAsync(record.Id);
        Assert.Equal(FileRecordStatus.Sorted, stored!.Status);
        Assert.Equal(finalPath, stored.CurrentPath);
        // Исходный путь сохраняется — на нём будет держаться Undo (Phase 8).
        Assert.Equal(path, stored.OriginalPath);
    }

    [Fact]
    public async Task Operation_is_logged_before_the_move()
    {
        var path = _source.WriteFile("отчёт.docx", "текст");
        var record = await SeedFileRecordAsync(path);

        var result = await Executor.MoveAndRenameAsync(Decision(record, "отчёт.docx"), CancellationToken.None);

        var entries = await OperationLogRepo.GetByFileRecordAsync(record.Id);
        var entry = Assert.Single(entries);
        Assert.Equal(FileOperationLogStatus.Completed, entry.Status);
        Assert.Equal(path, entry.OriginalPath);
        Assert.Equal(result.FinalPath, entry.FinalPath);
        Assert.NotNull(entry.CompletedAt);
        // Журнал пишется раньше операции, значит и раньше её завершения.
        Assert.True(entry.StartedAt <= entry.CompletedAt);
    }

    [Fact]
    public async Task Name_conflict_with_different_content_appends_suffix_and_keeps_both_files()
    {
        var sourcePath = _source.WriteFile("отчёт.docx", "новая версия");
        var existingPath = _target.WriteFile("отчёт.docx", "старая версия");
        var record = await SeedFileRecordAsync(sourcePath);

        var result = await Executor.MoveAndRenameAsync(Decision(record, "отчёт.docx"), CancellationToken.None);

        Assert.Equal(FileOperationOutcome.Moved, result.Outcome);
        Assert.Equal(_target.Combine("отчёт (2).docx"), result.FinalPath);

        // Оба файла на месте и с исходным содержимым — ничего не перезаписано.
        Assert.Equal("старая версия", await File.ReadAllTextAsync(existingPath));
        Assert.Equal("новая версия", await File.ReadAllTextAsync(result.FinalPath!));
    }

    [Fact]
    public async Task Identical_content_is_skipped_and_source_survives()
    {
        var sourcePath = _source.WriteFile("отчёт.docx", "одно и то же");
        var existingPath = _target.WriteFile("отчёт.docx", "одно и то же");
        var record = await SeedFileRecordAsync(sourcePath);

        var result = await Executor.MoveAndRenameAsync(Decision(record, "отчёт.docx"), CancellationToken.None);

        Assert.Equal(FileOperationOutcome.SkippedDuplicate, result.Outcome);

        // Оба файла целы: дубликат не удаляется, оригинал не трогается.
        Assert.True(File.Exists(sourcePath));
        Assert.Equal("одно и то же", await File.ReadAllTextAsync(sourcePath));
        Assert.Equal("одно и то же", await File.ReadAllTextAsync(existingPath));

        var stored = await FileRecordRepo.GetByIdAsync(record.Id);
        Assert.Equal(FileRecordStatus.Quarantined, stored!.Status);
    }

    [Fact]
    public async Task Locked_source_is_deferred_and_file_survives()
    {
        var sourcePath = _source.WriteFile("занят.docx", "содержимое");
        var record = await SeedFileRecordAsync(sourcePath);

        using (new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var result = await Executor.MoveAndRenameAsync(Decision(record, "занят.docx"), CancellationToken.None);

            Assert.Equal(FileOperationOutcome.Deferred, result.Outcome);
            Assert.Equal("archivist.file_locked", result.Error!.Value.Code);
        }

        Assert.True(File.Exists(sourcePath));
        Assert.Equal("содержимое", await File.ReadAllTextAsync(sourcePath));

        // Неудачная операция остаётся в журнале со статусом Failed.
        var entries = await OperationLogRepo.GetByFileRecordAsync(record.Id);
        Assert.Equal(FileOperationLogStatus.Failed, Assert.Single(entries).Status);

        // Запись учёта уходит в Deferred (не Quarantined) — её подберёт очередь ретрая.
        Assert.Equal(FileRecordStatus.Deferred, (await FileRecordRepo.GetByIdAsync(record.Id))!.Status);
    }

    [Fact]
    public async Task Missing_target_directory_is_created()
    {
        var sourcePath = _source.WriteFile("отчёт.docx", "текст");
        var record = await SeedFileRecordAsync(sourcePath);
        var nested = _target.Combine("Матан", "Лекции");

        var decision = new SortDecision(
            record.Id, null, nested, "отчёт.docx", TestRule, ConflictResolution.None);

        var result = await Executor.MoveAndRenameAsync(decision, CancellationToken.None);

        Assert.Equal(FileOperationOutcome.Moved, result.Outcome);
        Assert.True(File.Exists(Path.Combine(nested, "отчёт.docx")));
    }

    [Fact]
    public async Task Missing_source_fails_without_touching_anything()
    {
        var record = await SeedFileRecordForMissingFileAsync(_source.Combine("нет-такого.docx"));

        var result = await Executor.MoveAndRenameAsync(Decision(record, "нет-такого.docx"), CancellationToken.None);

        Assert.Equal(FileOperationOutcome.Failed, result.Outcome);
        Assert.Equal("archivist.source_missing", result.Error!.Value.Code);
        Assert.Empty(Directory.EnumerateFiles(_target.Path));
    }

    [Fact]
    public async Task Undo_returns_file_to_its_original_path()
    {
        var path = _source.WriteFile("отчёт.docx", "текст отчёта");
        var record = await SeedFileRecordAsync(path);
        await Executor.MoveAndRenameAsync(Decision(record, "Матан_отчёт.docx"), CancellationToken.None);

        var undone = await Executor.UndoAsync(record.Id, CancellationToken.None);

        Assert.True(undone);
        Assert.True(File.Exists(path));
        Assert.Equal("текст отчёта", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(_target.Combine("Матан_отчёт.docx")));

        var stored = await FileRecordRepo.GetByIdAsync(record.Id);
        Assert.Equal(FileRecordStatus.UndoneByUser, stored!.Status);
        Assert.Equal(path, stored.CurrentPath);

        var entry = Assert.Single(await OperationLogRepo.GetByFileRecordAsync(record.Id));
        Assert.Equal(FileOperationLogStatus.RolledBack, entry.Status);
    }

    [Fact]
    public async Task Undo_without_a_completed_operation_returns_false()
    {
        var path = _source.WriteFile("отчёт.docx", "текст");
        var record = await SeedFileRecordAsync(path);

        Assert.False(await Executor.UndoAsync(record.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Undo_refuses_when_original_path_is_taken_again_and_keeps_that_file_intact()
    {
        var path = _source.WriteFile("отчёт.docx", "исходный текст");
        var record = await SeedFileRecordAsync(path);
        await Executor.MoveAndRenameAsync(Decision(record, "Матан_отчёт.docx"), CancellationToken.None);

        // Пользователь успел положить на прежний путь другой файл.
        File.WriteAllText(path, "чужой новый файл");

        var undone = await Executor.UndoAsync(record.Id, CancellationToken.None);

        Assert.False(undone);
        Assert.Equal("чужой новый файл", await File.ReadAllTextAsync(path));
        Assert.Equal("исходный текст", await File.ReadAllTextAsync(_target.Combine("Матан_отчёт.docx")));
    }

    [Fact]
    public async Task Second_undo_of_the_same_operation_returns_false()
    {
        var path = _source.WriteFile("отчёт.docx", "текст");
        var record = await SeedFileRecordAsync(path);
        await Executor.MoveAndRenameAsync(Decision(record, "Матан_отчёт.docx"), CancellationToken.None);

        Assert.True(await Executor.UndoAsync(record.Id, CancellationToken.None));
        Assert.False(await Executor.UndoAsync(record.Id, CancellationToken.None));
    }

    // --- Ревизия edge cases Phase 12 (ARCHITECTURE §8.8) ---

    [Fact]
    public async Task Overlong_target_name_is_quarantined_and_not_retried()
    {
        var sourcePath = _source.WriteFile("отчёт.docx", "текст");
        var record = await SeedFileRecordAsync(sourcePath);

        // 300-символьное имя физически не создать ни на NTFS, ни на FAT — ретрай бессмыслен.
        var overlong = new string('и', 300) + ".docx";

        var result = await Executor.MoveAndRenameAsync(Decision(record, overlong), CancellationToken.None);

        Assert.Equal(FileOperationOutcome.Failed, result.Outcome);
        Assert.Equal("archivist.path_too_long", result.Error!.Value.Code);
        Assert.Equal(FileRecordStatus.Quarantined, (await FileRecordRepo.GetByIdAsync(record.Id))!.Status);

        // Файл не тронут, целевая папка пуста.
        Assert.Equal("текст", await File.ReadAllTextAsync(sourcePath));
        Assert.Empty(Directory.EnumerateFiles(_target.Path));
    }

    [Fact]
    public async Task PathTooLong_from_move_is_quarantined_not_deferred()
    {
        var sourcePath = _source.WriteFile("отчёт.docx", "текст");
        var record = await SeedFileRecordAsync(sourcePath);

        var executor = ExecutorWith(new ThrowingFileSystem(new PathTooLongException()));

        var result = await executor.MoveAndRenameAsync(Decision(record, "отчёт.docx"), CancellationToken.None);

        Assert.Equal(FileOperationOutcome.Failed, result.Outcome);
        Assert.Equal("archivist.path_too_long", result.Error!.Value.Code);
        // Карантин, а не Deferred: очередь ретрая не должна крутить безнадёжный случай.
        Assert.Equal(FileRecordStatus.Quarantined, (await FileRecordRepo.GetByIdAsync(record.Id))!.Status);
        Assert.Equal(FileOperationLogStatus.Failed, Assert.Single(await OperationLogRepo.GetByFileRecordAsync(record.Id)).Status);
        Assert.Equal("текст", await File.ReadAllTextAsync(sourcePath));
    }

    [Fact]
    public async Task Unauthorized_access_on_move_is_deferred_and_file_survives()
    {
        var sourcePath = _source.WriteFile("отчёт.docx", "текст");
        var record = await SeedFileRecordAsync(sourcePath);

        var executor = ExecutorWith(new ThrowingFileSystem(new UnauthorizedAccessException()));

        var result = await executor.MoveAndRenameAsync(Decision(record, "отчёт.docx"), CancellationToken.None);

        // Права могли пропасть временно (антивирус, блокировка ACL) — даём шанс на ретрай.
        Assert.Equal(FileOperationOutcome.Deferred, result.Outcome);
        Assert.Equal(FileRecordStatus.Deferred, (await FileRecordRepo.GetByIdAsync(record.Id))!.Status);
        Assert.Equal("текст", await File.ReadAllTextAsync(sourcePath));
    }

    private FileOperationExecutor ExecutorWith(IFileSystem fileSystem) => new(
        fileSystem,
        Hasher,
        FileRecordRepo,
        OperationLogRepo,
        NullLogger<FileOperationExecutor>.Instance);

    private SortDecision Decision(FileRecord record, string newFileName) => new(
        SourceFileRecordId: record.Id,
        SubjectId: null,
        TargetDirectory: _target.Path,
        NewFileName: newFileName,
        MatchedRule: TestRule,
        Conflict: ConflictResolution.None);

    /// <summary>
    /// Реальная файловая система, но <see cref="Move"/> всегда бросает заданное исключение — так
    /// проверяются ветки отказа исполнителя без настоящих проблем с правами/длиной пути.
    /// </summary>
    private sealed class ThrowingFileSystem(Exception onMove) : IFileSystem
    {
        private readonly IFileSystem _inner = new SystemFileSystem();

        public bool FileExists(string path) => _inner.FileExists(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public void CreateDirectory(string path) => _inner.CreateDirectory(path);

        public Stream OpenRead(string path) => _inner.OpenRead(path);

        public FileStream Open(string path, FileMode mode, FileAccess access, FileShare share) =>
            _inner.Open(path, mode, access, share);

        public void Move(string sourcePath, string destinationPath, bool overwrite = false) => throw onMove;

        public void Copy(string sourcePath, string destinationPath, bool overwrite = false) =>
            _inner.Copy(sourcePath, destinationPath, overwrite);

        public long GetFileSize(string path) => _inner.GetFileSize(path);

        public DateTimeOffset GetLastWriteTimeUtc(string path) => _inner.GetLastWriteTimeUtc(path);

        public DateTimeOffset GetCreationTimeUtc(string path) => _inner.GetCreationTimeUtc(path);

        public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);

        public IEnumerable<string> EnumerateFiles(
            string directoryPath, string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
            _inner.EnumerateFiles(directoryPath, searchPattern, searchOption);

        public IEnumerable<string> EnumerateDirectories(
            string directoryPath, string searchPattern = "*",
            SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
            _inner.EnumerateDirectories(directoryPath, searchPattern, searchOption);
    }

    private static ArchivistRule TestRule { get; } = new()
    {
        Id = Guid.Empty,
        Pattern = ".docx",
        MatchType = RuleMatchType.Extension,
        Enabled = true,
    };

    private async Task<FileRecord> SeedFileRecordForMissingFileAsync(string path)
    {
        var record = new FileRecord
        {
            Id = Guid.NewGuid(),
            OriginalPath = path,
            CurrentPath = path,
            ContentHash = string.Empty,
            DetectedAt = DateTimeOffset.Now,
            Status = FileRecordStatus.Detected,
        };
        await FileRecordRepo.AddAsync(record);
        return record;
    }

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }
}
