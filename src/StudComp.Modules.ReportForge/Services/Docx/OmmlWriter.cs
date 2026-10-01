using System.Collections.Frozen;
using DocumentFormat.OpenXml;
using StudComp.Core.Domain;
using M = DocumentFormat.OpenXml.Math;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace StudComp.Modules.ReportForge.Services.Docx;

/// <summary>
/// Формула из <c>$…$</c> как настоящее уравнение Word (OMML): <c>m:oMath</c> внутри абзаца.
/// Разбор берётся у <see cref="MathExpression"/> – того же, что верстает предпросмотр в приложении,
/// поэтому документ и экран не могут разойтись по смыслу.
/// </summary>
/// <remarks>
/// До этого формула уезжала в <c>.docx</c> исходником моноширинным курсивом: её было видно, но
/// править как уравнение – нельзя. Обход дерева здесь структурно повторяет <c>MathInlineRenderer</c>
/// из App-слоя, только цель другая.
/// <para>
/// Типографику внутри уравнения считает сам Word: латинские буквы он ставит математическим курсивом,
/// цифры и знаки – прямо. Поэтому <c>m:nor</c> («обычный текст») выставляется только там, где
/// прямое начертание обязательно: имена функций, <c>\mathrm</c>, <c>\operatorname</c>, <c>\text</c>.
/// Из-за этого документ местами выглядит правильнее предпросмотра, у которого правило курсива
/// упрощённое, – расхождение осознанное и в пользу документа.
/// </para>
/// <para>
/// Незнакомую команду (<see cref="MathUnknown"/>) писатель оставляет моноширинным исходником прямо
/// внутри уравнения: пользователь видит, что он написал, а не пустоту.
/// </para>
/// </remarks>
internal static class OmmlWriter
{
    /// <summary>Математический шрифт Word: им набираются все уравнения.</summary>
    private const string MathFont = "Cambria Math";

    private const string MonospaceFont = "Consolas";

    /// <summary>Группа справочника <see cref="MathExpression.Catalog"/>, где лежат имена функций.</summary>
    private const string FunctionsGroup = "Функции";

    /// <summary>
    /// Имена функций берутся из каталога самого движка, а не дублируются здесь: иначе список тихо
    /// разошёлся бы с разбором, и <c>\sin</c> в документе оказался бы курсивом.
    /// </summary>
    private static readonly FrozenSet<string> FunctionNames = MathExpression.Catalog
        .Where(command => command.Group == FunctionsGroup)
        .Select(command => command.Name)
        .ToFrozenSet(StringComparer.Ordinal);

    /// <summary>Формула внутри строки текста. <paramref name="tex"/> – исходник без обрамляющих <c>$</c>.</summary>
    public static M.OfficeMath BuildInline(string? tex)
    {
        var math = new M.OfficeMath();

        // Именно BuildBody, а не Build: перенос «\» на верхнем уровне формулы тоже обязан стать
        // многострочным уравнением, а не пропасть.
        foreach (var element in BuildBody(MathExpression.Parse(tex)))
        {
            math.Append(element);
        }

        return math;
    }

    /// <summary>Выключная формула отдельным абзацем – по центру, как принято в ГОСТ-отчётах.</summary>
    public static M.Paragraph BuildDisplay(string? tex) => new(
        new M.ParagraphProperties(new M.Justification { Val = M.JustificationValues.Center }),
        BuildInline(tex));

    private static IEnumerable<OpenXmlElement> Build(MathNode node) => node switch
    {
        MathSequence sequence => sequence.Items.SelectMany(Build),
        MathText text => [TextRun(text.Text, upright: IsFunctionName(text.Text))],
        MathSymbol symbol => [TextRun(symbol.Glyph, upright: false)],
        MathScript script => [BuildScript(script)],
        MathFraction fraction => [BuildFraction(fraction.Numerator, fraction.Denominator, withBar: true)],
        MathBinomial binomial => [Delimited("(", ")", [BuildFraction(binomial.Top, binomial.Bottom, withBar: false)])],
        MathSqrt sqrt => [BuildSqrt(sqrt)],
        MathGroup group => [Delimited("(", ")", [.. Build(group.Inner)])],
        MathBoxed boxed => [new M.BorderBox(new M.BorderBoxProperties(), Argument<M.Base>(boxed.Inner))],
        MathAccent accent => [BuildAccent(accent)],
        MathStyled styled => BuildStyled(styled),
        MathMatrix matrix => [BuildMatrix(matrix)],
        MathBreak => [],
        MathUnknown unknown => [MonospaceRun(unknown.Source)],
        _ => [],
    };

