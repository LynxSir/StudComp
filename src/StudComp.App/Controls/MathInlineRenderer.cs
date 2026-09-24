using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using StudComp.Core.Domain;

namespace StudComp.Controls;

/// <summary>
/// Формула из <c>$…$</c> как набор инлайнов <see cref="FlowDocument"/> (new_addons.md §11.1).
/// Разбор живёт в <see cref="MathExpression"/> и покрыт тестами, здесь — только вёрстка.
/// </summary>
/// <remarks>
/// Своя вёрстка вместо TeX-пакета — решение владельца (new_addons.md §17 вопрос 3) и конвенция
/// проекта: свои контролы уже несут <c>GradeTrendChart</c>, <c>ActivityHeatmap</c>,
/// <c>VirtualizingWrapPanel</c>. Осознанные границы: верхний и нижний индексы у одного основания
/// идут подряд, а не столбиком; дробь, корень, рамка и матрица — атомарные инлайны и по ширине не
/// переносятся; размер знака корня и скобок матрицы не растёт вместе с содержимым.
/// Черта корня, черта над выражением и рамка — настоящие <see cref="Border"/> вокруг блока текста,
/// а не <c>TextDecorations</c>: декорацию WPF рисует на каждом <c>Run</c> отдельно по его базовой
/// линии и кеглю, поэтому над индексом она рвалась и уезжала вверх.
/// Рендер обязан быть чистым: готовые <c>Inline</c>/<c>UIElement</c> между вызовами не кешируются —
/// у элемента ровно один логический родитель, и повторная вставка бросает исключение.
/// </remarks>
public static class MathInlineRenderer
{
    /// <summary>Кегль индекса относительно основания.</summary>
    private const double ScriptScale = 0.72;

    /// <summary>Кегль содержимого дроби: чуть мельче строки, но ещё читаемый.</summary>
    private const double FractionScale = 0.92;

    /// <summary>Ниже этого кегля индекс индекса уже не прочесть.</summary>
    private const double MinFontSize = 7.0;

    private const string LineBrushKey = "TextFillColorPrimaryBrush";

    private static readonly FontFamily Monospace = new("Consolas, Courier New, monospace");

    /// <summary>Собрать формулу. <paramref name="source"/> — исходник TeX без обрамляющих <c>$</c>.</summary>
    public static Inline Build(string? source, double baseFontSize) =>
        Build(MathExpression.Parse(source), baseFontSize, BaselineAlignment.Baseline);

    /// <remarks>
    /// Выравнивание протаскивается параметром до самых листьев: <c>BaselineAlignment</c> —
    /// не наследуемое свойство, и выставленное на <c>Span</c> оно до вложенных <c>Run</c> не дойдёт.
    /// </remarks>
    private static Inline Build(MathNode node, double size, BaselineAlignment align) => node switch
    {
        MathSequence sequence => BuildSequence(sequence, size, align),
        MathText text => Leaf(text.Text, size, align, italic: false),
        MathSymbol symbol => Leaf(symbol.Glyph, size, align, italic: false),
        MathScript script => BuildScript(script, size, align),
        MathFraction fraction => BuildFraction(fraction.Numerator, fraction.Denominator, size, align, withBar: true),
        MathBinomial binomial => BuildBinomial(binomial, size, align),
        MathSqrt sqrt => BuildSqrt(sqrt, size, align),
        MathGroup group => BuildGroup(group, size, align),
        MathBoxed boxed => Framed(boxed.Inner, size, align, new Thickness(1), new Thickness(4, 1, 4, 1), new CornerRadius(2)),
        MathAccent accent => BuildAccent(accent, size, align),
        MathStyled styled => BuildStyled(styled, size, align),
        MathMatrix matrix => BuildMatrix(matrix, size),
        MathBreak => new LineBreak(),
        MathUnknown unknown => BuildUnknown(unknown.Source, size, align),
        _ => Leaf(string.Empty, size, align, italic: false),
    };

    private static Inline BuildSequence(MathSequence sequence, double size, BaselineAlignment align)
    {
        var span = new Span { BaselineAlignment = align };
        foreach (var item in sequence.Items)
        {
            span.Inlines.Add(Build(item, size, align));
        }

        return span;
    }

    private static Inline BuildGroup(MathGroup group, double size, BaselineAlignment align)
    {
        var span = new Span { BaselineAlignment = align };
        span.Inlines.Add(Leaf("(", size, align, italic: false));
        span.Inlines.Add(Build(group.Inner, size, align));
        span.Inlines.Add(Leaf(")", size, align, italic: false));
        return span;
    }

    private static Inline BuildScript(MathScript script, double size, BaselineAlignment align)
    {
        var span = new Span { BaselineAlignment = align };
        span.Inlines.Add(Build(script.Base, size, align));

        var scriptSize = Math.Max(MinFontSize, size * ScriptScale);

        if (script.Sub is { } sub)
        {
            span.Inlines.Add(Build(sub, scriptSize, BaselineAlignment.Subscript));
        }

        if (script.Sup is { } sup)
        {
            span.Inlines.Add(Build(sup, scriptSize, BaselineAlignment.Superscript));
        }

        return span;
    }

