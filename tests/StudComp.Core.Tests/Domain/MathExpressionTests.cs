using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Разбор подмножества TeX (new_addons.md §11.1). Главный инвариант — никакой ввод не бросает:
/// формулы пишет человек прямо в заметке, и опечатка не имеет права ронять предпросмотр.
/// </summary>
public class MathExpressionTests
{
    [Theory]
    [InlineData("e^(31*33)", "31·33")]
    [InlineData("e^{31*33}", "31·33")]
    public void Whole_expression_is_used_as_the_exponent(string source, string expected)
    {
        var script = Assert.IsType<MathScript>(MathExpression.Parse(source));
        Assert.Equal(new MathText("e"), script.Base);
        Assert.Equal(new MathText(expected), script.Sup);
    }

    [Fact]
    public void Nested_parentheses_inside_the_exponent_stay_a_group()
    {
        var script = Assert.IsType<MathScript>(MathExpression.Parse("e^((31+2)*33)"));
        var sup = Assert.IsType<MathSequence>(script.Sup);
        Assert.Equal(new MathGroup(new MathText("31+2")), sup.Items[0]);
        Assert.Equal(new MathText("·33"), sup.Items[1]);
    }

    [Theory]
    [InlineData(@"\SUM", "∑")]
    [InlineData(@"\tau", "τ")]
    [InlineData(@"\vert", "|")]
    [InlineData(@"\lVert", "‖")]
    [InlineData(@"\iiint", "∭")]
    public void Additional_symbols_are_supported(string source, string glyph) =>
        Assert.Equal(new MathSymbol(glyph), MathExpression.Parse(source));

    [Fact]
    public void Integral_accepts_both_bounds()
    {
        var script = Assert.IsType<MathScript>(MathExpression.Parse(@"\int_o^\tau"));
        Assert.Equal(new MathSymbol("∫"), script.Base);
        Assert.Equal(new MathText("o"), script.Sub);
        Assert.Equal(new MathSymbol("τ"), script.Sup);
    }

    [Fact]
    public void Deep_command_nesting_has_a_limit() =>
        Assert.NotNull(MathExpression.Parse(string.Concat(Enumerable.Repeat(@"\sqrt", 10000)) + "x"));

    [Fact]
    public void Empty_input_gives_an_empty_sequence()
    {
        var node = MathExpression.Parse(string.Empty);

        var sequence = Assert.IsType<MathSequence>(node);
        Assert.Empty(sequence.Items);
    }

    [Fact]
    public void Null_input_does_not_throw()
    {
        var node = MathExpression.Parse(null);

        Assert.NotNull(node);
    }

    [Fact]
    public void Plain_text_stays_one_text_node()
    {
        var node = MathExpression.Parse("2 + 2 = 4");

        Assert.Equal(new MathText("2 + 2 = 4"), node);
    }

    [Fact]
    public void Superscript_takes_the_previous_node_as_its_base()
    {
        var node = MathExpression.Parse("x^2");

        var script = Assert.IsType<MathScript>(node);
        Assert.Equal(new MathText("x"), script.Base);
        Assert.Equal(new MathText("2"), script.Sup);
        Assert.Null(script.Sub);
    }

    [Fact]
    public void Subscript_is_read_the_same_way()
    {
        var node = MathExpression.Parse("a_i");

        var script = Assert.IsType<MathScript>(node);
        Assert.Equal(new MathText("i"), script.Sub);
        Assert.Null(script.Sup);
    }

    [Fact]
    public void Braced_script_keeps_the_whole_group()
    {
        var node = MathExpression.Parse("e^{-x}");

        var script = Assert.IsType<MathScript>(node);
        Assert.Equal(new MathText("-x"), script.Sup);
    }

    [Fact]
    public void Both_scripts_land_on_one_base()
    {
        var node = MathExpression.Parse("x_i^2");

        var script = Assert.IsType<MathScript>(node);
        Assert.Equal(new MathText("x"), script.Base);
        Assert.Equal(new MathText("i"), script.Sub);
        Assert.Equal(new MathText("2"), script.Sup);
    }