    /// <summary>
    /// Тело уравнения с учётом переносов <c>\\</c>: если они есть, строки собираются в
    /// <c>m:eqArr</c> – штатный для OMML способ показать многострочное уравнение.
    /// </summary>
    private static IEnumerable<OpenXmlElement> BuildBody(MathNode node)
    {
        var lines = SplitLines(node);
        if (lines.Count <= 1)
        {
            return Build(node);
        }

        var array = new M.EquationArray(new M.EquationArrayProperties());
        foreach (var line in lines)
        {
            array.Append(Argument<M.Base>(line));
        }

        return [array];
    }

    /// <summary>Разбить последовательность по <see cref="MathBreak"/>. Один элемент – переносов нет.</summary>
    private static List<MathNode> SplitLines(MathNode node)
    {
        if (node is not MathSequence sequence || !sequence.Items.Any(item => item is MathBreak))
        {
            return [node];
        }

        var lines = new List<MathNode>();
        var current = new List<MathNode>();

        foreach (var item in sequence.Items)
        {
            if (item is MathBreak)
            {
                lines.Add(new MathSequence(current));
                current = [];
                continue;
            }

            current.Add(item);
        }

        lines.Add(new MathSequence(current));
        return lines;
    }

    private static OpenXmlElement BuildScript(MathScript script) => (script.Sub, script.Sup) switch
    {
        ({ } sub, { } sup) => new M.SubSuperscript(
            new M.SubSuperscriptProperties(),
            Argument<M.Base>(script.Base),
            Argument<M.SubArgument>(sub),
            Argument<M.SuperArgument>(sup)),

        ({ } sub, null) => new M.Subscript(
            new M.SubscriptProperties(),
            Argument<M.Base>(script.Base),
            Argument<M.SubArgument>(sub)),

        (null, { } sup) => new M.Superscript(
            new M.SuperscriptProperties(),
            Argument<M.Base>(script.Base),
            Argument<M.SuperArgument>(sup)),

        _ => Argument<M.Base>(script.Base),
    };

    private static OpenXmlElement BuildFraction(MathNode numerator, MathNode denominator, bool withBar)
    {
        var properties = new M.FractionProperties();
        if (!withBar)
        {
            // Биномиальный коэффициент – столбик без черты; скобки добавляет вызывающий.
            properties.Append(new M.FractionType { Val = M.FractionTypeValues.NoBar });
        }

        return new M.Fraction(
            properties,
            Argument<M.Numerator>(numerator),
            Argument<M.Denominator>(denominator));
    }

    private static OpenXmlElement BuildSqrt(MathSqrt sqrt)
    {
        var properties = new M.RadicalProperties();
        if (sqrt.Index is null)
        {
            // Квадратный корень: степень не показывается, но сам элемент по схеме обязателен.
            properties.Append(new M.HideDegree { Val = M.BooleanValues.One });
        }

        return new M.Radical(
            properties,
            sqrt.Index is { } index ? Argument<M.Degree>(index) : new M.Degree(),
            Argument<M.Base>(sqrt.Radicand));
    }

    private static OpenXmlElement BuildAccent(MathAccent accent)
    {
        // Черта над и под выражением – это m:bar, а не «символ сверху»: она тянется по всей ширине.
        if (accent.Kind is MathAccentKind.Overline or MathAccentKind.Underline)
        {
            var position = accent.Kind == MathAccentKind.Overline
                ? M.VerticalJustificationValues.Top
                : M.VerticalJustificationValues.Bottom;

            return new M.Bar(
                new M.BarProperties(new M.Position { Val = position }),
                Argument<M.Base>(accent.Inner));
        }

        var mark = accent.Kind switch
        {
            MathAccentKind.Vec => "⃗",
            MathAccentKind.Hat => "̂",
            MathAccentKind.Bar => "̄",
            MathAccentKind.Dot => "̇",
            MathAccentKind.Ddot => "̈",
            MathAccentKind.Tilde => "̃",
            _ => "̄",
        };

        return new M.Accent(
            new M.AccentProperties(new M.AccentChar { Val = mark }),
            Argument<M.Base>(accent.Inner));
    }

    private static IEnumerable<OpenXmlElement> BuildStyled(MathStyled styled) => styled.Style switch
    {
        // \text{…}: слова внутри формулы обычным прямым шрифтом, пробелы сохраняются.
        MathTextStyle.Text when styled.Inner is MathText text => [TextRun(text.Text, upright: true)],
        MathTextStyle.Bold => [.. Build(styled.Inner).Select(WithBold)],
        MathTextStyle.Upright => Upright(styled.Inner),
        _ => Build(styled.Inner),
    };

