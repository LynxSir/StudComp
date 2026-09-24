using StudComp.Core.Abstractions.Archivist;
using StudComp.Core.Domain;

namespace StudComp.Modules.Archivist.Tests;

/// <summary>
/// Нагрузочная проверка исполнителя файловых операций (Phase 12): много файлов с одинаковым целевым
/// именем в одной папке. Прямой молоток по самому строгому требованию проекта — ни один файл
/// пользователя не теряется и не перезаписывается (ARCHITECTURE §8.4 п.5, §14).
/// </summary>
public sealed class FileOperationExecutorStressTests : ArchivistDatabaseTestBase, IDisposable
{
    private readonly TempFolder _source = new("stress-src");
    private readonly TempFolder _target = new("stress-dst");

    [Fact]
    public async Task Two_hundred_files_with_the_same_target_name_all_survive_with_unique_suffixes()
    {
        const int count = 200;

        var records = new List<FileRecord>(count);
        for (var i = 0; i < count; i++)
        {
            var path = _source.WriteFile($"источник{i:D3}.docx", $"уникальное содержимое {i}");
            records.Add(await SeedFileRecordAsync(path));
        }

        foreach (var record in records)
        {
            var result = await Executor.MoveAndRenameAsync(
                Decision(record, "Отчёт.docx"), CancellationToken.None);
            Assert.Equal(FileOperationOutcome.Moved, result.Outcome);
        }

        // Все 200 приземлились в целевую папку, каждое содержимое — ровно один раз.
        var landed = Directory.EnumerateFiles(_target.Path).Select(File.ReadAllText).ToList();
        Assert.Equal(count, landed.Count);
        Assert.Equal(count, landed.Distinct(StringComparer.Ordinal).Count());
        for (var i = 0; i < count; i++)
        {
            Assert.Contains($"уникальное содержимое {i}", landed);
        }

        // Первый файл уходит под своим именем, остальные — с суффиксом « (N)».
        Assert.True(File.Exists(_target.Combine("Отчёт.docx")));
        Assert.True(File.Exists(_target.Combine("Отчёт (2).docx")));
        Assert.True(File.Exists(_target.Combine($"Отчёт ({count}).docx")));

        // В исходной папке не осталось ничего — всё перемещено, не скопировано.
        Assert.Empty(Directory.EnumerateFiles(_source.Path));

        // В журнале ровно count завершённых операций и ни одной зависшей в Planned.
        var allEntries = new List<FileOperationLogEntry>();
        foreach (var record in records)
        {
            allEntries.AddRange(await OperationLogRepo.GetByFileRecordAsync(record.Id));
        }

        Assert.Equal(count, allEntries.Count(e => e.Status == FileOperationLogStatus.Completed));
        Assert.DoesNotContain(allEntries, e => e.Status == FileOperationLogStatus.Planned);
    }

    [Fact]
    public async Task Identical_content_in_the_batch_is_deduplicated_and_sources_are_kept()
    {
        const int copies = 25;
        var records = new List<FileRecord>(copies);
        for (var i = 0; i < copies; i++)
        {
            var path = _source.WriteFile($"копия{i:D2}.docx", "один и тот же текст");
            records.Add(await SeedFileRecordAsync(path));
        }

        var outcomes = new List<FileOperationOutcome>();
        foreach (var record in records)
        {
            var result = await Executor.MoveAndRenameAsync(
                Decision(record, "Одинаковый.docx"), CancellationToken.None);
            outcomes.Add(result.Outcome);
        }

        // Ровно один файл переехал, остальные распознаны дубликатами и оставлены на месте.
        Assert.Equal(1, outcomes.Count(o => o == FileOperationOutcome.Moved));
        Assert.Equal(copies - 1, outcomes.Count(o => o == FileOperationOutcome.SkippedDuplicate));

        Assert.Single(Directory.EnumerateFiles(_target.Path));
        Assert.Equal(copies - 1, Directory.EnumerateFiles(_source.Path).Count());
    }

    private SortDecision Decision(FileRecord record, string newFileName) => new(
        SourceFileRecordId: record.Id,
        SubjectId: null,
        TargetDirectory: _target.Path,
        NewFileName: newFileName,
        MatchedRule: TestRule,
        Conflict: ConflictResolution.None);

    private static ArchivistRule TestRule { get; } = new()
    {
        Id = Guid.Empty,
        Pattern = ".docx",
        MatchType = RuleMatchType.Extension,
        Enabled = true,
    };

    public void Dispose()
    {
        _source.Dispose();
        _target.Dispose();
    }
}
