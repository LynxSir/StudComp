using System.Collections.Frozen;
using System.Text;

namespace StudComp.Core.Domain;

/// <summary>Узел разобранной формулы. Дерево иммутабельное и про способ отрисовки ничего не знает.</summary>
public abstract record MathNode;

/// <summary>Несколько узлов подряд — тело формулы, группы <c>{…}</c>, числитель и так далее.</summary>
public sealed record MathSequence(IReadOnlyList<MathNode> Items) : MathNode;

/// <summary>Обычный текст: числа, буквы, знаки препинания.</summary>
public sealed record MathText(string Text) : MathNode;

/// <summary>Готовый символ по команде: <c>\alpha</c> → «α», <c>\times</c> → «×».</summary>
public sealed record MathSymbol(string Glyph) : MathNode;

/// <summary>Основание с верхним и/или нижним индексом.</summary>
public sealed record MathScript(MathNode Base, MathNode? Sup, MathNode? Sub) : MathNode;

/// <summary>Дробь <c>\frac{числитель}{знаменатель}</c> или <c>(a+b)/(c+d)</c>.</summary>
public sealed record MathFraction(MathNode Numerator, MathNode Denominator) : MathNode;

/// <summary>Корень <c>\sqrt{…}</c> / <c>sqrt(…)</c>, при наличии — со степенью <c>\sqrt[n]{…}</c>.</summary>
public sealed record MathSqrt(MathNode Radicand, MathNode? Index) : MathNode;

/// <summary>Выражение в круглых скобках <c>(…)</c>: скобки остаются на экране, но содержимое — единый узел.</summary>
public sealed record MathGroup(MathNode Inner) : MathNode;

/// <summary>Выражение в рамке <c>\boxed{…}</c>.</summary>
public sealed record MathBoxed(MathNode Inner) : MathNode;

/// <summary>Вид надстрочного/подстрочного украшения.</summary>
public enum MathAccentKind
{
    Overline,
    Underline,
    Vec,
    Hat,
    Bar,
    Dot,
    Ddot,
    Tilde,
}

/// <summary>Узел с украшением: <c>\overline{AB}</c>, <c>\vec{v}</c>, <c>\hat{x}</c>.</summary>
public sealed record MathAccent(MathNode Inner, MathAccentKind Kind) : MathNode;

/// <summary>Способ начертания фрагмента.</summary>
public enum MathTextStyle
{
    /// <summary>Прямой шрифт, буквы не курсивятся: <c>\mathrm</c>, <c>\operatorname</c>.</summary>
    Upright,

    /// <summary>Полужирный: <c>\mathbf</c>.</summary>
    Bold,

    /// <summary>Обычный текст внутри формулы, пробелы сохраняются: <c>\text{…}</c>.</summary>
    Text,
}

/// <summary>Фрагмент с явно заданным начертанием.</summary>
public sealed record MathStyled(MathNode Inner, MathTextStyle Style) : MathNode;

/// <summary>Биномиальный коэффициент <c>\binom{n}{k}</c>: столбик без черты в скобках.</summary>
public sealed record MathBinomial(MathNode Top, MathNode Bottom) : MathNode;

/// <summary>Обрамление матрицы.</summary>
public enum MathMatrixKind
{
    Plain,
    Paren,
    Bracket,
    Cases,
}

/// <summary>Таблица <c>\begin{matrix}…\end{matrix}</c>; <c>cases</c> — система с фигурной скобкой слева.</summary>
public sealed record MathMatrix(IReadOnlyList<IReadOnlyList<MathNode>> Rows, MathMatrixKind Kind) : MathNode;

/// <summary>Перенос строки <c>\\</c> вне матрицы.</summary>
public sealed record MathBreak : MathNode;

/// <summary>
/// То, чего разборщик не знает: незнакомая команда со всеми её аргументами. Рендерер показывает
/// такое моноширинным исходником — пользователь видит, что он написал, а не пустоту.
/// </summary>
public sealed record MathUnknown(string Source) : MathNode;

/// <summary>Строка справочника команд: имя без <c>\</c>, глиф и группа для шпаргалки.</summary>
public sealed record MathCommandInfo(string Name, string Glyph, string Group);