    private static Inline BuildFraction(MathNode numerator, MathNode denominator, double size, BaselineAlignment align, bool withBar)
    {
        var inner = Math.Max(MinFontSize, size * FractionScale);

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(2, 0, 2, 0),
        };

        stack.Children.Add(Host(numerator, inner));

        if (withBar)
        {
            var bar = new Border { Height = 1, Margin = new Thickness(0, 1.5, 0, 1.5) };

            // Кисть из темы, а не константа: предпросмотр живёт и в тёмной теме.
            bar.SetResourceReference(Border.BackgroundProperty, LineBrushKey);

            // Черта растягивается на ширину панели, а панель широка настолько, насколько широк
            // самый широкий из числителя и знаменателя — ровно то, что нужно.
            stack.Children.Add(bar);
        }

        stack.Children.Add(Host(denominator, inner));

        // Именно Center: у StackPanel нет базовой линии, и при Baseline дробь провалилась бы
        // под строку целиком. В верхнем индексе — прижать к верху, чтобы не сесть на основание.
        return new InlineUIContainer(stack)
        {
            BaselineAlignment = align == BaselineAlignment.Superscript ? BaselineAlignment.Top : BaselineAlignment.Center,
        };
    }

    private static Inline BuildBinomial(MathBinomial binomial, double size, BaselineAlignment align)
    {
        var span = new Span { BaselineAlignment = align };
        span.Inlines.Add(Leaf("(", size, align, italic: false));
        span.Inlines.Add(BuildFraction(binomial.Top, binomial.Bottom, size, align, withBar: false));
        span.Inlines.Add(Leaf(")", size, align, italic: false));
        return span;
    }

    /// <summary>Часть дроби отдельным <c>TextBlock</c> — панель инлайнов не принимает.</summary>
    private static TextBlock Host(MathNode node, double size)
    {
        var block = new TextBlock
        {
            FontSize = size,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        block.Inlines.Add(Build(node, size, BaselineAlignment.Baseline));
        return block;
    }

    /// <summary>
    /// Блок текста в рамке как инлайн строки. Низ блока выравнивается по низу текста строки
    /// (<see cref="BaselineAlignment.TextBottom"/>): при одном шрифте и кегле базовые линии
    /// совпадают, а рамка честно обходит и индексы, и дробь внутри.
    /// </summary>
    private static Inline Framed(MathNode node, double size, BaselineAlignment align, Thickness borderThickness, Thickness padding, CornerRadius radius)
    {
        var border = new Border
        {
            BorderThickness = borderThickness,
            Padding = padding,
            CornerRadius = radius,
            Child = Host(node, size),
        };
        border.SetResourceReference(Border.BorderBrushProperty, LineBrushKey);

        return new InlineUIContainer(border)
        {
            BaselineAlignment = align switch
            {
                BaselineAlignment.Superscript => BaselineAlignment.Top,
                BaselineAlignment.Subscript => BaselineAlignment.Bottom,
                _ => BaselineAlignment.TextBottom,
            },
        };
    }

    private static Inline BuildSqrt(MathSqrt sqrt, double size, BaselineAlignment align)
    {
        var span = new Span { BaselineAlignment = align };

        if (sqrt.Index is { } index)
        {
            span.Inlines.Add(Build(index, Math.Max(MinFontSize, size * ScriptScale), BaselineAlignment.Superscript));
        }

        span.Inlines.Add(Leaf("√", size, align, italic: false));

        // Черта — верхняя граница рамки вокруг блока, одна на всю ширину подкоренного выражения.
        span.Inlines.Add(Framed(sqrt.Radicand, size, align, new Thickness(0, 1, 0, 0), new Thickness(1, 1, 1, 0), new CornerRadius(0)));

        return span;
    }

    private static Inline BuildAccent(MathAccent accent, double size, BaselineAlignment align)
    {
        // Одиночный символ — комбинируемый знак Unicode: он ляжет ровно над буквой любого кегля.
        if (accent.Inner is MathText { Text.Length: 1 } single && accent.Kind != MathAccentKind.Underline)
        {
            var mark = accent.Kind switch
            {
                MathAccentKind.Vec => "⃗",
                MathAccentKind.Hat => "̂",
                MathAccentKind.Bar => "̄",
                MathAccentKind.Dot => "̇",
                MathAccentKind.Ddot => "̈",
                MathAccentKind.Tilde => "̃",
                _ => "̅",
            };

            return Leaf(single.Text + mark, size, align, italic: char.IsAsciiLetter(single.Text[0]));
        }

        if (accent.Kind == MathAccentKind.Underline)
        {
            return Framed(accent.Inner, size, align, new Thickness(0, 0, 0, 1), new Thickness(1, 0, 1, 1), new CornerRadius(0));
        }

        // Многосимвольное выражение под чертой — та же рамка, что у корня; стрелка над словом
        // (\vec{AB}) рисуется той же чертой: отдельного растяжимого глифа у шрифта нет.
        return Framed(accent.Inner, size, align, new Thickness(0, 1, 0, 0), new Thickness(1, 1, 1, 0), new CornerRadius(0));
    }

    private static Inline BuildStyled(MathStyled styled, double size, BaselineAlignment align)
    {
        var span = new Span { BaselineAlignment = align };

        switch (styled.Style)
        {
            case MathTextStyle.Bold:
                span.FontWeight = FontWeights.Bold;
                span.Inlines.Add(Build(styled.Inner, size, align));
                break;

            case MathTextStyle.Text when styled.Inner is MathText text:
                span.Inlines.Add(new Run(text.Text) { FontSize = size, BaselineAlignment = align });
                break;

            default:
                // Прямой шрифт: листья собираются без курсива переменных.
                span.Inlines.Add(BuildUpright(styled.Inner, size, align));
                break;
        }

        return span;
    }

    /// <summary>Как <see cref="Build(MathNode, double, BaselineAlignment)"/>, но одиночные буквы не курсивятся.</summary>
    private static Inline BuildUpright(MathNode node, double size, BaselineAlignment align) => node switch
    {
        MathText text => new Run(text.Text) { FontSize = size, BaselineAlignment = align },
        MathSequence sequence => UprightSequence(sequence, size, align),
        _ => Build(node, size, align),
    };

    private static Inline UprightSequence(MathSequence sequence, double size, BaselineAlignment align)
    {
        var span = new Span { BaselineAlignment = align };
        foreach (var item in sequence.Items)
        {
            span.Inlines.Add(BuildUpright(item, size, align));
        }

        return span;
    }

    private static Inline BuildMatrix(MathMatrix matrix, double size)
    {
        var inner = Math.Max(MinFontSize, size * FractionScale);
        var grid = new Grid { Margin = new Thickness(2, 0, 2, 0) };

        var columns = matrix.Rows.Count == 0 ? 0 : matrix.Rows.Max(row => row.Count);
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        for (var r = 0; r < matrix.Rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var row = matrix.Rows[r];
            for (var c = 0; c < row.Count; c++)
            {
                var cell = Host(row[c], inner);
                cell.Margin = new Thickness(4, 1, 4, 1);
                cell.TextAlignment = matrix.Kind == MathMatrixKind.Cases ? TextAlignment.Left : TextAlignment.Center;
                cell.HorizontalAlignment = matrix.Kind == MathMatrixKind.Cases ? HorizontalAlignment.Left : HorizontalAlignment.Center;
                Grid.SetRow(cell, r);
                Grid.SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var (open, close) = matrix.Kind switch
        {
            MathMatrixKind.Paren => ("(", ")"),
            MathMatrixKind.Bracket => ("[", "]"),
            MathMatrixKind.Cases => ("{", string.Empty),
            _ => (string.Empty, string.Empty),
        };

        if (open.Length > 0)
        {
            panel.Children.Add(Bracket(open, size));
        }

        panel.Children.Add(grid);

        if (close.Length > 0)
        {
            panel.Children.Add(Bracket(close, size));
        }

        return new InlineUIContainer(panel) { BaselineAlignment = BaselineAlignment.Center };
    }

    /// <summary>Скобка на всю высоту матрицы: обычный глиф, растянутый по вертикали.</summary>
    private static UIElement Bracket(string glyph, double size)
    {
        var text = new TextBlock { Text = glyph, FontSize = size, FontWeight = FontWeights.Light };
        return new Viewbox
        {
            Stretch = Stretch.Fill,
            StretchDirection = StretchDirection.Both,
            VerticalAlignment = VerticalAlignment.Stretch,
            Width = size * 0.45,
            Child = text,
        };
    }

    /// <summary>Незнакомая конструкция — исходником, приглушённо и моноширинно, но не пустотой.</summary>
    private static Inline BuildUnknown(string source, double size, BaselineAlignment align)
    {
        var run = new Run(source)
        {
            FontFamily = Monospace,
            FontSize = Math.Max(MinFontSize, size * 0.95),
            BaselineAlignment = align,
        };

        run.SetResourceReference(TextElement.ForegroundProperty, "TextFillColorTertiaryBrush");
        return run;
    }

    /// <summary>
    /// Текст формулы. Одиночная латинская буква — переменная и набирается курсивом, как принято
    /// в математике; цифры, кириллица и имена функций остаются прямыми.
    /// </summary>
    private static Inline Leaf(string text, double size, BaselineAlignment align, bool italic)
    {
        if (text.Length == 1 && char.IsAsciiLetter(text[0]))
        {
            italic = true;
        }

        return new Run(text)
        {
            FontSize = size,
            BaselineAlignment = align,
            FontStyle = italic ? FontStyles.Italic : FontStyles.Normal,
        };
    }
}
