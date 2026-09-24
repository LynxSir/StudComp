using System.Text;
using System.Text.RegularExpressions;

namespace StudComp.Core.Domain;

/// <summary>
/// Правка текста как замена диапазона: так она ложится на <c>TextBox.Select</c> +
/// <c>SelectedText</c> и сохраняет стек отмены. Присваивать <c>TextBox.Text</c> целиком нельзя —
/// это сносит всю историю Ctrl+Z.
/// </summary>
/// <param name="Start">Начало заменяемого куска в исходном тексте.</param>
/// <param name="Length">Длина заменяемого куска; ноль — вставка.</param>
/// <param name="Replacement">Чем заменить.</param>
/// <param name="CaretOffset">Куда поставить каретку относительно <paramref name="Start"/>.</param>
/// <param name="SelectionLength">Сколько символов выделить от каретки; ноль — просто каретка.</param>
public readonly record struct MarkdownEdit(
    int Start,
    int Length,
    string Replacement,
    int CaretOffset,
    int SelectionLength = 0);

/// <summary>
/// Автосинтаксис Markdown при вводе (new_addons.md §11.2, §11.3, §11.6): продолжение списков,
/// вложенность, обёртка выделения, перенос по клику ниже текста, парные скобки и кавычки.
/// </summary>
/// <remarks>
/// Вся логика живёт здесь, а не в поведении над <c>TextBox</c>, ровно ради тестируемости —
/// DoD фазы 13.6 требует покрыть разбор автосинтаксиса тестами, а проекта <c>App.Tests</c>
/// в решении нет. Поведение остаётся тонкой обёрткой: спросить, применить, поставить каретку.
/// Каждый метод возвращает <see langword="null"/>, когда вмешиваться не нужно — тогда клавиша
/// отрабатывает штатно.
/// </remarks>
public static partial class MarkdownEditing
{
    /// <summary>Отступ одного уровня вложенности списка.</summary>
    private const string IndentUnit = "  ";