/// <summary>
/// Разбор формул из <c>$…$</c> и <c>$$…$$</c> (new_addons.md §11.1): подмножество TeX плюс
/// «естественная» запись без обратной косой — <c>(a+b)/(c+d)</c>, <c>sqrt(x)</c>, <c>x^(n+1)</c>,
/// <c>omega</c>, <c>*</c> как умножение, <c>-&gt;</c>, <c>&lt;=</c>, <c>...</c>.
/// Чистая функция на голом BCL, покрывается таблично — соседи по папке: ClozeParser, WikiLinkParser.
/// </summary>
/// <remarks>
/// Полноценного TeX-движка здесь нет и не планируется: решением владельца фаза 13.6 закрывает
/// «самые частые конструкции без нового пакета» (§17 вопрос 3). Всё, что в подмножество не вошло,
/// доезжает до экрана исходником через <see cref="MathUnknown"/>.
/// Правило дроби через <c>/</c> одно и предсказуемое: дробь появляется только там, где хотя бы одна
/// сторона взята в круглые скобки; <c>1/2</c> и <c>км/ч</c> остаются косой чертой.
/// Слова без <c>\</c> распознаются только как греческие буквы и <c>infty</c>: иначе обычные слова
/// вроде «in» или «to» превращались бы в знаки.
/// Вход — текст, написанный человеком, поэтому разбор НИКОГДА не бросает: незакрытая скобка,
/// одинокий <c>^</c> и мусорная команда остаются текстом.
/// </remarks>
public static class MathExpression
{
    private const string GreekLower = "Греческие строчные";
    private const string GreekUpper = "Греческие прописные";

