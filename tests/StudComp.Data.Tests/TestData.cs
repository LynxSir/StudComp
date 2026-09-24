using StudComp.Core.Domain;

namespace StudComp.Data.Tests;

/// <summary>
/// Фабрики валидных сущностей с заполненными обязательными полями и заранее заданными
/// <c>Id</c> (PK генерируется в коде, не базой — ARCHITECTURE §7.2).
/// </summary>
internal static class TestData
{
    public static Subject Subject(string name = "Матан") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        Code = "МА",
        FolderPath = @"C:\Учёба\Матан",
        ColorHex = "#8E2434",
    };

    public static Semester Semester(string name = "Осень 2026") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        CourseNumber = 3,
        StartDate = new DateOnly(2026, 9, 1),
        EndDate = new DateOnly(2026, 12, 27),
        FirstWeekIsOdd = true,
        IsActive = true,
        PairSlotsJson = "[]",
    };

    public static Note Note(
        Guid? subjectId = null,
        Guid? scheduleEntryId = null,
        Guid? fileRecordId = null) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subjectId,
        ScheduleEntryId = scheduleEntryId,
        LinkedFileRecordId = fileRecordId,
        Kind = NoteKind.Lecture,
        Title = "Конспект",
        ContentMarkdown = "# Конспект. Текст лекции.",
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public static ScheduleEntry ScheduleEntry(Guid subjectId) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subjectId,
        DayOfWeek = DayOfWeek.Monday,
        StartTime = new TimeOnly(9, 0),
        EndTime = new TimeOnly(10, 30),
        Room = "301",
        Teacher = "Иванов И.И.",
        Type = ScheduleEntryType.Lecture,
        WeekParity = WeekParity.Any,
    };

    public static Deadline Deadline(
        Guid subjectId,
        DateTimeOffset dueDate,
        DeadlineStatus status = DeadlineStatus.Pending,
        Guid? linkedFileRecordId = null) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subjectId,
        Title = "Лабораторная 1",
        DueDate = dueDate,
        Type = DeadlineType.Homework,
        Priority = DeadlinePriority.Normal,
        Status = status,
        LinkedFileRecordId = linkedFileRecordId,
    };

    public static GradeEntry GradeEntry(Guid subjectId, DateTime date, decimal rawScore = 80m) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subjectId,
        Type = GradeEntryType.Attestation,
        RawScore = rawScore,
        MaxScore = 100m,
        Weight = 0.5m,
        Date = date,
        Semester = 1,
    };

    public static ArchivistRule ArchivistRule(int priority, bool enabled = true, Guid? subjectId = null) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subjectId,
        Pattern = "лекция",
        MatchType = RuleMatchType.Keyword,
        Priority = priority,
        Enabled = enabled,
        RenameTemplate = "{Subject}_{Date:yyyy-MM-dd}{Ext}",
    };

    public static FileRecord FileRecord(
        string contentHash,
        Guid? subjectId = null,
        FileRecordStatus status = FileRecordStatus.Detected) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subjectId,
        OriginalPath = @"C:\Downloads\file.docx",
        CurrentPath = @"C:\Downloads\file.docx",
        ContentHash = contentHash,
        DetectedAt = DateTimeOffset.UtcNow,
        Status = status,
    };

    public static Card Card(
        Guid? subjectId = null,
        Guid? deckId = null,
        string front = "Формула Ньютона-Лейбница",
        string back = "Интеграл от a до b равен разности значений первообразной.",
        Guid? sourceNoteId = null,
        Guid? sourceFileRecordId = null) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subjectId,
        DeckId = deckId,
        SourceNoteId = sourceNoteId,
        SourceFileRecordId = sourceFileRecordId,
        Kind = CardKind.Formula,
        Front = front,
        Back = back,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public static CardDeck CardDeck(Guid? subjectId = null, string name = "К экзамену") => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subjectId,
        Name = name,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    public static CardTag CardTag(string name = "формулы") => new()
    {
        Id = Guid.NewGuid(),
        Name = name,
        DisplayName = name,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public static StudySession StudySession(Guid? subjectId = null, Guid? deckId = null) => new()
    {
        Id = Guid.NewGuid(),
        SubjectId = subjectId,
        DeckId = deckId,
        Mode = StudyMode.Practice,
        StartedAt = DateTimeOffset.UtcNow,
        FilterJson = "{}",
        Seed = 42,
    };

    public static CardReviewLog CardReviewLog(Guid cardId, Guid? sessionId = null) => new()
    {
        Id = Guid.NewGuid(),
        CardId = cardId,
        SessionId = sessionId,
        ReviewedAt = DateTimeOffset.UtcNow,
        Grade = ReviewGrade.Good,
        Mode = StudyMode.Practice,
        ElapsedMs = 1200,
    };

    public static ReportTemplate ReportTemplate() => new()
    {
        Id = Guid.NewGuid(),
        Name = "ГОСТ 7.32-2017",
        GostVariant = "7.32-2017",
        StyleProfileJson = "{}",
    };

    public static ReportJob ReportJob(Guid templateId, DateTimeOffset createdAt, Guid? subjectId = null) => new()
    {
        Id = Guid.NewGuid(),
        TemplateId = templateId,
        SubjectId = subjectId,
        SourcePath = @"C:\report.md",
        OutputPath = @"C:\report.docx",
        CreatedAt = createdAt,
        Status = ReportJobStatus.Rendered,
    };
}