    /// <summary>Маркер строки списка: отступ, сам маркер, необязательный чекбокс и содержимое.</summary>
    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>[-*+]|\d+[.)])(?<space>[ \t]+)(?<task>\[[ xX]\][ \t]+)?(?<rest>.*)$")]
    private static partial Regex ListLinePattern();

    /// <summary>Строка цитаты: отступ и одна или несколько «птичек».</summary>
    [GeneratedRegex(@"^(?<indent>[ \t]*)(?<marker>>+)(?<space>[ \t]*)(?<rest>.*)$")]
    private static partial Regex QuoteLinePattern();

    /// <summary>
    /// Нажат <c>Enter</c>: продолжить список или цитату тем же маркером. Пустой пункт списка
    /// вместо новой строки снимает маркер — так из списка выходят одним лишним Enter'ом,
    /// как в любом привычном Markdown-редакторе.
    /// </summary>
    public static MarkdownEdit? ContinueLine(string? text, int caret)
    {
        var source = text ?? string.Empty;
        caret = Math.Clamp(caret, 0, source.Length);

        var lineStart = LineStart(source, caret);
        var lineEnd = LineEnd(source, caret);
        var line = source[lineStart..lineEnd];

        // Каретка в середине строки — пусть Enter просто разрежет её, без самодеятельности.
        if (caret < lineEnd)
        {
            return null;
        }

        if (ListLinePattern().Match(line) is { Success: true } list)
        {
            var indent = list.Groups["indent"].Value;
            var marker = list.Groups["marker"].Value;
            var space = list.Groups["space"].Value;
            var task = list.Groups["task"].Value;
            var rest = list.Groups["rest"].Value;

            if (rest.Length == 0)
            {
                // Пустой пункт: выходим из списка, стирая его маркер целиком.
                return new MarkdownEdit(lineStart, line.Length, string.Empty, 0);
            }

            var nextMarker = NextMarker(marker);
            var nextTask = task.Length == 0 ? string.Empty : "[ ] ";
            var inserted = $"\n{indent}{nextMarker}{space}{nextTask}";

            return new MarkdownEdit(caret, 0, inserted, inserted.Length);
        }

        if (QuoteLinePattern().Match(line) is { Success: true } quote)
        {
            var indent = quote.Groups["indent"].Value;
            var marker = quote.Groups["marker"].Value;
            var rest = quote.Groups["rest"].Value;

            if (rest.Length == 0)
            {
                return new MarkdownEdit(lineStart, line.Length, string.Empty, 0);
            }

            var inserted = $"\n{indent}{marker} ";
            return new MarkdownEdit(caret, 0, inserted, inserted.Length);
        }

        return null;
    }

    /// <summary>
    /// <c>Tab</c> / <c>Shift+Tab</c> внутри списка: сдвинуть строку (или все строки выделения)
    /// на уровень вложенности. Вне списка возвращает <see langword="null"/> — <c>Tab</c>
    /// продолжает работать как обычно.
    /// </summary>
    public static MarkdownEdit? Indent(string? text, int selectionStart, int selectionLength, bool outdent)
    {
        var source = text ?? string.Empty;
        selectionStart = Math.Clamp(selectionStart, 0, source.Length);
        selectionLength = Math.Clamp(selectionLength, 0, source.Length - selectionStart);

        var blockStart = LineStart(source, selectionStart);
        var blockEnd = LineEnd(source, selectionStart + selectionLength);
        var block = source[blockStart..blockEnd];
        var lines = block.Split('\n');

        // Сдвигаем только списки: отступ обычного абзаца в Markdown означает листинг, а это
        // совсем не то, чего ждёт человек, нажимая Tab посреди текста.
        if (!lines.Any(line => ListLinePattern().IsMatch(line)))
        {
            return null;
        }

        var builder = new StringBuilder(block.Length + (lines.Length * IndentUnit.Length));
        var changed = false;

        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            var line = lines[i];
            if (!ListLinePattern().IsMatch(line))
            {
                builder.Append(line);
                continue;
            }

            if (outdent)
            {
                var removed = RemoveIndentUnit(line);
                changed |= removed.Length != line.Length;
                builder.Append(removed);
            }
            else
            {
                changed = true;
                builder.Append(IndentUnit).Append(line);
            }
        }

        if (!changed)
        {
            return null;
        }

        var replacement = builder.ToString();
        return new MarkdownEdit(blockStart, block.Length, replacement, 0, replacement.Length);
    }

    /// <summary>
    /// Обернуть выделение парным маркером (<c>**</c> для жирного, <c>*</c> для курсива) либо снять
    /// уже стоящую обёртку. Без выделения вставляет пустую пару и ставит каретку внутрь.
    /// </summary>
    public static MarkdownEdit? ToggleWrap(string? text, int selectionStart, int selectionLength, string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        var source = text ?? string.Empty;
        selectionStart = Math.Clamp(selectionStart, 0, source.Length);
        selectionLength = Math.Clamp(selectionLength, 0, source.Length - selectionStart);

        if (selectionLength == 0)
        {
            return new MarkdownEdit(selectionStart, 0, token + token, token.Length);
        }

        var selected = source.Substring(selectionStart, selectionLength);

        // Обёртка внутри выделения: «**текст**» → «текст».
        if (selected.Length >= token.Length * 2
            && selected.StartsWith(token, StringComparison.Ordinal)
            && selected.EndsWith(token, StringComparison.Ordinal))
        {
            var stripped = selected[token.Length..^token.Length];
            return new MarkdownEdit(selectionStart, selectionLength, stripped, 0, stripped.Length);
        }

        // Обёртка снаружи выделения: пользователь выделил «текст» внутри «**текст**».
        var outerStart = selectionStart - token.Length;
        var outerEnd = selectionStart + selectionLength + token.Length;
        if (outerStart >= 0
            && outerEnd <= source.Length
            && string.CompareOrdinal(source, outerStart, token, 0, token.Length) == 0
            && string.CompareOrdinal(source, selectionStart + selectionLength, token, 0, token.Length) == 0)
        {
            return new MarkdownEdit(outerStart, outerEnd - outerStart, selected, 0, selected.Length);
        }

        var wrapped = token + selected + token;
        return new MarkdownEdit(selectionStart, selectionLength, wrapped, token.Length, selected.Length);
    }

    /// <summary>
    /// Клик по пустому месту ниже последней строки (new_addons.md §11.6): дописать перенос строки
    /// и встать на новую строку. Если текст пуст или уже кончается переносом — вмешиваться незачем.
    /// </summary>
    public static MarkdownEdit? AppendTrailingLine(string? text)
    {
        var source = text ?? string.Empty;
        if (source.Length == 0 || source.EndsWith('\n'))
        {
            return null;
        }

        return new MarkdownEdit(source.Length, 0, "\n", 1);
    }

    /// <summary>Пары «открывающий → закрывающий». Симметричные токены закрываются собой же.</summary>
    private static readonly (char Open, char Close)[] Pairs =
    [
        ('(', ')'), ('[', ']'), ('{', '}'), ('"', '"'), ('$', '$'), ('`', '`'), ('*', '*'), ('_', '_'),
    ];

    /// <summary>
    /// Набран один символ (new_addons.md §11.3, помощник ввода как в Obsidian):
    /// открывающая скобка ставит и закрывающую с кареткой между ними; символ обёртки вокруг
    /// выделения оборачивает его; набор закрывающей, когда она уже стоит под кареткой,
    /// перепрыгивает через неё; второй <c>$</c> в пустой паре <c>$|$</c> даёт <c>$$|$$</c>.
    /// Возвращает <see langword="null"/>, когда символ надо вставить как есть.
    /// </summary>
    public static MarkdownEdit? AutoPair(string? text, int selectionStart, int selectionLength, string? typed)
    {
        if (typed is not { Length: 1 })
        {
            return null;
        }

        var source = text ?? string.Empty;
        selectionStart = Math.Clamp(selectionStart, 0, source.Length);
        selectionLength = Math.Clamp(selectionLength, 0, source.Length - selectionStart);
        var typedChar = typed[0];

        var opening = Pairs.FirstOrDefault(pair => pair.Open == typedChar);
        var isOpening = opening.Open == typedChar;
        var isClosing = Pairs.Any(pair => pair.Close == typedChar);
        var isSymmetric = isOpening && opening.Open == opening.Close;

        if (!isOpening && !isClosing)
        {
            return null;
        }

        // 1. Выделение + открывающий — обёртка, выделение остаётся на содержимом.
        if (selectionLength > 0)
        {
            if (!isOpening)
            {
                return null;
            }

            var selected = source.Substring(selectionStart, selectionLength);
            var wrapped = opening.Open + selected + opening.Close;
            return new MarkdownEdit(selectionStart, selectionLength, wrapped, 1, selected.Length);
        }

        var caret = selectionStart;
        var next = caret < source.Length ? source[caret] : '\0';
        var previous = caret > 0 ? source[caret - 1] : '\0';

        // 2. Симметричный токен внутри своей же пустой пары: $|$ → $$|$$, *|* → **|**.
        if (isSymmetric && next == typedChar && previous == typedChar)
        {
            return new MarkdownEdit(caret, 1, new string(typedChar, 3), 1);
        }

        // 3. Закрывающий уже стоит под кареткой — перепрыгнуть, ничего не вставляя.
        if (isClosing && next == typedChar)
        {
            return new MarkdownEdit(caret, 1, typed, 1);
        }

        if (!isOpening)
        {
            return null;
        }

        // 4. Вставка пары — только перед концом строки, пробелом или закрывающей скобкой:
        //    внутри слова (напр. «f(x») закрывающую подставлять нельзя.
        if (!(next is '\0' or '\n' or '\r' || char.IsWhiteSpace(next) || next is ')' or ']' or '}' or ',' or '.' or ';' or ':'))
        {
            return null;
        }

        if (isSymmetric)
        {
            // «5$ и 10$», «слово"» — это не начало обёртки.
            if (char.IsLetterOrDigit(previous))
            {
                return null;
            }

            // «* » в начале строки — маркер списка, а не начало курсива.
            if (typedChar is '*' or '_' && IsLineStartSoFar(source, caret))
            {
                return null;
            }
        }

        return new MarkdownEdit(caret, 0, new string([opening.Open, opening.Close]), 1);
    }

    /// <summary>
    /// <c>Backspace</c> внутри пустой пары (<c>(|)</c>, <c>$|$</c>) удаляет оба символа разом.
    /// В любом другом месте — <see langword="null"/>, штатное удаление одного символа.
    /// </summary>
    public static MarkdownEdit? DeletePair(string? text, int caret)
    {
        var source = text ?? string.Empty;
        if (caret <= 0 || caret >= source.Length)
        {
            return null;
        }

        var previous = source[caret - 1];
        var next = source[caret];
        return Pairs.Any(pair => pair.Open == previous && pair.Close == next)
            ? new MarkdownEdit(caret - 1, 2, string.Empty, 0)
            : null;
    }

    /// <summary>
    /// <c>Enter</c> между <c>$$</c> и <c>$$</c>: развести выключную формулу по строкам,
    /// каретка — на пустую среднюю строку.
    /// </summary>
    public static MarkdownEdit? SplitDisplayMath(string? text, int caret)
    {
        var source = text ?? string.Empty;
        caret = Math.Clamp(caret, 0, source.Length);

        if (caret < 2 || caret + 2 > source.Length
            || source[caret - 1] != '$' || source[caret - 2] != '$'
            || source[caret] != '$' || source[caret + 1] != '$')
        {
            return null;
        }

        return new MarkdownEdit(caret, 0, "\n\n", 1);
    }

    /// <summary>До каретки на этой строке — только пробелы (или ничего).</summary>
    private static bool IsLineStartSoFar(string source, int caret)
    {
        for (var i = LineStart(source, caret); i < caret; i++)
        {
            if (!char.IsWhiteSpace(source[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Следующий маркер списка: у нумерованного номер растёт, у маркированного — тот же знак.</summary>
    private static string NextMarker(string marker)
    {
        var suffix = marker[^1];
        if (suffix is not ('.' or ')'))
        {
            return marker;
        }

        return int.TryParse(marker[..^1], out var number)
            ? string.Create(null, $"{number + 1}{suffix}")
            : marker;
    }

    private static string RemoveIndentUnit(string line)
    {
        if (line.StartsWith(IndentUnit, StringComparison.Ordinal))
        {
            return line[IndentUnit.Length..];
        }

        if (line.Length > 0 && (line[0] == '\t' || line[0] == ' '))
        {
            return line[1..];
        }

        return line;
    }

    private static int LineStart(string source, int position)
    {
        if (position <= 0 || source.Length == 0)
        {
            return 0;
        }

        var index = source.LastIndexOf('\n', position - 1);
        return index < 0 ? 0 : index + 1;
    }

    private static int LineEnd(string source, int position)
    {
        var index = source.IndexOf('\n', Math.Min(position, source.Length));
        return index < 0 ? source.Length : index;
    }
}