    /// <summary>Команды без аргументов: подставляется готовый символ Unicode. Группа — для шпаргалки.</summary>
    private static readonly (string Group, string Name, string Glyph)[] SymbolTable =
    [
        (GreekLower, "alpha", "α"), (GreekLower, "beta", "β"), (GreekLower, "gamma", "γ"),
        (GreekLower, "delta", "δ"), (GreekLower, "epsilon", "ε"), (GreekLower, "varepsilon", "ϵ"),
        (GreekLower, "zeta", "ζ"), (GreekLower, "eta", "η"), (GreekLower, "theta", "θ"),
        (GreekLower, "vartheta", "ϑ"), (GreekLower, "iota", "ι"), (GreekLower, "kappa", "κ"),
        (GreekLower, "lambda", "λ"), (GreekLower, "mu", "μ"), (GreekLower, "nu", "ν"),
        (GreekLower, "xi", "ξ"), (GreekLower, "pi", "π"), (GreekLower, "varpi", "ϖ"),
        (GreekLower, "rho", "ρ"), (GreekLower, "varrho", "ϱ"), (GreekLower, "sigma", "σ"),
        (GreekLower, "varsigma", "ς"), (GreekLower, "tau", "τ"), (GreekLower, "upsilon", "υ"),
        (GreekLower, "phi", "φ"), (GreekLower, "varphi", "ϕ"), (GreekLower, "chi", "χ"),
        (GreekLower, "psi", "ψ"), (GreekLower, "omega", "ω"),

        (GreekUpper, "Gamma", "Γ"), (GreekUpper, "Delta", "Δ"), (GreekUpper, "Theta", "Θ"),
        (GreekUpper, "Lambda", "Λ"), (GreekUpper, "Xi", "Ξ"), (GreekUpper, "Pi", "Π"),
        (GreekUpper, "Sigma", "Σ"), (GreekUpper, "Upsilon", "Υ"), (GreekUpper, "Phi", "Φ"),
        (GreekUpper, "Psi", "Ψ"), (GreekUpper, "Omega", "Ω"),

        ("Операции", "times", "×"), ("Операции", "div", "÷"), ("Операции", "cdot", "·"),
        ("Операции", "ast", "∗"), ("Операции", "bullet", "•"), ("Операции", "pm", "±"),
        ("Операции", "mp", "∓"), ("Операции", "circ", "∘"), ("Операции", "star", "⋆"),
        ("Операции", "otimes", "⊗"), ("Операции", "oplus", "⊕"), ("Операции", "ominus", "⊖"),
        ("Операции", "odot", "⊙"), ("Операции", "oslash", "⊘"), ("Операции", "dagger", "†"),
        ("Операции", "ddagger", "‡"),

        ("Отношения", "le", "≤"), ("Отношения", "leq", "≤"), ("Отношения", "ge", "≥"),
        ("Отношения", "geq", "≥"), ("Отношения", "ne", "≠"), ("Отношения", "neq", "≠"),
        ("Отношения", "approx", "≈"), ("Отношения", "equiv", "≡"), ("Отношения", "sim", "∼"),
        ("Отношения", "simeq", "≃"), ("Отношения", "cong", "≅"), ("Отношения", "propto", "∝"),
        ("Отношения", "ll", "≪"), ("Отношения", "gg", "≫"), ("Отношения", "perp", "⊥"),
        ("Отношения", "parallel", "∥"), ("Отношения", "nparallel", "∦"), ("Отношения", "mid", "∣"),

        ("Крупные операторы", "sum", "∑"), ("Крупные операторы", "SUM", "∑"),
        ("Крупные операторы", "prod", "∏"), ("Крупные операторы", "coprod", "∐"),
        ("Крупные операторы", "int", "∫"), ("Крупные операторы", "iint", "∬"),
        ("Крупные операторы", "iiint", "∭"), ("Крупные операторы", "iiiint", "⨌"),
        ("Крупные операторы", "oint", "∮"),

        ("Стрелки", "to", "→"), ("Стрелки", "rightarrow", "→"), ("Стрелки", "leftarrow", "←"),
        ("Стрелки", "leftrightarrow", "↔"), ("Стрелки", "uparrow", "↑"), ("Стрелки", "downarrow", "↓"),
        ("Стрелки", "Rightarrow", "⇒"), ("Стрелки", "Leftarrow", "⇐"), ("Стрелки", "Leftrightarrow", "⇔"),
        ("Стрелки", "implies", "⇒"), ("Стрелки", "iff", "⇔"), ("Стрелки", "mapsto", "↦"),
        ("Стрелки", "longrightarrow", "⟶"), ("Стрелки", "longleftarrow", "⟵"),
        ("Стрелки", "Longrightarrow", "⟹"), ("Стрелки", "hookrightarrow", "↪"), ("Стрелки", "leadsto", "⇝"),

        ("Множества и логика", "in", "∈"), ("Множества и логика", "notin", "∉"),
        ("Множества и логика", "ni", "∋"), ("Множества и логика", "subset", "⊂"),
        ("Множества и логика", "subseteq", "⊆"), ("Множества и логика", "subsetneq", "⊊"),
        ("Множества и логика", "supset", "⊃"), ("Множества и логика", "supseteq", "⊇"),
        ("Множества и логика", "supsetneq", "⊋"), ("Множества и логика", "cup", "∪"),
        ("Множества и логика", "cap", "∩"), ("Множества и логика", "setminus", "\\"),
        ("Множества и логика", "emptyset", "∅"), ("Множества и логика", "varnothing", "∅"),
        ("Множества и логика", "forall", "∀"), ("Множества и логика", "exists", "∃"),
        ("Множества и логика", "nexists", "∄"), ("Множества и логика", "neg", "¬"),
        ("Множества и логика", "lnot", "¬"), ("Множества и логика", "land", "∧"),
        ("Множества и логика", "wedge", "∧"), ("Множества и логика", "lor", "∨"),
        ("Множества и логика", "vee", "∨"), ("Множества и логика", "top", "⊤"),
        ("Множества и логика", "bot", "⊥"), ("Множества и логика", "therefore", "∴"),
        ("Множества и логика", "because", "∵"),

        ("Скобки и модули", "vert", "|"), ("Скобки и модули", "lvert", "|"), ("Скобки и модули", "rvert", "|"),
        ("Скобки и модули", "Vert", "‖"), ("Скобки и модули", "lVert", "‖"), ("Скобки и модули", "rVert", "‖"),
        ("Скобки и модули", "langle", "⟨"), ("Скобки и модули", "rangle", "⟩"),
        ("Скобки и модули", "lfloor", "⌊"), ("Скобки и модули", "rfloor", "⌋"),
        ("Скобки и модули", "lceil", "⌈"), ("Скобки и модули", "rceil", "⌉"),
        ("Скобки и модули", "lbrace", "{"), ("Скобки и модули", "rbrace", "}"),

        ("Разное", "infty", "∞"), ("Разное", "partial", "∂"), ("Разное", "nabla", "∇"),
        ("Разное", "deg", "°"), ("Разное", "degree", "°"), ("Разное", "angle", "∠"),
        ("Разное", "triangle", "△"), ("Разное", "Box", "□"), ("Разное", "ldots", "…"),
        ("Разное", "dots", "…"), ("Разное", "cdots", "⋯"), ("Разное", "vdots", "⋮"),
        ("Разное", "ddots", "⋱"), ("Разное", "prime", "′"), ("Разное", "aleph", "ℵ"),
        ("Разное", "hbar", "ℏ"), ("Разное", "ell", "ℓ"), ("Разное", "Re", "ℜ"), ("Разное", "Im", "ℑ"),
        ("Разное", "percent", "%"), ("Разное", "colon", ":"),
    ];