    [Fact]
    public void Script_without_a_base_does_not_throw_and_keeps_an_empty_base()
    {
        var node = MathExpression.Parse("^2");

        var script = Assert.IsType<MathScript>(node);
        Assert.Equal(new MathText(string.Empty), script.Base);
        Assert.Equal(new MathText("2"), script.Sup);
    }

    [Fact]
    public void Dangling_caret_at_the_end_does_not_throw()
    {
        var node = MathExpression.Parse("x^");

        var script = Assert.IsType<MathScript>(node);
        Assert.Equal(new MathText(string.Empty), script.Sup);
    }

    [Fact]
    public void Fraction_reads_both_arguments()
    {
        var node = MathExpression.Parse(@"\frac{a+b}{2}");

        var fraction = Assert.IsType<MathFraction>(node);
        Assert.Equal(new MathText("a+b"), fraction.Numerator);
        Assert.Equal(new MathText("2"), fraction.Denominator);
    }

    [Fact]
    public void Nested_fraction_stays_a_tree()
    {
        var node = MathExpression.Parse(@"\frac{\frac{1}{2}}{3}");

        var outer = Assert.IsType<MathFraction>(node);
        var inner = Assert.IsType<MathFraction>(outer.Numerator);
        Assert.Equal(new MathText("1"), inner.Numerator);
        Assert.Equal(new MathText("2"), inner.Denominator);
        Assert.Equal(new MathText("3"), outer.Denominator);
    }

    [Theory]
    [InlineData(@"\dfrac{1}{2}")]
    [InlineData(@"\tfrac{1}{2}")]
    public void Fraction_aliases_are_read_as_fractions(string tex)
    {
        Assert.IsType<MathFraction>(MathExpression.Parse(tex));
    }

    [Fact]
    public void Fraction_with_a_missing_argument_does_not_throw()
    {
        var node = MathExpression.Parse(@"\frac{a}");

        var fraction = Assert.IsType<MathFraction>(node);
        Assert.Equal(new MathText("a"), fraction.Numerator);
        Assert.Equal(new MathText(string.Empty), fraction.Denominator);
    }

    [Fact]
    public void Square_root_reads_its_radicand()
    {
        var node = MathExpression.Parse(@"\sqrt{x+1}");

        var sqrt = Assert.IsType<MathSqrt>(node);
        Assert.Equal(new MathText("x+1"), sqrt.Radicand);
        Assert.Null(sqrt.Index);
    }

    [Fact]
    public void Nth_root_reads_the_degree_in_brackets()
    {
        var node = MathExpression.Parse(@"\sqrt[3]{8}");

        var sqrt = Assert.IsType<MathSqrt>(node);
        Assert.Equal(new MathText("3"), sqrt.Index);
        Assert.Equal(new MathText("8"), sqrt.Radicand);
    }

    [Theory]
    [InlineData(@"\alpha", "α")]
    [InlineData(@"\beta", "β")]
    [InlineData(@"\Omega", "Ω")]
    [InlineData(@"\pi", "π")]
    [InlineData(@"\times", "×")]
    [InlineData(@"\cdot", "·")]
    [InlineData(@"\le", "≤")]
    [InlineData(@"\geq", "≥")]
    [InlineData(@"\neq", "≠")]
    [InlineData(@"\approx", "≈")]
    [InlineData(@"\pm", "±")]
    [InlineData(@"\infty", "∞")]
    [InlineData(@"\sum", "∑")]
    [InlineData(@"\int", "∫")]
    [InlineData(@"\partial", "∂")]
    [InlineData(@"\to", "→")]
    [InlineData(@"\in", "∈")]
    [InlineData(@"\cup", "∪")]
    [InlineData(@"\forall", "∀")]
    [InlineData(@"\ldots", "…")]
    public void Known_commands_become_symbols(string tex, string glyph)
    {
        Assert.Equal(new MathSymbol(glyph), MathExpression.Parse(tex));
    }

    [Theory]
    [InlineData(@"\sin", "sin")]
    [InlineData(@"\ln", "ln")]
    [InlineData(@"\lim", "lim")]
    public void Function_names_stay_upright_text(string tex, string expected)
    {
        Assert.Equal(new MathText(expected), MathExpression.Parse(tex));
    }

