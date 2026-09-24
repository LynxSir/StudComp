using System.Globalization;
using System.Windows.Media;
using StudComp.Core.Domain;

namespace StudComp.ViewModels.Organizer;

/// <summary>
/// Строка списка дедлайнов. Статус <c>Overdue</c> — производный (<c>Pending</c> + срок в прошлом),
/// не хранится (ARCHITECTURE §9.3).
/// </summary>
public sealed class DeadlineRowViewModel
{
    private static readonly SolidColorBrush OverdueBrush = Frozen(Color.FromRgb(0xC0, 0x37, 0x2A));
    private static readonly SolidColorBrush DoneBrush = Frozen(Color.FromRgb(0x2E, 0x7D, 0x32));
    private static readonly SolidColorBrush PendingBrush = Frozen(Color.FromRgb(0x6B, 0x6B, 0x6B));

    public DeadlineRowViewModel(
        Deadline deadline,
        string subjectName,
        string? subjectColorHex,
        DateTimeOffset now,
        string? linkedFilePath = null,
        int attachmentCount = 0)
    {
        Deadline = deadline;
        AttachmentCount = attachmentCount;
        HasAnswer = deadline.AnsweredAt is not null;
        AnsweredText = deadline.AnsweredAt is { } answeredAt
            ? "Сдано " + answeredAt.LocalDateTime.ToString($"{Resources.AppDateFormat.ShortDate} HH:mm", CultureInfo.InvariantCulture)
            : string.Empty;
        SubjectName = subjectName;
        AccentBrush = SubjectColor.BrushFor(subjectColorHex);

        LinkedFilePath = linkedFilePath;
        LinkedFileName = linkedFilePath is null ? string.Empty : System.IO.Path.GetFileName(linkedFilePath);
        HasLinkedFile = linkedFilePath is not null;

        IsDone = deadline.Status == DeadlineStatus.Done;
        IsOverdue = deadline.Status == DeadlineStatus.Pending && deadline.DueDate < now;
        // «Срочный» — не просроченный Pending со сроком в ближайшие 48 часов (тонкая золотая рамка, §19.2).
        IsUrgent = deadline.Status == DeadlineStatus.Pending && !IsOverdue && deadline.DueDate <= now.AddHours(48);

        DueText = deadline.DueDate.LocalDateTime.ToString("dd.MM.yyyy HH:mm", CultureInfo.GetCultureInfo("ru-RU"));
        TypeText = OrganizerChoices.DeadlineTypes.First(t => t.Value == deadline.Type).Display;
        PriorityText = OrganizerChoices.Priorities.First(p => p.Value == deadline.Priority).Display;

        (StatusText, StatusBrush) = IsDone
            ? ("Готово", DoneBrush)
            : IsOverdue
                ? ("Просрочен", OverdueBrush)
                : ("Ожидается", PendingBrush);
    }

    public Deadline Deadline { get; }

    public string SubjectName { get; }

    public Brush AccentBrush { get; }

    public string Title => Deadline.Title;

    /// <summary>Срок — нужен группировке по неделям и календарной сетке.</summary>
    public DateTimeOffset DueDate => Deadline.DueDate;

    public bool IsDone { get; }

    /// <summary>Подпись кнопки переключения статуса.</summary>
    public string ToggleDoneText => IsDone ? "Вернуть" : "Готово";

    public bool IsOverdue { get; }

    /// <summary>
    /// Дедлайн-экзамен: только у него есть смысл в кнопке «Готовиться» — она уводит в аврал
    /// по карточкам предмета (new_addons.md §2.3, §6.4).
    /// </summary>
    public bool IsExam => Deadline.Type == DeadlineType.Exam && !IsDone;

    public bool IsUrgent { get; }

    public string DueText { get; }

    public string TypeText { get; }

    public string PriorityText { get; }

    public string StatusText { get; }

    public Brush StatusBrush { get; }

    /// <summary>
    /// Файл, привязанный к дедлайну архивариусом (ARCHITECTURE §9.3). Без этой строки привязка была бы
    /// невидимой сразу после того, как тост-предложение пропал с экрана.
    /// </summary>
    public bool HasLinkedFile { get; }

    public string LinkedFileName { get; }

    public string? LinkedFilePath { get; }

    /// <summary>Число приложенных файлов (материалы + ответ) — бейдж на карточке.</summary>
    public int AttachmentCount { get; }

    public bool HasAttachments => AttachmentCount > 0;

    /// <summary>Работа отмечена сданной со страницы дедлайна.</summary>
    public bool HasAnswer { get; }

    public string AnsweredText { get; }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }
}