    private static readonly FrozenDictionary<string, string> Symbols = SymbolTable
        .ToDictionary(x => x.Name, x => x.Glyph, StringComparer.Ordinal)
        .ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    /// Слова, которые становятся символом и без обратной косой: только греческий алфавит и
    /// бесконечность. Остальные имена (<c>in</c>, <c>to</c>, <c>sum</c>) в обычном тексте формулы
    /// встречаются как слова, поэтому требуют <c>\</c>.
    /// </summary>
    private static readonly FrozenSet<string> BareWords = SymbolTable
        .Where(x => x.Group is GreekLower or GreekUpper)
        .Select(x => x.Name)
        .Append("infty")
        .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Имена функций: набираются прямым шрифтом, а не курсивом переменной.</summary>
    private static readonly FrozenSet<string> Functions = new[]
    {
        "sin", "cos", "tan", "tg", "cot", "ctg", "sec", "csc",
        "arcsin", "arccos", "arctan", "arctg", "arcctg", "sinh", "cosh", "tanh", "cth", "sh", "ch", "th",
        "ln", "log", "lg", "exp", "lim", "max", "min", "sup", "inf",
        "det", "dim", "ker", "gcd", "mod", "arg", "sgn", "rank", "tr",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Команды-модификаторы размера скобок: на смысл не влияют, просто отбрасываются.</summary>
    private static readonly FrozenSet<string> Ignored = new[]
    {
        "left", "right", "big", "Big", "bigg", "Bigg", "bigl", "bigr", "Bigl", "Bigr",
        "displaystyle", "textstyle", "scriptstyle", "limits", "nolimits", "end",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// Подстановки ASCII-записи внутри формулы: длинные образцы раньше коротких, чтобы
    /// <c>&lt;-&gt;</c> не распался на <c>&lt;-</c> и <c>&gt;</c>.
    /// </summary>
    private static readonly (string Ascii, string Glyph)[] AsciiReplacements =
    [
        ("<=>", "⇔"), ("<->", "↔"), ("...", "…"),
        ("->", "→"), ("=>", "⇒"), ("<-", "←"), ("<=", "≤"), (">=", "≥"), ("!=", "≠"), ("+-", "±"),
        ("*", "·"),
    ];

    /// <summary>Глубина вложенности, дальше которой разбор не идёт — защита от <c>{{{{{…</c>.</summary>
    private const int MaxDepth = 16;

    /// <summary>Справочник символов и функций для шпаргалки редактора — из тех же таблиц, что разбор.</summary>
    public static IReadOnlyList<MathCommandInfo> Catalog { get; } = SymbolTable
        .Select(x => new MathCommandInfo(x.Name, x.Glyph, x.Group))
        .Concat(Functions.Order(StringComparer.Ordinal).Select(x => new MathCommandInfo(x, x, "Функции")))
        .ToArray();

    /// <summary>Разобрать тело формулы (без обрамляющих <c>$</c>).</summary>
    public static MathNode Parse(string? tex)
    {
        var source = tex ?? string.Empty;
        var position = 0;
        return ParseSequence(source, ref position, depth: 0, stopAtBrace: false);
    }

    /// <summary>
    /// Читает узлы подряд, пока не кончится строка или (во вложенном разборе) не встретится <c>}</c>.
    /// </summary>
    private static MathNode ParseSequence(string source, ref int position, int depth, bool stopAtBrace)
    {
        var items = new List<MathNode>();
        var text = new StringBuilder();

        void FlushText()
        {
            if (text.Length > 0)
            {
                items.Add(new MathText(text.ToString()));
                text.Clear();
            }
        }

        while (position < source.Length)
        {
            var current = source[position];

            if (current == '}')
            {
                if (stopAtBrace)
                {
                    break;
                }

                // Лишняя закрывающая скобка — обычный символ, ронять разбор не за что.
                text.Append(current);
                position++;
                continue;
            }

            if (current is '^' or '_')
            {
                position++;
                FlushText();
                ApplyScript(items, source, ref position, depth, isSuperscript: current == '^');
                continue;
            }

            if (current == '{')
            {
                position++;
                FlushText();
                items.Add(ParseBraceGroup(source, ref position, depth));
                continue;
            }

            if (current == '\\')
            {
                var node = ParseCommand(source, ref position, depth);
                if (node is MathText plain)
                {
                    // Экранированный символ склеивается с соседним текстом — меньше узлов.
                    text.Append(plain.Text);
                    continue;
                }

                if (node is not null)
                {
                    FlushText();
                    items.Add(node);
                }

                continue;
            }

            if (current == '(' && TryParseParenGroup(source, ref position, depth, out var inner))
            {
                FlushText();
                items.Add(new MathGroup(inner));
                continue;
            }

            if (current == '/')
            {
                if (TryParseSlashFraction(items, text, source, ref position, depth))
                {
                    continue;
                }

                text.Append(current);
                position++;
                continue;
            }

            if (char.IsAsciiLetter(current))
            {
                var word = ReadWord(source, ref position);

                if (word == "sqrt" && TryParseBareSqrt(source, ref position, depth, out var sqrt))
                {
                    FlushText();
                    items.Add(sqrt);
                    continue;
                }

                if (BareWords.Contains(word))
                {
                    FlushText();
                    items.Add(new MathSymbol(Symbols[word]));
                    continue;
                }

                text.Append(word);
                continue;
            }

            if (TryReplaceAscii(source, ref position, out var glyph))
            {
                text.Append(glyph);
                continue;
            }

            text.Append(current);
            position++;
        }

        FlushText();

        return items.Count == 1 ? items[0] : new MathSequence(items);
    }

    /// <summary>Буквенное слово целиком: только латиница, кириллица идёт посимвольно как обычный текст.</summary>
    private static string ReadWord(string source, ref int position)
    {
        var start = position;
        while (position < source.Length && char.IsAsciiLetter(source[position]))
        {
            position++;
        }

        return source[start..position];
    }

    private static bool TryReplaceAscii(string source, ref int position, out string glyph)
    {
        foreach (var (ascii, replacement) in AsciiReplacements)
        {
            if (string.CompareOrdinal(source, position, ascii, 0, ascii.Length) == 0)
            {
                position += ascii.Length;
                glyph = replacement;
                return true;
            }
        }

        glyph = string.Empty;
        return false;
    }

    /// <summary>
    /// Сбалансированная группа <c>(…)</c> с текущей позиции: скобки съедаются, содержимое разбирается
    /// отдельно. Незакрытая скобка — не группа, вызывающий оставит её обычным символом.
    /// </summary>
    private static bool TryParseParenGroup(string source, ref int position, int depth, out MathNode inner)
    {
        inner = new MathText(string.Empty);
        if (position >= source.Length || source[position] != '(' || depth >= MaxDepth)
        {
            return false;
        }

        var end = FindBalancedParen(source, position);
        if (end < 0)
        {
            return false;
        }

        var body = source[(position + 1)..end];
        var innerPosition = 0;
        inner = ParseSequence(body, ref innerPosition, depth + 1, stopAtBrace: false);
        position = end + 1;
        return true;
    }

    /// <summary>Позиция парной <c>)</c> для <c>(</c> в <paramref name="open"/> либо −1.</summary>
    private static int FindBalancedParen(string source, int open)
    {
        var balance = 0;
        for (var i = open; i < source.Length; i++)
        {
            if (source[i] == '(')
            {
                balance++;
            }
            else if (source[i] == ')' && --balance == 0)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Дробь через <c>/</c>: срабатывает, только если числитель — уже собранная группа <c>(…)</c>
    /// либо знаменатель начинается с <c>(</c>. Иначе косая черта остаётся текстом
    /// (<c>1/2</c>, <c>км/ч</c>). Позиция стоит на самой <c>/</c>.
    /// </summary>
    private static bool TryParseSlashFraction(List<MathNode> items, StringBuilder text, string source, ref int position, int depth)
    {
        var lookahead = position + 1;
        SkipSpaces(source, ref lookahead);
        var denominatorIsGroup = lookahead < source.Length && source[lookahead] == '('
            && FindBalancedParen(source, lookahead) >= 0;
        var numeratorIsGroup = text.Length == 0 && items.Count > 0 && items[^1] is MathGroup;

        if (!numeratorIsGroup && !denominatorIsGroup)
        {
            return false;
        }

        if (!TryTakeNumerator(items, text, out var numerator))
        {
            return false;
        }

        position = lookahead;
        MathNode denominator;
        if (denominatorIsGroup)
        {
            TryParseParenGroup(source, ref position, depth, out denominator);
        }
        else
        {
            denominator = ParseAtom(source, ref position, depth);
        }

        items.Add(new MathFraction(numerator, denominator));
        return true;
    }

    /// <summary>
    /// Снимает числитель со стека: группу <c>(…)</c> вместе с именем функции перед ней
    /// (<c>sin(x)/(x)</c>), хвост текста из букв и цифр (<c>1/(2)</c>) либо последний нетекстовый
    /// узел (<c>x^2/(2)</c>).
    /// </summary>
    private static bool TryTakeNumerator(List<MathNode> items, StringBuilder text, out MathNode numerator)
    {
        if (text.Length > 0)
        {
            var atom = TakeTrailingAtom(text);
            if (atom.Length == 0)
            {
                numerator = new MathText(string.Empty);
                return false;
            }

            // Остаток буфера («y=» из «y=1/(2)») идёт в дерево ПЕРЕД дробью.
            if (text.Length > 0)
            {
                items.Add(new MathText(text.ToString()));
                text.Clear();
            }

            numerator = new MathText(atom);
            return true;
        }

        if (items.Count == 0 || items[^1] is MathText)
        {
            numerator = new MathText(string.Empty);
            return false;
        }

        var last = items[^1];
        items.RemoveAt(items.Count - 1);

        if (last is MathGroup group)
        {
            // Слово перед скобками — имя функции или множитель, оно принадлежит числителю.
            if (items.Count > 0 && items[^1] is MathText previous
                && previous.Text.Length > 0 && !char.IsWhiteSpace(previous.Text[^1]))
            {
                var buffer = new StringBuilder(previous.Text);
                var atom = TakeTrailingAtom(buffer);
                if (atom.Length > 0)
                {
                    items.RemoveAt(items.Count - 1);
                    if (buffer.Length > 0)
                    {
                        items.Add(new MathText(buffer.ToString()));
                    }

                    numerator = new MathSequence([new MathText(atom), group]);
                    return true;
                }
            }

            numerator = group.Inner;
            return true;
        }

        numerator = last;
        return true;
    }

    /// <summary>Отрезает от буфера хвост из букв, цифр и точки — один операнд без скобок.</summary>
    private static string TakeTrailingAtom(StringBuilder text)
    {
        var end = text.Length;
        var start = end;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '.'))
        {
            start--;
        }

        var atom = text.ToString(start, end - start);
        text.Length = start;
        return atom;
    }

    /// <summary>
    /// Один операнд без скобок для знаменателя: группа <c>{…}</c>, команда, слово с возможной группой
    /// <c>(…)</c> после него (<c>(a)/sin(x)</c>) либо число.
    /// </summary>
    private static MathNode ParseAtom(string source, ref int position, int depth)
    {
        SkipSpaces(source, ref position);
        if (position >= source.Length)
        {
            return new MathText(string.Empty);
        }

        if (source[position] is '{' or '\\')
        {
            return ParseArgument(source, ref position, depth);
        }

        if (char.IsAsciiLetter(source[position]))
        {
            var word = ReadWord(source, ref position);
            if (word == "sqrt" && TryParseBareSqrt(source, ref position, depth, out var sqrt))
            {
                return sqrt;
            }

            MathNode head = BareWords.Contains(word) ? new MathSymbol(Symbols[word]) : new MathText(word);
            return TryParseParenGroup(source, ref position, depth, out var inner)
                ? new MathSequence([head, new MathGroup(inner)])
                : head;
        }

        var start = position;
        while (position < source.Length && (char.IsDigit(source[position]) || source[position] == '.'))
        {
            position++;
        }

        if (position == start)
        {
            position++;
        }

        return new MathText(source[start..position]);
    }

    /// <summary>
    /// <c>sqrt(…)</c> и <c>sqrt[n](…)</c> без обратной косой. Слово уже прочитано; без скобок сразу
    /// за ним это обычное слово, и позиция не сдвигается.
    /// </summary>
    private static bool TryParseBareSqrt(string source, ref int position, int depth, out MathNode sqrt)
    {
        sqrt = new MathText(string.Empty);
        var cursor = position;
        SkipSpaces(source, ref cursor);

        MathNode? index = null;
        if (cursor < source.Length && source[cursor] == '[')
        {
            var close = source.IndexOf(']', cursor);
            if (close < 0)
            {
                return false;
            }

            index = ParseSubstring(source[(cursor + 1)..close], depth);
            cursor = close + 1;
            SkipSpaces(source, ref cursor);
        }

        if (!TryParseParenGroup(source, ref cursor, depth, out var radicand))
        {
            return false;
        }

        position = cursor;
        sqrt = new MathSqrt(radicand, index);
        return true;
    }

    private static MathNode ParseSubstring(string body, int depth)
    {
        var innerPosition = 0;
        return depth >= MaxDepth
            ? new MathUnknown(body)
            : ParseSequence(body, ref innerPosition, depth + 1, stopAtBrace: false);
    }

    /// <summary>Тело <c>{…}</c>; открывающая скобка уже съедена.</summary>
    private static MathNode ParseBraceGroup(string source, ref int position, int depth)
    {
        if (depth >= MaxDepth)
        {
            // Слишком глубоко — остаток отдаём как есть, дальше не разбираем.
            var rest = source[position..];
            position = source.Length;
            return new MathUnknown(rest);
        }

        var body = ParseSequence(source, ref position, depth + 1, stopAtBrace: true);

        if (position < source.Length && source[position] == '}')
        {
            position++;
        }

        return body;
    }

    /// <summary>Сырой текст группы <c>{…}</c> без разбора — для <c>\text</c> и имени окружения.</summary>
    private static string ReadRawBraceGroup(string source, ref int position)
    {
        SkipSpaces(source, ref position);
        if (position >= source.Length || source[position] != '{')
        {
            return string.Empty;
        }

        var balance = 0;
        var start = position + 1;
        for (var i = position; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                balance++;
            }
            else if (source[i] == '}' && --balance == 0)
            {
                position = i + 1;
                return source[start..i];
            }
        }

        // Незакрытая скобка: берём всё до конца, это честнее пустоты.
        var rest = source[start..];
        position = source.Length;
        return rest;
    }

    /// <summary>Один аргумент: группа <c>{…}</c>, группа <c>(…)</c>, команда или один символ.</summary>
    private static MathNode ParseArgument(string source, ref int position, int depth)
    {
        SkipSpaces(source, ref position);

        if (position >= source.Length)
        {
            return new MathText(string.Empty);
        }

        if (source[position] == '{')
        {
            position++;
            return ParseBraceGroup(source, ref position, depth);
        }

        if (source[position] == '\\')
        {
            return ParseCommand(source, ref position, depth) ?? new MathText(string.Empty);
        }

        if (TryParseParenGroup(source, ref position, depth, out var inner))
        {
            return inner;
        }

        var single = source[position];
        position++;
        return new MathText(single.ToString());
    }

    /// <summary>
    /// Навешивает индекс на последний узел. Второй индекс к тому же основанию не создаёт новый
    /// узел, а дополняет уже существующий — так <c>x_i^2</c> остаётся одним основанием с двумя индексами.
    /// Скобки после знака индекса берутся целиком: <c>e^(31·33)</c> — весь показатель.
    /// </summary>
    private static void ApplyScript(List<MathNode> items, string source, ref int position, int depth, bool isSuperscript)
    {
        var script = ParseArgument(source, ref position, depth);

        // Индекс без основания (строка начинается с ^) — пустое основание, но конструкция сохраняется.
        var previous = items.Count > 0 ? items[^1] : new MathText(string.Empty);
        if (items.Count > 0)
        {
            items.RemoveAt(items.Count - 1);
        }

        if (previous is MathScript existing
            && (isSuperscript ? existing.Sup : existing.Sub) is null)
        {
            items.Add(isSuperscript
                ? existing with { Sup = script }
                : existing with { Sub = script });
            return;
        }

        items.Add(isSuperscript
            ? new MathScript(previous, script, null)
            : new MathScript(previous, null, script));
    }

    /// <summary>
    /// Команда после <c>\</c>. Возвращает <see langword="null"/>, если команду следует просто
    /// пропустить (<c>\left</c> и родня).
    /// </summary>
    private static MathNode? ParseCommand(string source, ref int position, int depth)
    {
        if (depth >= MaxDepth)
        {
            var remainder = new MathUnknown(source[position..]);
            position = source.Length;
            return remainder;
        }

        depth++;
        var start = position;
        position++; // сама обратная косая

        if (position >= source.Length)
        {
            return new MathText("\\");
        }

        if (!char.IsAsciiLetter(source[position]))
        {
            // Экранированный символ: \{, \}, \$, \% и так далее; \\ — перенос строки.
            var escaped = source[position];
            position++;
            return escaped switch
            {
                '\\' => new MathBreak(),
                '|' => new MathText("‖"),
                ',' or ';' or ':' or ' ' => new MathText(" "),
                '!' => new MathText(string.Empty),
                _ => new MathText(escaped.ToString()),
            };
        }

        var nameStart = position;
        while (position < source.Length && char.IsAsciiLetter(source[position]))
        {
            position++;
        }

        var name = source[nameStart..position];

        if (Ignored.Contains(name))
        {
            // \left. и \right. — невидимый ограничитель, точку показывать незачем.
            if (name is "left" or "right" && position < source.Length && source[position] == '.')
            {
                position++;
            }

            return null;
        }

        if (Symbols.TryGetValue(name, out var glyph))
        {
            return new MathSymbol(glyph);
        }

        if (Functions.Contains(name))
        {
            return new MathText(name);
        }

        return name switch
        {
            "frac" or "dfrac" or "tfrac" => new MathFraction(
                ParseArgument(source, ref position, depth),
                ParseArgument(source, ref position, depth)),
            "binom" => new MathBinomial(
                ParseArgument(source, ref position, depth),
                ParseArgument(source, ref position, depth)),
            "sqrt" => ParseSqrt(source, ref position, depth),
            "boxed" => new MathBoxed(ParseArgument(source, ref position, depth)),
            "overline" => new MathAccent(ParseArgument(source, ref position, depth), MathAccentKind.Overline),
            "underline" => new MathAccent(ParseArgument(source, ref position, depth), MathAccentKind.Underline),
            "vec" => new MathAccent(ParseArgument(source, ref position, depth), MathAccentKind.Vec),
            "hat" or "widehat" => new MathAccent(ParseArgument(source, ref position, depth), MathAccentKind.Hat),
            "bar" => new MathAccent(ParseArgument(source, ref position, depth), MathAccentKind.Bar),
            "dot" => new MathAccent(ParseArgument(source, ref position, depth), MathAccentKind.Dot),
            "ddot" => new MathAccent(ParseArgument(source, ref position, depth), MathAccentKind.Ddot),
            "tilde" or "widetilde" => new MathAccent(ParseArgument(source, ref position, depth), MathAccentKind.Tilde),
            "text" or "textrm" or "mbox" => new MathStyled(new MathText(ReadRawBraceGroup(source, ref position)), MathTextStyle.Text),
            "mathrm" or "operatorname" => new MathStyled(ParseArgument(source, ref position, depth), MathTextStyle.Upright),
            "mathbf" or "boldsymbol" or "textbf" => new MathStyled(ParseArgument(source, ref position, depth), MathTextStyle.Bold),
            "mathit" => ParseArgument(source, ref position, depth),
            "mathbb" => new MathText(ToDoubleStruck(ReadRawBraceGroup(source, ref position))),
            "quad" => new MathText("  "),
            "qquad" => new MathText("    "),
            "begin" => ParseEnvironment(source, ref position, depth),
            _ => UnknownCommand(source, start, ref position, depth),
        };
    }

    /// <summary>Корень с необязательной степенью в квадратных скобках.</summary>
    private static MathNode ParseSqrt(string source, ref int position, int depth)
    {
        MathNode? index = null;

        SkipSpaces(source, ref position);
        if (position < source.Length && source[position] == '[')
        {
            var close = source.IndexOf(']', position);
            if (close > 0)
            {
                index = ParseSubstring(source[(position + 1)..close], depth);
                position = close + 1;
            }
        }

        return new MathSqrt(ParseArgument(source, ref position, depth), index);
    }

    /// <summary>
    /// <c>\begin{имя}…\end{имя}</c>: строки делятся по <c>\\</c>, ячейки — по <c>&amp;</c>, каждая
    /// ячейка разбирается отдельно. Без <c>\end</c> телом считается остаток строки.
    /// </summary>
    private static MathNode ParseEnvironment(string source, ref int position, int depth)
    {
        var name = ReadRawBraceGroup(source, ref position).Trim();
        var kind = name switch
        {
            "pmatrix" => MathMatrixKind.Paren,
            "bmatrix" or "Bmatrix" => MathMatrixKind.Bracket,
            "cases" or "dcases" => MathMatrixKind.Cases,
            _ => MathMatrixKind.Plain,
        };

        var terminator = @"\end{" + name + "}";
        var end = source.IndexOf(terminator, position, StringComparison.Ordinal);
        string body;
        if (end < 0)
        {
            body = source[position..];
            position = source.Length;
        }
        else
        {
            body = source[position..end];
            position = end + terminator.Length;
        }

        var rows = new List<IReadOnlyList<MathNode>>();
        foreach (var row in SplitTopLevel(body, @"\\"))
        {
            if (row.Trim().Length == 0)
            {
                continue;
            }

            rows.Add(SplitTopLevel(row, "&").Select(cell => ParseSubstring(cell.Trim(), depth)).ToArray());
        }

        return new MathMatrix(rows, kind);
    }

    /// <summary>Делит строку по разделителю, не заглядывая внутрь фигурных скобок.</summary>
    private static List<string> SplitTopLevel(string body, string separator)
    {
        var parts = new List<string>();
        var balance = 0;
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c == '{')
            {
                balance++;
            }
            else if (c == '}')
            {
                balance = Math.Max(0, balance - 1);
            }
            else if (balance == 0 && string.CompareOrdinal(body, i, separator, 0, separator.Length) == 0)
            {
                parts.Add(body[start..i]);
                i += separator.Length - 1;
                start = i + 1;
            }
        }