    [Fact]
    public void Unknown_command_keeps_its_source_with_arguments()
    {
        var node = MathExpression.Parse(@"\foo{n}{k}");

        var unknown = Assert.IsType<MathUnknown>(node);
        Assert.Equal(@"\foo{n}{k}", unknown.Source);
    }

    [Fact]
    public void Size_modifiers_are_dropped_without_affecting_the_rest()
    {
        var node = MathExpression.Parse(@"\left(x\right)");

        Assert.Equal(new MathGroup(new MathText("x")), node);
    }

    [Fact]
    public void Invisible_delimiter_dot_is_dropped_too()
    {
        Assert.Equal(new MathText("x"), MathExpression.Parse(@"\left.x\right."));
    }

    [Fact]
    public void Escaped_character_becomes_plain_text()
    {
        var node = MathExpression.Parse(@"50\% \{a\}");

        Assert.Equal(new MathText("50% {a}"), node);
    }

    [Fact]
    public void Unclosed_brace_does_not_throw_and_keeps_the_content()
    {
        var node = MathExpression.Parse(@"\frac{a}{b");

        var fraction = Assert.IsType<MathFraction>(node);
        Assert.Equal(new MathText("b"), fraction.Denominator);
    }

    [Fact]
    public void Stray_closing_brace_is_just_a_character()
    {
        var node = MathExpression.Parse("a}b");

        Assert.Equal(new MathText("a}b"), node);
    }