    /// <summary>Прямое начертание для всего поддерева: <c>\mathrm</c>, <c>\operatorname</c>.</summary>
    private static IEnumerable<OpenXmlElement> Upright(MathNode node) => node switch
    {
        MathText text => [TextRun(text.Text, upright: true)],
        MathSequence sequence => sequence.Items.SelectMany(Upright),
        _ => Build(node),
    };

    /// <summary>Дописать полужирность каждому текстовому куску уже собранного поддерева.</summary>
    private static OpenXmlElement WithBold(OpenXmlElement element)
    {
        if (element is M.Run run)
        {
            var properties = run.GetFirstChild<W.RunProperties>();
            if (properties is null)
            {
                properties = new W.RunProperties();
                run.InsertAt(properties, run.GetFirstChild<M.RunProperties>() is null ? 0 : 1);
            }

            properties.Append(new W.Bold());
            properties.Append(new W.BoldComplexScript());
            return element;
        }

        foreach (var nested in element.Descendants<M.Run>().ToList())
        {
            WithBold(nested);
        }

        return element;
    }

    private static OpenXmlElement BuildMatrix(MathMatrix matrix)
    {
        var columns = matrix.Rows.Count == 0 ? 0 : matrix.Rows.Max(row => row.Count);

        // Система уравнений выравнивается по левому краю, обычная матрица – по центру.
        var justification = matrix.Kind == MathMatrixKind.Cases
            ? M.HorizontalAlignmentValues.Left
            : M.HorizontalAlignmentValues.Center;

        var columnProperties = new M.MatrixColumnProperties(
            new M.MatrixColumnCount { Val = Math.Max(1, columns) },
            new M.MatrixColumnJustification { Val = justification });

        var element = new M.Matrix(
            new M.MatrixProperties(new M.MatrixColumns(new M.MatrixColumn(columnProperties))));

        foreach (var row in matrix.Rows)
        {
            var matrixRow = new M.MatrixRow();
            foreach (var cell in row)
            {
                matrixRow.Append(Argument<M.Base>(cell));
            }

            element.Append(matrixRow);
        }

        var (open, close) = matrix.Kind switch
        {
            MathMatrixKind.Paren => ("(", ")"),
            MathMatrixKind.Bracket => ("[", "]"),
            MathMatrixKind.Cases => ("{", string.Empty),
            _ => (string.Empty, string.Empty),
        };

        return open.Length == 0 && close.Length == 0
            ? element
            : Delimited(open, close, [element]);
    }

    /// <summary>Содержимое в скобках: <c>m:d</c> умеет и «скобку только слева» (система уравнений).</summary>
    /// <remarks>
    /// Тип параметра – именно список, а не <c>IEnumerable</c>: <c>OpenXmlElement</c> сам реализует
    /// <c>IEnumerable&lt;OpenXmlElement&gt;</c> и перечисляет собственных детей, поэтому одиночный
    /// готовый элемент молча разобрался бы на потомки – уже принадлежащие чужому дереву.
    /// </remarks>
    private static OpenXmlElement Delimited(string open, string close, IReadOnlyList<OpenXmlElement> content)
    {
        var argument = new M.Base();
        foreach (var element in content)
        {
            argument.Append(element);
        }

        return new M.Delimiter(
            new M.DelimiterProperties(
                new M.BeginChar { Val = open },
                new M.EndChar { Val = close }),
            argument);
    }

    /// <summary>Аргумент уравнения (<c>m:e</c>, <c>m:num</c>, <c>m:sup</c>…) из поддерева.</summary>
    private static T Argument<T>(MathNode node)
        where T : M.OfficeMathArgumentType, new()
    {
        var argument = new T();
        foreach (var element in BuildBody(node))
        {
            argument.Append(element);
        }

        return argument;
    }

    private static M.Run TextRun(string text, bool upright)
    {
        var run = new M.Run();

        if (upright)
        {
            run.Append(new M.RunProperties(new M.NormalText()));
        }

        run.Append(new W.RunProperties(new W.RunFonts
        {
            Ascii = MathFont,
            HighAnsi = MathFont,
        }));

        run.Append(new M.Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    /// <summary>Незнакомая конструкция: исходник моноширинным прямо внутри уравнения.</summary>
    private static M.Run MonospaceRun(string source)
    {
        var run = new M.Run(new M.RunProperties(new M.NormalText()));

        run.Append(new W.RunProperties(new W.RunFonts
        {
            Ascii = MonospaceFont,
            HighAnsi = MonospaceFont,
            ComplexScript = MonospaceFont,
        }));

        run.Append(new M.Text(source) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static bool IsFunctionName(string text) => FunctionNames.Contains(text);
}
