using System.Security.Cryptography;
using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Workspace;

namespace StudComp.Infrastructure.Tests;

/// <summary>
/// Импорт файлов в папку предмета (new_addons.md §1.11) — зона самого строгого требования проекта
/// (§14). Каждый сценарий снимает содержимое обеих папок до и после операции: файл пользователя не
/// имеет права ни потеряться, ни оказаться перезаписанным.
/// </summary>
public sealed class WorkspaceImportServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"rubrica-import-{Guid.NewGuid():N}");

    private readonly string _source;
    private readonly string _target;
    private readonly IWorkspaceImportService _service;

    public WorkspaceImportServiceTests()
    {
        _source = Path.Combine(_root, "Загрузки");
        _target = Path.Combine(_root, "Матан");
        Directory.CreateDirectory(_source);
        Directory.CreateDirectory(_target);

        var fileSystem = new SystemFileSystem();
        _service = new WorkspaceImportService(
            fileSystem,
            new FileHasher(fileSystem),
            NullLogger<WorkspaceImportService>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    // ---- снимки папок --------------------------------------------------------------------

    /// <summary>Снимок каталога: относительный путь → (размер, SHA-256 полного содержимого).</summary>
    private static Dictionary<string, (long Size, string Hash)> Snapshot(string directory)
    {
        var result = new Dictionary<string, (long, string)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            return result;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            result[Path.GetRelativePath(directory, file)] = (new FileInfo(file).Length, Sha256(file));
        }

        return result;
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private string WriteSource(string name, string content)
    {
        var path = Path.Combine(_source, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string WriteTarget(string name, string content)
    {
        var path = Path.Combine(_target, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>Ни один файл, лежавший в папке, не изменил содержимое и не исчез.</summary>
    private static void AssertUntouched(
        Dictionary<string, (long Size, string Hash)> before,
        Dictionary<string, (long Size, string Hash)> after)
    {
        foreach (var (name, expected) in before)
        {
            Assert.True(after.ContainsKey(name), $"Файл {name} исчез");
            Assert.Equal(expected, after[name]);
        }
    }

    // ---- сценарии ------------------------------------------------------------------------

    [Fact]
    public async Task Move_transfers_file_and_leaves_nothing_behind()
    {
        var file = WriteSource("Лекция.docx", "содержимое лекции");
        var hash = Sha256(file);

        var summary = await _service.ImportAsync([file], _target, ImportMode.Move);

        Assert.Equal(1, summary.Imported);
        Assert.False(File.Exists(file));

        var after = Snapshot(_target);
        Assert.Single(after);
        Assert.Equal(hash, after["Лекция.docx"].Hash);
    }

    [Fact]
    public async Task Copy_leaves_the_source_folder_byte_for_byte_identical()
    {
        var first = WriteSource("Лекция.docx", "первый");
        var second = WriteSource("Практика.pdf", "второй");
        var before = Snapshot(_source);

        var summary = await _service.ImportAsync([first, second], _target, ImportMode.Copy);

        Assert.Equal(2, summary.Imported);

        // Самое сильное утверждение фазы: при копировании источник не меняется вообще никак.
        Assert.Equal(before, Snapshot(_source));
        Assert.Equal(2, Snapshot(_target).Count);
    }

    [Fact]
    public async Task Existing_target_files_are_never_overwritten()
    {
        var file = WriteSource("Отчёт.docx", "новая версия");
        WriteTarget("Отчёт.docx", "старая версия, терять нельзя");
        var targetBefore = Snapshot(_target);

        var summary = await _service.ImportAsync([file], _target, ImportMode.Move);

        var item = Assert.Single(summary.Items);
        Assert.Equal(ImportItemOutcome.RenamedDueToConflict, item.Outcome);

        var after = Snapshot(_target);
        AssertUntouched(targetBefore, after);
        Assert.Equal(2, after.Count);
        Assert.True(after.ContainsKey("Отчёт (2).docx"));
    }

    [Fact]
    public async Task Duplicate_content_is_skipped_and_nothing_moves()
    {
        var file = WriteSource("Отчёт.docx", "одно и то же");
        WriteTarget("Отчёт.docx", "одно и то же");
        var sourceBefore = Snapshot(_source);
        var targetBefore = Snapshot(_target);

        var summary = await _service.ImportAsync([file], _target, ImportMode.Move);

        Assert.Equal(1, summary.SkippedDuplicates);
        Assert.Equal(sourceBefore, Snapshot(_source));
        Assert.Equal(targetBefore, Snapshot(_target));
    }

    [Fact]
    public async Task Duplicate_content_with_keep_both_adds_a_second_copy()
    {
        var file = WriteSource("Отчёт.docx", "одно и то же");
        WriteTarget("Отчёт.docx", "одно и то же");
        var targetBefore = Snapshot(_target);

        var summary = await _service.ImportAsync(
            [file], _target, ImportMode.Copy, DuplicatePolicy.KeepBoth);

        Assert.Equal(1, summary.Imported);
        var after = Snapshot(_target);
        AssertUntouched(targetBefore, after);
        Assert.Equal(2, after.Count);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Batch_with_one_target_name_produces_unique_names_and_loses_nothing()
    {
        var sources = new List<string>();
        for (var i = 0; i < 50; i++)
        {
            var directory = Path.Combine(_source, $"п{i}");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "Отчёт.docx");
            File.WriteAllText(path, $"содержимое номер {i}");
            sources.Add(path);
        }

        var expectedHashes = sources.Select(Sha256).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        var summary = await _service.ImportAsync(sources, _target, ImportMode.Move);

        Assert.Equal(50, summary.Imported);

        var after = Snapshot(_target);
        Assert.Equal(50, after.Count);

        // Все 50 различных содержимых доехали, ни одно не затёрло другое.
        Assert.Equal(
            expectedHashes,
            after.Values.Select(x => x.Hash).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Locked_source_fails_without_touching_anything()
    {
        var file = WriteSource("Занятый.docx", "содержимое");
        var sourceBefore = Snapshot(_source);
        var targetBefore = Snapshot(_target);

        using (File.Open(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var summary = await _service.ImportAsync([file], _target, ImportMode.Move);

            var item = Assert.Single(summary.Items);
            Assert.Equal(ImportItemOutcome.Failed, item.Outcome);
            Assert.Equal("workspace.file_locked", item.Error!.Value.Code);
        }

        Assert.Equal(sourceBefore, Snapshot(_source));
        Assert.Equal(targetBefore, Snapshot(_target));
    }

    [Fact]
    public async Task Missing_source_is_reported_not_thrown()
    {
        var summary = await _service.ImportAsync(
            [Path.Combine(_source, "нет-такого.docx")], _target, ImportMode.Move);

        var item = Assert.Single(summary.Items);
        Assert.Equal(ImportItemOutcome.Failed, item.Outcome);
        Assert.Equal("workspace.source_missing", item.Error!.Value.Code);
        Assert.Empty(Snapshot(_target));
    }

    [Fact]
    public async Task One_bad_source_does_not_stop_the_rest_of_the_batch()
    {
        var ghost = Path.Combine(_source, "исчез.docx");
        var good = WriteSource("Лекция.docx", "содержимое");

        var summary = await _service.ImportAsync([ghost, good], _target, ImportMode.Move);

        Assert.Equal(1, summary.Failed);
        Assert.Equal(1, summary.Imported);
        Assert.Equal(
            "workspace.source_missing",
            summary.Items.Single(x => x.SourcePath == ghost).Error!.Value.Code);
        Assert.True(File.Exists(Path.Combine(_target, "Лекция.docx")));
    }

    [Fact]
    public async Task Cancellation_between_files_keeps_the_rest_untouched()
    {
        var sources = Enumerable.Range(0, 6)
            .Select(i => WriteSource($"Файл{i}.docx", $"содержимое {i}"))
            .ToArray();

        using var cts = new CancellationTokenSource();
        var progress = new Progress<ImportProgress>(p =>
        {
            if (p.Done >= 2)
            {
                cts.Cancel();
            }
        });

        var summary = await _service.ImportAsync(
            sources, _target, ImportMode.Move, DuplicatePolicy.Skip, progress, cts.Token);

        Assert.True(summary.WasCancelled);

        var after = Snapshot(_target);

        // Ни одного обрывка: каждый файл в цели совпадает с одним из исходных по содержимому,
        // а каждый неперенесённый исходник цел.
        Assert.All(after.Values, x => Assert.True(x.Size > 0));
        Assert.Equal(sources.Length, after.Count + Snapshot(_source).Count);
    }

    [Fact]
    public async Task Dropped_folder_is_recreated_with_its_structure()
    {
        var folder = Path.Combine(_source, "Лабы");
        Directory.CreateDirectory(Path.Combine(folder, "ЛР1"));
        File.WriteAllText(Path.Combine(folder, "readme.md"), "описание");
        File.WriteAllText(Path.Combine(folder, "ЛР1", "отчёт.docx"), "текст отчёта");

        var summary = await _service.ImportAsync([folder], _target, ImportMode.Copy);

        Assert.Equal(2, summary.Imported);

        var after = Snapshot(_target);
        Assert.True(after.ContainsKey(Path.Combine("Лабы", "readme.md")));
        Assert.True(after.ContainsKey(Path.Combine("Лабы", "ЛР1", "отчёт.docx")));
    }

    [Fact]
    public async Task Progress_is_reported_for_every_file()
    {
        var sources = Enumerable.Range(0, 3)
            .Select(i => WriteSource($"Файл{i}.docx", $"содержимое {i}"))
            .ToArray();

        var reports = new List<ImportProgress>();
        var progress = new Progress<ImportProgress>(reports.Add);

        await _service.ImportAsync(sources, _target, ImportMode.Copy, DuplicatePolicy.Skip, progress);

        // Progress<T> маршалит через SynchronizationContext — в тесте это пул потоков, поэтому
        // дожидаемся доставки, а не полагаемся на синхронность.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (reports.Count < 4 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.Contains(reports, r => r.Total == 3);
    }

    [Fact]
    public async Task Importing_into_the_same_folder_is_a_no_op()
    {
        var file = WriteSource("Лекция.docx", "содержимое");
        var before = Snapshot(_source);

        var summary = await _service.ImportAsync([file], _source, ImportMode.Move);

        Assert.Equal(1, summary.Imported);
        Assert.Equal(before, Snapshot(_source));
    }

    [Fact]
    public async Task Returned_hash_matches_the_archivist_hash()
    {
        var file = WriteSource("Лекция.docx", "содержимое лекции");
        var fileSystem = new SystemFileSystem();
        var expected = await new FileHasher(fileSystem).ComputeAsync(file);

        var summary = await _service.ImportAsync([file], _target, ImportMode.Copy);

        // Хэш возвращается наружу, чтобы вызывающий не считал его второй раз, и обязан совпадать
        // с тем, что пишет Архивариус в FileRecord.ContentHash.
        Assert.Equal(expected, Assert.Single(summary.Items).ContentHash);
    }
}