    [Fact]
    public void Lone_backslash_at_the_end_does_not_throw()
    {
        Assert.Equal(new MathText(@"x\"), MathExpression.Parse(@"x\"));
    }

    [Fact]
    public void Deeply_nested_braces_do_not_throw()
    {
        var tex = string.Concat(Enumerable.Repeat("{", 60)) + "x" + string.Concat(Enumerable.Repeat("}", 60));

        Assert.NotNull(MathExpression.Parse(tex));
    }

    [Fact]
    public void A_real_formula_parses_into_the_expected_shape()
    {
        var node = MathExpression.Parse(@"\int_0^1 f(x)dx");

        var sequence = Assert.IsType<MathSequence>(node);
        var script = Assert.IsType<MathScript>(sequence.Items[0]);
        Assert.Equal(new MathSymbol("∫"), script.Base);
        Assert.Equal(new MathText("0"), script.Sub);
        Assert.Equal(new MathText("1"), script.Sup);
        Assert.Equal(new MathText(" f"), sequence.Items[1]);
        Assert.Equal(new MathGroup(new MathText("x")), sequence.Items[2]);
        Assert.Equal(new MathText("dx"), sequence.Items[3]);
    }

    [Theory]
    [InlineData("$$$$")]
    [InlineData("^^^")]
    [InlineData("___")]
    [InlineData(@"\\\\")]
    [InlineData("{}{}{}")]
    [InlineData(@"\frac")]
    [InlineData(@"\sqrt[")]
    [InlineData("a^{b^{c^{d")]
    [InlineData("не формула вовсе")]
    public void Garbage_never_throws(string tex)
    {
        Assert.NotNull(MathExpression.Parse(tex));
    }

    // ---- Естественная запись без обратной косой ----

    [Fact]
    public void Parenthesised_expression_becomes_a_group()
    {
        Assert.Equal(new MathGroup(new MathText("a+b")), MathExpression.Parse("(a+b)"));
    }

    [Fact]
    public void Unbalanced_parenthesis_stays_plain_text()
    {
        Assert.Equal(new MathText("(a+b"), MathExpression.Parse("(a+b"));
    }

    [Fact]
    public void Slash_between_two_groups_builds_a_fraction_without_the_parentheses()
    {
        var fraction = Assert.IsType<MathFraction>(MathExpression.Parse("(a+b)/(c+d)"));
        Assert.Equal(new MathText("a+b"), fraction.Numerator);
        Assert.Equal(new MathText("c+d"), fraction.Denominator);
    }

    [Fact]
    public void Slash_with_a_group_only_on_the_right_takes_the_trailing_operand()
    {
        var sequence = Assert.IsType<MathSequence>(MathExpression.Parse("y=1/(2x)"));
        Assert.Equal(new MathText("y="), sequence.Items[0]);
        var fraction = Assert.IsType<MathFraction>(sequence.Items[1]);
        Assert.Equal(new MathText("1"), fraction.Numerator);
        Assert.Equal(new MathText("2x"), fraction.Denominator);
    }

    [Fact]
    public void Slash_with_a_group_only_on_the_left_takes_one_atom_as_denominator()
    {
        var sequence = Assert.IsType<MathSequence>(MathExpression.Parse("(a)/b+1"));
        var fraction = Assert.IsType<MathFraction>(sequence.Items[0]);
        Assert.Equal(new MathText("a"), fraction.Numerator);
        Assert.Equal(new MathText("b"), fraction.Denominator);
        Assert.Equal(new MathText("+1"), sequence.Items[1]);
    }

    [Theory]
    [InlineData("1/2")]
    [InlineData("км/ч")]
    [InlineData("a/b/c")]
    public void Slash_without_parentheses_stays_a_slash(string source)
    {
        Assert.Equal(new MathText(source), MathExpression.Parse(source));
    }

    [Fact]
    public void Function_call_before_the_slash_belongs_to_the_numerator()
    {
        var fraction = Assert.IsType<MathFraction>(MathExpression.Parse("sin(x)/(x)"));
        var numerator = Assert.IsType<MathSequence>(fraction.Numerator);
        Assert.Equal(new MathText("sin"), numerator.Items[0]);
        Assert.Equal(new MathGroup(new MathText("x")), numerator.Items[1]);
        Assert.Equal(new MathText("x"), fraction.Denominator);
    }

    [Fact]
    public void Scripted_base_before_the_slash_is_the_numerator()
    {
        var fraction = Assert.IsType<MathFraction>(MathExpression.Parse("x^2/(2)"));
        Assert.IsType<MathScript>(fraction.Numerator);
        Assert.Equal(new MathText("2"), fraction.Denominator);
    }

    [Fact]
    public void Function_call_after_the_slash_is_the_whole_denominator()
    {
        var fraction = Assert.IsType<MathFraction>(MathExpression.Parse("(1)/sin(x)"));
        var denominator = Assert.IsType<MathSequence>(fraction.Denominator);
        Assert.Equal(new MathText("sin"), denominator.Items[0]);
        Assert.Equal(new MathGroup(new MathText("x")), denominator.Items[1]);
    }

    [Fact]
    public void Slash_right_after_an_operator_stays_text()
    {
        var sequence = Assert.IsType<MathSequence>(MathExpression.Parse("a+/(b)"));
        Assert.Equal(new MathText("a+/"), sequence.Items[0]);
        Assert.Equal(new MathGroup(new MathText("b")), sequence.Items[1]);
    }

    [Fact]
    public void Bare_sqrt_with_parentheses_is_a_root()
    {
        var sqrt = Assert.IsType<MathSqrt>(MathExpression.Parse("sqrt(x+1)"));
        Assert.Equal(new MathText("x+1"), sqrt.Radicand);
        Assert.Null(sqrt.Index);
    }

    [Fact]
    public void Bare_sqrt_accepts_a_degree_in_brackets()
    {
        var sqrt = Assert.IsType<MathSqrt>(MathExpression.Parse("sqrt[3](8)"));
        Assert.Equal(new MathText("3"), sqrt.Index);
        Assert.Equal(new MathText("8"), sqrt.Radicand);
    }

    [Fact]
    public void Bare_sqrt_without_parentheses_is_just_a_word()
    {
        Assert.Equal(new MathText("sqrt x"), MathExpression.Parse("sqrt x"));
    }

    [Fact]
    public void Backslash_sqrt_accepts_parentheses_as_its_argument()
    {
        var sqrt = Assert.IsType<MathSqrt>(MathExpression.Parse(@"\sqrt(x+1)"));
        Assert.Equal(new MathText("x+1"), sqrt.Radicand);
    }

    [Theory]
    [InlineData("omega", "ω")]
    [InlineData("alpha", "α")]
    [InlineData("pi", "π")]
    [InlineData("Omega", "Ω")]
    [InlineData("infty", "∞")]
    public void Greek_words_become_symbols_without_a_backslash(string word, string glyph)
    {
        Assert.Equal(new MathSymbol(glyph), MathExpression.Parse(word));
    }

    [Theory]
    [InlineData("in")]
    [InlineData("to")]
    [InlineData("sum")]
    [InlineData("spin")]
    [InlineData("Api")]
    public void Other_words_stay_text_without_a_backslash(string word)
    {
        Assert.Equal(new MathText(word), MathExpression.Parse(word));
    }

    [Fact]
    public void Greek_word_inside_a_group_is_recognised()
    {
        var sequence = Assert.IsType<MathSequence>(MathExpression.Parse("A(omega)"));
        Assert.Equal(new MathText("A"), sequence.Items[0]);
        Assert.Equal(new MathGroup(new MathSymbol("ω")), sequence.Items[1]);
    }

    [Theory]
    [InlineData("a*b", "a·b")]
    [InlineData("x->y", "x→y")]
    [InlineData("x<->y", "x↔y")]
    [InlineData("a<=b", "a≤b")]
    [InlineData("a>=b", "a≥b")]
    [InlineData("a!=b", "a≠b")]
    [InlineData("a+-b", "a±b")]
    [InlineData("p=>q", "p⇒q")]
    [InlineData("p<=>q", "p⇔q")]
    [InlineData("1, 2, ..., n", "1, 2, …, n")]
    public void Ascii_operators_are_replaced_by_glyphs(string source, string expected)
    {
        Assert.Equal(new MathText(expected), MathExpression.Parse(source));
    }

    [Fact]
    public void The_owner_formula_from_the_screenshot_parses_naturally()
    {
        var sequence = Assert.IsType<MathSequence>(MathExpression.Parse("A(omega)=sqrt(U^2(omega)+V^2(omega))"));
        Assert.Equal(new MathText("A"), sequence.Items[0]);
        Assert.Equal(new MathGroup(new MathSymbol("ω")), sequence.Items[1]);
        Assert.Equal(new MathText("="), sequence.Items[2]);
        var sqrt = Assert.IsType<MathSqrt>(sequence.Items[3]);
        var radicand = Assert.IsType<MathSequence>(sqrt.Radicand);
        Assert.IsType<MathScript>(radicand.Items[0]);
    }

    // ---- Новые команды ----

    [Fact]
    public void Boxed_wraps_its_argument()
    {
        Assert.Equal(new MathBoxed(new MathText("E=mc")), MathExpression.Parse(@"\boxed{E=mc}"));
    }

    [Theory]
    [InlineData(@"\overline{AB}", MathAccentKind.Overline)]
    [InlineData(@"\underline{x}", MathAccentKind.Underline)]
    [InlineData(@"\vec{v}", MathAccentKind.Vec)]
    [InlineData(@"\hat{x}", MathAccentKind.Hat)]
    [InlineData(@"\widehat{x}", MathAccentKind.Hat)]
    [InlineData(@"\bar{x}", MathAccentKind.Bar)]
    [InlineData(@"\dot{x}", MathAccentKind.Dot)]
    [InlineData(@"\ddot{x}", MathAccentKind.Ddot)]
    [InlineData(@"\tilde{x}", MathAccentKind.Tilde)]
    public void Accents_are_read_with_their_kind(string source, MathAccentKind kind)
    {
        var accent = Assert.IsType<MathAccent>(MathExpression.Parse(source));
        Assert.Equal(kind, accent.Kind);
    }

    [Fact]
    public void Binomial_reads_both_arguments()
    {
        var binomial = Assert.IsType<MathBinomial>(MathExpression.Parse(@"\binom{n}{k}"));
        Assert.Equal(new MathText("n"), binomial.Top);
        Assert.Equal(new MathText("k"), binomial.Bottom);
    }

    [Fact]
    public void Text_command_keeps_its_content_raw()
    {
        var styled = Assert.IsType<MathStyled>(MathExpression.Parse(@"\text{если x -> 0}"));
        Assert.Equal(MathTextStyle.Text, styled.Style);
        Assert.Equal(new MathText("если x -> 0"), styled.Inner);
    }

    [Theory]
    [InlineData(@"\mathrm{d}x", MathTextStyle.Upright)]
    [InlineData(@"\mathbf{v}", MathTextStyle.Bold)]
    [InlineData(@"\operatorname{rank}", MathTextStyle.Upright)]
    public void Styled_commands_carry_their_style(string source, MathTextStyle style)
    {
        var node = MathExpression.Parse(source);
        var styled = node is MathSequence sequence ? sequence.Items[0] : node;
        Assert.Equal(style, Assert.IsType<MathStyled>(styled).Style);
    }

    [Theory]
    [InlineData(@"\mathbb{R}", "ℝ")]
    [InlineData(@"\mathbb{N}", "ℕ")]
    [InlineData(@"\mathbb{A}", "𝔸")]
    public void Blackboard_letters_map_to_double_struck_glyphs(string source, string expected)
    {
        Assert.Equal(new MathText(expected), MathExpression.Parse(source));
    }

    [Fact]
    public void Double_backslash_is_a_line_break()
    {
        var sequence = Assert.IsType<MathSequence>(MathExpression.Parse(@"a\\b"));
        Assert.IsType<MathBreak>(sequence.Items[1]);
    }

    [Fact]
    public void Cases_environment_splits_rows_and_cells()
    {
        var matrix = Assert.IsType<MathMatrix>(MathExpression.Parse(@"\begin{cases} x=1 & a>0 \\ y=2 & a\le 0 \end{cases}"));
        Assert.Equal(MathMatrixKind.Cases, matrix.Kind);
        Assert.Equal(2, matrix.Rows.Count);
        Assert.Equal(2, matrix.Rows[0].Count);
        Assert.Equal(new MathText("x=1"), matrix.Rows[0][0]);
        Assert.Equal(new MathText("y=2"), matrix.Rows[1][0]);
    }

    [Theory]
    [InlineData("pmatrix", MathMatrixKind.Paren)]
    [InlineData("bmatrix", MathMatrixKind.Bracket)]
    [InlineData("matrix", MathMatrixKind.Plain)]
    public void Matrix_kind_follows_the_environment_name(string name, MathMatrixKind kind)
    {
        var matrix = Assert.IsType<MathMatrix>(MathExpression.Parse($@"\begin{{{name}}} 1 & 2 \\ 3 & 4 \end{{{name}}}"));
        Assert.Equal(kind, matrix.Kind);
        Assert.Equal(new MathText("4"), matrix.Rows[1][1]);
    }

    [Fact]
    public void Environment_without_end_takes_the_rest_and_does_not_throw()
    {
        var matrix = Assert.IsType<MathMatrix>(MathExpression.Parse(@"\begin{cases} x \\ y"));
        Assert.Equal(2, matrix.Rows.Count);
    }

    [Fact]
    public void Catalog_lists_every_symbol_with_its_group()
    {
        Assert.Contains(MathExpression.Catalog, x => x.Name == "omega" && x.Glyph == "ω" && x.Group == "Греческие строчные");
        Assert.Contains(MathExpression.Catalog, x => x.Name == "sin" && x.Group == "Функции");
        Assert.Contains(MathExpression.Catalog, x => x.Name == "rightarrow" && x.Glyph == "→");
    }

    [Theory]
    [InlineData("((((")]
    [InlineData("))))")]
    [InlineData("/(")]
    [InlineData("(a)/")]
    [InlineData("sqrt[")]
    [InlineData("sqrt[3")]
    [InlineData(@"\begin")]
    [InlineData(@"\begin{")]
    [InlineData(@"\text{")]
    [InlineData(@"\mathbb")]
    [InlineData("*/*->")]
    [InlineData("a^(b^(c^(d")]
    public void Natural_syntax_garbage_never_throws(string tex)
    {
        Assert.NotNull(MathExpression.Parse(tex));
    }
}