        parts.Add(body[start..]);
        return parts;
    }

    /// <summary>Латинские буквы → ажурные (ℝ, ℕ, 𝔸…); прочие символы остаются как есть.</summary>
    private static string ToDoubleStruck(string text)
    {
        var builder = new StringBuilder(text.Length * 2);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                'C' => "ℂ",
                'H' => "ℍ",
                'N' => "ℕ",
                'P' => "ℙ",
                'Q' => "ℚ",
                'R' => "ℝ",
                'Z' => "ℤ",
                >= 'A' and <= 'Z' => char.ConvertFromUtf32(0x1D538 + (c - 'A')),
                >= 'a' and <= 'z' => char.ConvertFromUtf32(0x1D552 + (c - 'a')),
                _ => c.ToString(),
            });
        }

        return builder.ToString();
    }

    /// <summary>
    /// Незнакомая команда вместе с её фигурными аргументами — чтобы на экран уехал весь исходник
    /// целиком, а не команда отдельно от содержимого скобок.
    /// </summary>
    private static MathNode UnknownCommand(string source, int start, ref int position, int depth)
    {
        while (position < source.Length && source[position] == '{')
        {
            position++;
            ParseBraceGroup(source, ref position, depth);
        }

        return new MathUnknown(source[start..position]);
    }

    private static void SkipSpaces(string source, ref int position)
    {
        while (position < source.Length && source[position] == ' ')
        {
            position++;
        }
    }
}
