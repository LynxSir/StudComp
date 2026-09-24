using StudComp.Core.Abstractions.ReportForge;

namespace StudComp.Core.Domain;

/// <summary>Один кандидат в карточку, найденный при разборе заметки.</summary>
/// <param name="Front">Лицевая сторона будущей карточки.</param>
/// <param name="Back">Оборот будущей карточки.</param>
/// <param name="Kind">Тип карточки, подсказанный шаблоном.</param>
/// <param name="Pattern">Каким шаблоном найден — для тестов и диагностики, на данные не влияет.</param>
public sealed record NoteCardCandidate(string Front, string Back, CardKind Kind, NoteCardPattern Pattern);

/// <summary>Шаблон, по которому распознан кандидат (new_addons.md §7.3).</summary>
public enum NoteCardPattern
{
    /// <summary><c>**Термин** — определение</c>.</summary>
    BoldTerm = 0,

    /// <summary>Заголовок, за которым сразу следует абзац.</summary>
    HeadingAndParagraph = 1,

    /// <summary>Строка списка вида <c>Термин: определение</c>.</summary>
    ListTerm = 2,

    /// <summary>Абзац-вопрос (кончается на «?»), за которым сразу следует абзац-ответ.</summary>
    Question = 3,
}

/// <summary>
/// Разбор заметки на кандидатов в карточки (new_addons.md §7.3) — чистая функция на голом BCL.
/// </summary>
/// <remarks>
/// Принимает уже готовую <see cref="ReportDocumentModel"/> — ту же блочную модель, что строит
/// <c>IMarkdownDocumentModelBuilder.Build</c> для предпросмотра заметки и для генератора отчётов; сам
/// Markdig сюда не тянется (<c>Core</c> — только BCL, ADR §16.4 держит парсинг и модель раздельно).
/// Распознаёт четыре шаблона таблицы §7.3; курсор по блокам движется только вперёд — один и тот же блок
/// не становится и «фронтом», и «оборотом» двух разных кандидатов. Непонятный блок просто не порождает
/// кандидата — исключений на пользовательском тексте не бывает.
/// </remarks>
public static class NoteToCardParser
{
    /// <summary>Не искать шаблон «Термин: определение» дальше этой позиции — иначе двоеточие в середине обычного предложения даёт ложное срабатывание.</summary>
    private const int ListTermMaxColonPosition = 60;

    public static IReadOnlyList<NoteCardCandidate> Parse(ReportDocumentModel model)
    {
        var candidates = new List<NoteCardCandidate>();
        var blocks = model.Blocks;
        var i = 0;

        while (i < blocks.Count)
        {
            switch (blocks[i])
            {
                case ParagraphBlock paragraph when TryBoldTerm(paragraph, out var boldCandidate):
                    candidates.Add(boldCandidate);
                    i++;
                    continue;

                case HeadingBlock heading when i + 1 < blocks.Count && blocks[i + 1] is ParagraphBlock next:
                {
                    var back = FlattenText(next).Trim();
                    var front = heading.Text.Trim();
                    if (front.Length > 0 && back.Length > 0)
                    {
                        candidates.Add(new NoteCardCandidate(front, back, CardKind.Term, NoteCardPattern.HeadingAndParagraph));
                        i += 2;
                        continue;
                    }

                    i++;
                    continue;
                }

                case ParagraphBlock question
                    when FlattenText(question).TrimEnd().EndsWith('?')
                        && i + 1 < blocks.Count && blocks[i + 1] is ParagraphBlock answer:
                {
                    var front = FlattenText(question).Trim();
                    var back = FlattenText(answer).Trim();
                    if (front.Length > 0 && back.Length > 0)
                    {
                        candidates.Add(new NoteCardCandidate(front, back, CardKind.Question, NoteCardPattern.Question));
                        i += 2;
                        continue;
                    }

                    i++;
                    continue;
                }

                case ListBlock list:
                    foreach (var item in list.Items)
                    {
                        if (TryListTerm(item, out var listCandidate))
                        {
                            candidates.Add(listCandidate);
                        }
                    }

                    i++;
                    continue;

                default:
                    i++;
                    continue;
            }
        }

        return candidates;
    }

    /// <summary><c>**Термин** — определение</c>: первый run жирный, остаток начинается с явного разделителя.</summary>
    private static bool TryBoldTerm(ParagraphBlock paragraph, out NoteCardCandidate candidate)
    {
        candidate = null!;

        if (paragraph.Runs.Count < 2 || !paragraph.Runs[0].Style.HasFlag(InlineStyle.Bold))
        {
            return false;
        }

        var front = paragraph.Runs[0].Text.Trim();
        var rest = string.Concat(paragraph.Runs.Skip(1).Select(r => r.Text)).TrimStart();

        if (front.Length == 0 || !TryStripSeparator(rest, out var back) || back.Length == 0)
        {
            return false;
        }

        candidate = new NoteCardCandidate(front, back, CardKind.Term, NoteCardPattern.BoldTerm);
        return true;
    }

    /// <summary>Строка списка «Термин: определение» — двоеточие в первой половине строки, без знаков конца предложения перед ним.</summary>
    private static bool TryListTerm(ReportDocumentModel item, out NoteCardCandidate candidate)
    {
        candidate = null!;

        if (item.Blocks.Count == 0 || item.Blocks[0] is not ParagraphBlock paragraph)
        {
            return false;
        }

        var text = FlattenText(paragraph).Trim();
        var colon = text.IndexOf(':');
        if (colon <= 0 || colon > ListTermMaxColonPosition)
        {
            return false;
        }

        var beforeColon = text[..colon];
        if (beforeColon.IndexOfAny(['.', '?', '!']) >= 0)
        {
            return false;
        }

        var front = beforeColon.Trim();
        var back = text[(colon + 1)..].Trim();
        if (front.Length == 0 || back.Length == 0)
        {
            return false;
        }

        candidate = new NoteCardCandidate(front, back, CardKind.Term, NoteCardPattern.ListTerm);
        return true;
    }

    /// <summary>Отделить разделитель («—», «–», «-» или «:») от начала строки, если он там есть.</summary>
    private static bool TryStripSeparator(string text, out string remainder)
    {
        remainder = string.Empty;
        if (text.Length == 0)
        {
            return false;
        }

        var first = text[0];
        if (first is not ('—' or '–' or '-' or ':'))
        {
            return false;
        }

        remainder = text[1..].TrimStart();
        return true;
    }

    private static string FlattenText(ParagraphBlock paragraph) =>
        string.Concat(paragraph.Runs.Select(r => r.Text));
}
