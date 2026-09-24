using System.IO.Compression;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using StudComp.Core.Domain;
using StudComp.Data.Backup;
using StudComp.Infrastructure.Backup;
using StudComp.Infrastructure.Settings;

namespace StudComp.Data.Tests;

/// <summary>
/// Сквозной round-trip полного состояния (Phase 13.8, new_addons.md §13): экспорт → «чистая
/// установка» → импорт восстанавливает предметы/расписание/дедлайны/зачётку/правила и журнал
/// Архивариуса/карточки-колоды-метки в рабочем состоянии. На <b>настоящем</b> движке —
/// <see cref="SqliteDatabaseBackupPort"/> + реальный <see cref="StudCompDbContext"/> с накатанными
/// миграциями, не на байтовом фейке из <c>BackupServiceTests</c> — иначе "экспорт → перенос на
/// чистую установку → импорт" не доказан по-настоящему для доменных данных. Отсюда и
/// ProjectReference тестового проекта на <c>StudComp.Infrastructure</c> (см. .csproj) — только для
/// тестов, production-граф не меняется.
/// </summary>
public sealed class BackupFullStateRoundTripTests : IAsyncLifetime
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "rubrica-backup-roundtrip-" + Guid.NewGuid().ToString("N"));

    private string _sourceDbPath = null!;
    private string _targetDbPath = null!;
    private string _sourceSettingsPath = null!;
    private string _targetSettingsPath = null!;
    private DbContextOptions<StudCompDbContext> _sourceOptions = null!;
    private DbContextOptions<StudCompDbContext> _targetOptions = null!;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(_root);
        _sourceDbPath = Path.Combine(_root, "source", "rubrica.db");
        _targetDbPath = Path.Combine(_root, "target", "rubrica.db");
        _sourceSettingsPath = Path.Combine(_root, "source", "usersettings.json");
        _targetSettingsPath = Path.Combine(_root, "target", "usersettings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_sourceDbPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_targetDbPath)!);

        // Pooling=False — как в DatabaseTestBase: соединение не держит файл, его можно заменить/удалить.
        _sourceOptions = new DbContextOptionsBuilder<StudCompDbContext>()
            .UseSqlite($"Data Source={_sourceDbPath};Pooling=False")
            .Options;
        _targetOptions = new DbContextOptionsBuilder<StudCompDbContext>()
            .UseSqlite($"Data Source={_targetDbPath};Pooling=False")
            .Options;

        await using (var source = new StudCompDbContext(_sourceOptions))
        {
            await source.Database.MigrateAsync();
        }

        // "Чистая установка" — та же схема, накатанная с нуля, без единой строки данных.
        await using (var target = new StudCompDbContext(_targetOptions))
        {
            await target.Database.MigrateAsync();
        }
    }

    public Task DisposeAsync()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        return Task.CompletedTask;
    }

    [Fact]
    public async Task Backup_then_restore_on_a_clean_install_reproduces_every_domain_entity_and_lineage()
    {
        // --- Arrange: по одной записи каждого вида из DoD на "исходной" установке ---
        var subject = TestData.Subject();
        var schedule = TestData.ScheduleEntry(subject.Id);
        var deadline = TestData.Deadline(subject.Id, DateTimeOffset.UtcNow.AddDays(7));
        var grade = TestData.GradeEntry(subject.Id, DateTime.Today, rawScore: 92m);
        var rule = TestData.ArchivistRule(priority: 10, subjectId: subject.Id);
        var fileRecord = TestData.FileRecord("хэш-1", subject.Id);
        var operationLog = new FileOperationLogEntry
        {
            Id = Guid.NewGuid(),
            FileRecordId = fileRecord.Id,
            RuleId = rule.Id,
            OriginalPath = @"C:\Downloads\лекция.docx",
            PlannedPath = @"C:\Учёба\Матан\Лекция.docx",
            FinalPath = @"C:\Учёба\Матан\Лекция.docx",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Status = FileOperationLogStatus.Completed,
        };
        var deck = TestData.CardDeck(subject.Id);
        var card = TestData.Card(subject.Id, deck.Id);
        var tag = TestData.CardTag("формулы");
        var tagLink = new CardTagLink { CardId = card.Id, TagId = tag.Id };

        await using (var arrange = new StudCompDbContext(_sourceOptions))
        {
            arrange.Subjects.Add(subject);
            arrange.ScheduleEntries.Add(schedule);
            arrange.Deadlines.Add(deadline);
            arrange.GradeEntries.Add(grade);
            arrange.ArchivistRules.Add(rule);
            arrange.FileRecords.Add(fileRecord);
            arrange.FileOperationLogs.Add(operationLog);
            arrange.CardDecks.Add(deck);
            arrange.Cards.Add(card);
            arrange.CardTags.Add(tag);
            arrange.CardTagLinks.Add(tagLink);
            await arrange.SaveChangesAsync();
        }

        File.WriteAllText(_sourceSettingsPath, "{\"Rubrica\":{\"Appearance\":{\"Theme\":\"Dark\"}}}");

        var sourceSettings = new UserSettingsProvider(NullLogger<UserSettingsProvider>.Instance, _sourceSettingsPath);
        var sourceBackup = new BackupService(
            new SqliteDatabaseBackupPort(new StaticDbContextFactory(_sourceOptions), NullLogger<SqliteDatabaseBackupPort>.Instance),
            sourceSettings,
            NullLogger<BackupService>.Instance,
            new BackupLayout(_sourceDbPath, _sourceSettingsPath));

        // --- Act: первый экспорт ---
        var zip1 = Path.Combine(_root, "backup-v1.zip");
        var backupResult = await sourceBackup.BackupAsync(zip1);
        Assert.True(backupResult.IsSuccess, backupResult.IsFailure ? backupResult.Error.Message : string.Empty);

        var manifest1 = ReadManifest(zip1);
        Assert.NotEqual(Guid.Empty, manifest1.LineageId);
        Assert.Equal(1, manifest1.Version);

        // --- Act: второй экспорт — версия растёт, линия остаётся той же (прямой пункт DoD) ---
        var zip2 = Path.Combine(_root, "backup-v2.zip");
        await sourceBackup.BackupAsync(zip2);
        var manifest2 = ReadManifest(zip2);
        Assert.Equal(manifest1.LineageId, manifest2.LineageId);
        Assert.Equal(2, manifest2.Version);

        // --- Act: перенос первого архива на "чистую установку" и восстановление ---
        var targetSettings = new UserSettingsProvider(NullLogger<UserSettingsProvider>.Instance, _targetSettingsPath);
        var targetBackup = new BackupService(
            new SqliteDatabaseBackupPort(new StaticDbContextFactory(_targetOptions), NullLogger<SqliteDatabaseBackupPort>.Instance),
            targetSettings,
            NullLogger<BackupService>.Instance,
            new BackupLayout(_targetDbPath, _targetSettingsPath));

        var restoreResult = await targetBackup.RestoreAsync(zip1);
        Assert.True(restoreResult.IsSuccess, restoreResult.IsFailure ? restoreResult.Error.Message : string.Empty);

        // --- Assert: каждый вид сущности из DoD пережил перенос на реальной "чистой" БД ---
        await using var check = new StudCompDbContext(_targetOptions);

        var restoredSubject = await check.Subjects.SingleAsync(x => x.Id == subject.Id);
        Assert.Equal(subject.Name, restoredSubject.Name);
        Assert.Equal(subject.FolderPath, restoredSubject.FolderPath);

        var restoredSchedule = await check.ScheduleEntries.SingleAsync(x => x.Id == schedule.Id);
        Assert.Equal(schedule.Room, restoredSchedule.Room);
        Assert.Equal(schedule.DayOfWeek, restoredSchedule.DayOfWeek);

        var restoredDeadline = await check.Deadlines.SingleAsync(x => x.Id == deadline.Id);
        Assert.Equal(deadline.Title, restoredDeadline.Title);
        Assert.Equal(deadline.DueDate, restoredDeadline.DueDate);

        var restoredGrade = await check.GradeEntries.SingleAsync(x => x.Id == grade.Id);
        Assert.Equal(grade.RawScore, restoredGrade.RawScore);

        var restoredRule = await check.ArchivistRules.SingleAsync(x => x.Id == rule.Id);
        Assert.Equal(rule.Pattern, restoredRule.Pattern);
        Assert.Equal(rule.Priority, restoredRule.Priority);

        var restoredFileRecord = await check.FileRecords.SingleAsync(x => x.Id == fileRecord.Id);
        Assert.Equal(fileRecord.ContentHash, restoredFileRecord.ContentHash);

        var restoredLog = await check.FileOperationLogs.SingleAsync(x => x.Id == operationLog.Id);
        Assert.Equal(operationLog.FinalPath, restoredLog.FinalPath);
        Assert.Equal(FileOperationLogStatus.Completed, restoredLog.Status);

        var restoredDeck = await check.CardDecks.SingleAsync(x => x.Id == deck.Id);
        Assert.Equal(deck.Name, restoredDeck.Name);

        var restoredCard = await check.Cards.SingleAsync(x => x.Id == card.Id);
        Assert.Equal(card.Front, restoredCard.Front);
        Assert.Equal(card.Back, restoredCard.Back);
        Assert.Equal(deck.Id, restoredCard.DeckId);

        var restoredTag = await check.CardTags.SingleAsync(x => x.Id == tag.Id);
        Assert.Equal(tag.Name, restoredTag.Name);

        Assert.True(await check.CardTagLinks.AnyAsync(x => x.CardId == card.Id && x.TagId == tag.Id));

        // --- Assert: "чистая установка" унаследовала линию/версию перенесённого архива ---
        var restoredIdentity = targetSettings.Get<DataOptions>(DataOptions.SectionName);
        Assert.Equal(manifest1.LineageId, restoredIdentity.BackupLineageId);
        Assert.Equal(1, restoredIdentity.BackupVersion);

        // --- Assert: настройки (usersettings.json) тоже переехали целиком ---
        Assert.Contains("Dark", File.ReadAllText(_targetSettingsPath));
    }

    private static BackupManifest ReadManifest(string zipPath)
    {
        using var archive = ZipFile.OpenRead(zipPath);
        using var stream = archive.GetEntry(BackupManifest.ManifestEntryName)!.Open();
        return JsonSerializer.Deserialize<BackupManifest>(stream)!;
    }

    [Fact]
    public async Task Images_and_external_subject_folders_survive_transfer_to_another_computer()
    {
        var study = Path.Combine(_root, "old-study");
        var custom = Path.Combine(_root, "custom-subject");
        Directory.CreateDirectory(study);
        Directory.CreateDirectory(Path.Combine(custom, "Рисунки"));
        var photo = Path.Combine(custom, "Рисунки", "Фото #1.png");
        await File.WriteAllBytesAsync(photo, [4, 3, 2, 1]);
        await File.WriteAllTextAsync(Path.ChangeExtension(photo, ".json"), "drawing source");
        await File.WriteAllTextAsync(Path.Combine(study, "lecture.pdf"), "lecture");
        var standalone = Path.Combine(_root, "Отдельный рисунок.png");
        await File.WriteAllBytesAsync(standalone, [5, 6]);
        await File.WriteAllTextAsync(Path.ChangeExtension(standalone, ".json"), "standalone drawing");
        var note = new Note { Id = Guid.NewGuid(), Title = "Конспект", ContentMarkdown =
            $"![Фото](<{MarkdownLocalImages.Encode(photo)}>){{width=320}}\n"
            + $"![Относительный путь](<{MarkdownLocalImages.Encode(Path.GetRelativePath(study, photo))}>)\n"
            + $"![Рисунок](<{MarkdownLocalImages.Encode(standalone)}>)\n"
            + "`![Пример](missing.png)`" };
        var subject = new Subject { Id = Guid.NewGuid(), Name = "Физика", FolderPath = custom, Assessment = SubjectAssessment.Exam };
        note.SubjectId = subject.Id;
        await using (var db = new StudCompDbContext(_sourceOptions))
        {
            db.Subjects.Add(subject);
            db.Notes.Add(note);
            db.FileRecords.Add(new FileRecord { Id = Guid.NewGuid(), SubjectId = subject.Id, CurrentPath = photo });
            await db.SaveChangesAsync();
        }
        var sourceSettings = new UserSettingsProvider(NullLogger<UserSettingsProvider>.Instance, _sourceSettingsPath);
        sourceSettings.Update<WorkspaceOptions>(WorkspaceOptions.SectionName, x => x.StudyRootPath = study);
        var sourceService = new BackupService(new SqliteDatabaseBackupPort(new StaticDbContextFactory(_sourceOptions), NullLogger<SqliteDatabaseBackupPort>.Instance),
            sourceSettings, NullLogger<BackupService>.Instance, new BackupLayout(_sourceDbPath, _sourceSettingsPath));
        var zip = Path.Combine(_root, "portable.zip");
        var backup = await sourceService.BackupAsync(zip);
        Assert.True(backup.IsSuccess, backup.IsFailure ? backup.Error.Message : string.Empty);

        // Source locations no longer exist on the destination computer.
        Directory.Move(study, study + "-unavailable");
        Directory.Move(custom, custom + "-unavailable");
        var targetSettings = new UserSettingsProvider(NullLogger<UserSettingsProvider>.Instance, _targetSettingsPath);
        var targetService = new BackupService(new SqliteDatabaseBackupPort(new StaticDbContextFactory(_targetOptions), NullLogger<SqliteDatabaseBackupPort>.Instance),
            targetSettings, NullLogger<BackupService>.Instance, new BackupLayout(_targetDbPath, _targetSettingsPath));
        var result = await targetService.RestoreAsync(zip);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : string.Empty);
        await using var check = new StudCompDbContext(_targetOptions);
        var restoredSubject = await check.Subjects.SingleAsync();
        Assert.Equal(SubjectAssessment.Exam, restoredSubject.Assessment);
        Assert.StartsWith(result.Value.StudyRootPath!, restoredSubject.FolderPath);
        var restoredNote = await check.Notes.SingleAsync();
        var images = MarkdownLocalImages.Paths(restoredNote.ContentMarkdown).ToArray();
        Assert.Equal(3, images.Length);
        var image = images[0];
        Assert.Equal(new byte[] { 4, 3, 2, 1 }, await File.ReadAllBytesAsync(image));
        Assert.True(File.Exists(Path.ChangeExtension(image, ".json")));
        Assert.Equal(Path.GetFullPath(image), Path.GetFullPath(Path.Combine(result.Value.StudyRootPath!, images[1])));
        Assert.StartsWith(result.Value.StudyRootPath!, images[2].Replace('/', '\\'));
        Assert.Equal(new byte[] { 5, 6 }, await File.ReadAllBytesAsync(images[2]));
        Assert.Equal("standalone drawing", await File.ReadAllTextAsync(Path.ChangeExtension(images[2], ".json")));
        Assert.Equal(image.Replace('/', '\\'), (await check.FileRecords.SingleAsync()).CurrentPath.Replace('/', '\\'));
        Assert.Equal("lecture", await File.ReadAllTextAsync(Path.Combine(result.Value.StudyRootPath!, "lecture.pdf")));
        Assert.Contains("width=320", restoredNote.ContentMarkdown);
    }

    private sealed class StaticDbContextFactory(DbContextOptions<StudCompDbContext> options)
        : IDbContextFactory<StudCompDbContext>
    {
        public StudCompDbContext CreateDbContext() => new(options);
    }
}
