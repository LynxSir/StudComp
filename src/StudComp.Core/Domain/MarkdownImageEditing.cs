namespace StudComp.Core.Domain;

/// <summary>Что делать с токеном по <c>Backspace</c>/<c>Delete</c>.</summary>
public enum ImageEraseStep
{
    /// <summary>Первое нажатие: выделить картинку целиком и ничего не удалять — это подтверждение.</summary>
    Select,

    /// <summary>Второе нажатие: картинка уже выделена, удаляем её одним куском.</summary>
    Delete,
}

/// <summary>Шаг двухэтапного удаления картинки.</summary>
/// <param name="Step">Выделить или удалить.</param>
/// <param name="Token">Картинка, о которой речь.</param>
/// <param name="Edit">Правка; осмысленна только при <see cref="ImageEraseStep.Delete"/>.</param>
public readonly record struct ImageErase(ImageEraseStep Step, MarkdownImageToken Token, MarkdownEdit Edit);

/// <summary>
/// Картинка в заметке как цельный объект (new_addons.md §12): каретка не встаёт внутрь ссылки,
/// стрелки перескакивают её, <c>Backspace</c> удаляет всю картинку в два нажатия, а вставка,
/// размер, перемещение и удаление выражены обычными правками <see cref="MarkdownEdit"/>.
/// </summary>
/// <remarks>
/// Всё здесь — чистые функции над текстом, ровно ради тестируемости: проекта <c>App.Tests</c> в
/// решении нет, поэтому поведение над <c>TextBox</c> обязано оставаться тонкой обёрткой.
/// Ни один метод не бросает исключений на любом пользовательском вводе.
/// </remarks>
public static class MarkdownImageEditing
{
    /// <summary>Минимальная и максимальная ширина картинки в пикселях.</summary>
    private const int MinWidth = 32;
    private const int MaxWidth = 2400;

    /// <summary>Картинка, внутрь которой попала позиция. На границах — <see langword="null"/>.</summary>
    public static MarkdownImageToken? Inside(string? markdown, int position)
    {
        foreach (var token in MarkdownLocalImages.Tokens(markdown))
        {
            if (token.Contains(position))
            {
                return token;
            }
        }

        return null;
    }

    /// <summary>
    /// Стрелка влево/вправо у границы картинки: перескочить её целиком одним нажатием, а не
    /// пробираться по символам ссылки. <see langword="null"/> — клавиша работает штатно.
    /// </summary>
    public static int? StepOverToken(string? markdown, int caret, bool forward)
    {
        var source = markdown ?? string.Empty;
        caret = Math.Clamp(caret, 0, source.Length);

        foreach (var token in MarkdownLocalImages.Tokens(source))
        {
            if (forward && (token.Start == caret || token.Contains(caret)))
            {
                return token.End;
            }

            if (!forward && (token.End == caret || token.Contains(caret)))
            {
                return token.Start;
            }
        }

        return null;
    }

    /// <summary>
    /// Страховочная сетка для каретки и выделения: одна проверка закрывает клик, двойной клик,
    /// протяжку, <c>Ctrl+стрелка</c> и визуальные <c>Home</c>/<c>End</c> при переносе строк.
    /// Каретка внутри картинки уезжает на ближайшую границу по направлению движения, а выделение,
    /// накрывшее картинку наполовину, расширяется до целых картинок — половину ссылки нельзя ни
    /// скопировать, ни затереть набором.
    /// </summary>
    /// <param name="previousCaret">
    /// Где каретка была до события — им определяется направление движения. Значение вне текста
    /// означает «направление неизвестно», тогда берётся ближайшая граница.
    /// </param>
    /// <returns>Исправленные начало и длина выделения либо <see langword="null"/>, если всё в порядке.</returns>
    public static (int Start, int Length)? Snap(string? markdown, int selectionStart, int selectionLength, int previousCaret)
    {
        var source = markdown ?? string.Empty;
        selectionStart = Math.Clamp(selectionStart, 0, source.Length);
        selectionLength = Math.Clamp(selectionLength, 0, source.Length - selectionStart);

        var tokens = MarkdownLocalImages.Tokens(source);
        if (tokens.Count == 0)
        {
            return null;
        }

        if (selectionLength == 0)
        {
            foreach (var token in tokens)
            {
                if (!token.Contains(selectionStart))
                {
                    continue;
                }

                var target = previousCaret < 0 || previousCaret > source.Length
                    ? NearestBoundary(token, selectionStart)
                    : previousCaret > selectionStart ? token.Start : token.End;

                return (target, 0);
            }

            return null;
        }

        var start = selectionStart;
        var end = selectionStart + selectionLength;

        foreach (var token in tokens)
        {
            // Пересечение есть, но картинка накрыта не целиком — расширяем до её границ.
            if (token.Start < end && start < token.End)
            {
                start = Math.Min(start, token.Start);
                end = Math.Max(end, token.End);
            }
        }

        return start == selectionStart && end == selectionStart + selectionLength
            ? null
            : (start, end - start);
    }

    /// <summary>
    /// <c>Backspace</c> (<paramref name="forward"/> = <see langword="false"/>) или <c>Delete</c> у
    /// границы картинки. Первое нажатие выделяет её и правки не пишет — никакого фантомного шага
    /// отмены; второе, когда выделение уже равно картинке точно, удаляет её целиком.
    /// </summary>
    /// <remarks>
    /// Состояние «взведено» — это само выделение, отдельного флага нет и рассинхронизироваться нечему.
    /// </remarks>
    public static ImageErase? Eraser(string? markdown, int selectionStart, int selectionLength, bool forward)
    {
        var source = markdown ?? string.Empty;
        selectionStart = Math.Clamp(selectionStart, 0, source.Length);
        selectionLength = Math.Clamp(selectionLength, 0, source.Length - selectionStart);

        foreach (var token in MarkdownLocalImages.Tokens(source))
        {
            if (selectionLength > 0)
            {
                if (token.Start == selectionStart && token.Length == selectionLength)
                {
                    return new ImageErase(ImageEraseStep.Delete, token, Delete(source, token));
                }

                continue;
            }

            if (forward ? token.Start == selectionStart : token.End == selectionStart)
            {
                return new ImageErase(ImageEraseStep.Select, token, default);
            }
        }

        return null;
    }

    /// <summary>
    /// Удалить картинку целиком. Занимающая свою строку забирает и перевод строки, который добавила
    /// вставка, — иначе после каждого удаления оставалась бы пустая строка.
    /// </summary>
    public static MarkdownEdit Delete(string? markdown, MarkdownImageToken token)
    {
        var source = markdown ?? string.Empty;
        var start = Math.Clamp(token.Start, 0, source.Length);
        var end = Math.Clamp(token.End, start, source.Length);

        if (OwnsItsLine(source, start, end))
        {
            if (end < source.Length && source[end] == '\n')
            {
                end++;
            }
            else if (start > 0 && source[start - 1] == '\n')
            {
                start--;
            }
        }

        return new MarkdownEdit(start, end - start, string.Empty, 0);
    }

    /// <summary>
    /// Вставить картинку в позицию каретки. Путь всегда в угловых скобках: по CommonMark путь без
    /// них не может содержать пробел, а папка предмета почти всегда называется в несколько слов —
    /// без скобок Markdig не распознал бы вставку картинкой вовсе (Phase 13.7, найдено на запуске).
    /// Переводы строк вокруг — для читаемости исходника; картинка обрывает абзац независимо от них.
    /// </summary>
    public static MarkdownEdit Insert(string? markdown, int caret, string relativePath, string caption = "Рисунок")
    {
        var source = markdown ?? string.Empty;
        caret = Math.Clamp(caret, 0, source.Length);

        var snippet = $"![{SafeCaption(caption)}](<{MarkdownLocalImages.Encode(relativePath ?? string.Empty)}>)";
        var before = caret > 0 && source[caret - 1] != '\n' ? "\n" : string.Empty;
        var after = caret < source.Length && source[caret] != '\n' ? "\n" : string.Empty;

        return new MarkdownEdit(caret, 0, before + snippet + after, before.Length + snippet.Length);
    }

    /// <summary>
    /// Задать ширину картинки в пикселях либо снять её (<paramref name="width"/> = <see langword="null"/>
    /// — размер по месту). Значение зажимается в допустимые границы, поэтому
    /// <see cref="ArgumentOutOfRangeException"/> из <see cref="MarkdownImageSize"/> сюда не доходит
    /// и до пользователя тем более. <see langword="null"/> — менять нечего.
    /// </summary>
    public static MarkdownEdit? SetWidth(string? markdown, MarkdownImageToken token, int? width)
    {
        var source = markdown ?? string.Empty;
        if (token.Start < 0 || token.End > source.Length || token.LinkLength <= 0)
        {
            return null;
        }

        var clamped = width is { } pixels ? Math.Clamp(pixels, MinWidth, MaxWidth) : (int?)null;
        var updated = MarkdownImageSize.SetWidth(source, token.Start, token.LinkLength, clamped);
        if (string.Equals(updated, source, StringComparison.Ordinal))
        {
            return null;
        }

        // Меняется только хвост атрибутов; сама ссылка остаётся на месте.
        var oldTailLength = token.End - token.LinkEnd;
        var newTailLength = oldTailLength + (updated.Length - source.Length);
        if (newTailLength < 0 || token.LinkEnd + newTailLength > updated.Length)
        {
            return null;
        }

        var tail = updated.Substring(token.LinkEnd, newTailLength);
        return new MarkdownEdit(token.LinkEnd, oldTailLength, tail, tail.Length);
    }

    /// <summary>
    /// Перенести картинку в другое место текста. Возвращается <b>одна непрерывная</b> правка: перенос
    /// сам по себе — две несвязные склейки, но замена объемлющего куска им равносильна, ложится в
    /// один шаг отмены и снимает разбор перекрытий. Заметки небольшие, копия куска бесплатна.
    /// </summary>
    public static MarkdownEdit? MoveToken(string? markdown, MarkdownImageToken token, int dropCaret)
    {
        var source = markdown ?? string.Empty;
        if (token.Start < 0 || token.End > source.Length)
        {
            return null;
        }

        var drop = Math.Clamp(dropCaret, 0, source.Length);

        // Сброс внутрь другой картинки — к её ближайшей границе; внутрь себя — переносить нечего.
        if (Inside(source, drop) is { } host)
        {
            drop = host.Start == token.Start ? token.Start : NearestBoundary(host, drop);
        }

        if (drop >= token.Start && drop <= token.End)
        {
            return null;
        }

        var tokenText = source[token.Start..token.End];
        var removal = Delete(source, token);
        var withoutToken = MarkdownEditing.Apply(source, removal);

        var dropWithoutToken = drop >= removal.Start + removal.Length
            ? drop - removal.Length
            : drop > removal.Start ? removal.Start : drop;
        dropWithoutToken = Math.Clamp(dropWithoutToken, 0, withoutToken.Length);

        var before = dropWithoutToken > 0 && withoutToken[dropWithoutToken - 1] != '\n' ? "\n" : string.Empty;
        var after = dropWithoutToken < withoutToken.Length && withoutToken[dropWithoutToken] != '\n'
            ? "\n"
            : string.Empty;
        var insertion = before + tokenText + after;
        var moved = withoutToken.Insert(dropWithoutToken, insertion);
        var caretInMoved = dropWithoutToken + before.Length + tokenText.Length;

        return MinimalEdit(source, moved, caretInMoved);
    }

    /// <summary>
    /// Найти картинку, соответствующую блоку из разбора Markdown: предпросмотр отдаёт
    /// <c>ImageBlock.SourceStart</c>/<c>SourceLength</c>, но текст мог уйти вперёд, а нормализация
    /// формул вообще сдвигает смещения — поэтому совпадение подтверждается ожидаемым путём.
    /// <see langword="null"/> — картинка больше не там, правку применять нельзя.
    /// </summary>
    public static MarkdownImageToken? Resolve(string? markdown, int sourceStart, int sourceLength, string? expectedPath)
    {
        var tokens = MarkdownLocalImages.Tokens(markdown);

        foreach (var token in tokens)
        {
            if (token.Start == sourceStart && token.LinkLength == sourceLength && SamePath(token.Path, expectedPath))
            {
                return token;
            }
        }

        // Смещение не совпало — принимаем только однозначный случай: такой путь в тексте один.
        MarkdownImageToken? single = null;
        foreach (var token in tokens)
        {
            if (!SamePath(token.Path, expectedPath))
            {
                continue;
            }

            if (single is not null)
            {
                return null;
            }

            single = token;
        }

        return single;
    }

    /// <summary>
    /// Передвинуть картинку на строку выше или ниже — детерминированная замена перетаскиванию в
    /// предпросмотре, где позицию сброса взять неоткуда. <see langword="null"/> — двигать некуда.
    /// </summary>
    public static MarkdownEdit? MoveByLine(string? markdown, MarkdownImageToken token, bool up)
    {
        var source = markdown ?? string.Empty;
        if (token.Start < 0 || token.End > source.Length)
        {
            return null;
        }

        if (up)
        {
            var lineStart = MarkdownEditing.LineStart(source, token.Start);
            if (lineStart == 0)
            {
                return null;
            }

            return MoveToken(source, token, MarkdownEditing.LineStart(source, lineStart - 1));
        }

        var lineEnd = MarkdownEditing.LineEnd(source, token.End);
        if (lineEnd >= source.Length)
        {
            return null;
        }

        return MoveToken(source, token, MarkdownEditing.LineEnd(source, lineEnd + 1));
    }

    /// <summary>
    /// Подпись картинки идёт в квадратных скобках, поэтому сами скобки и переводы строк из неё
    /// убираются: имя файла вида «фото [1].png» иначе развалило бы ссылку, и Markdig перестал бы
    /// видеть в ней картинку вовсе.
    /// </summary>
    private static string SafeCaption(string? caption)
    {
        var text = (caption ?? string.Empty)
            .Replace("[", string.Empty, StringComparison.Ordinal)
            .Replace("]", string.Empty, StringComparison.Ordinal)
            // Коды 13 и 10 числом: литералы переводов строк тут только мешают.
            .Replace((char)13, ' ')
            .Replace((char)10, ' ')
            .Trim();

        if (text.Length == 0)
        {
            return "Рисунок";
        }

        return text.Length > 80 ? text[..80].TrimEnd() : text;
    }

    /// <summary>Пути картинок сравниваются без учёта регистра и вида разделителя.</summary>
    public static bool SamePath(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    /// <summary>Приведение пути к виду, в котором его сравнивают и считают ссылки.</summary>
    internal static string Normalize(string? path) =>
        (path ?? string.Empty).Replace('\\', '/').Trim();

    /// <summary>Картинка занимает строку целиком — вокруг неё на этой строке только пробелы.</summary>
    private static bool OwnsItsLine(string source, int start, int end)
    {
        for (var i = start - 1; i >= 0 && source[i] != '\n'; i--)
        {
            if (!char.IsWhiteSpace(source[i]))
            {
                return false;
            }
        }

        for (var i = end; i < source.Length && source[i] != '\n'; i++)
        {
            if (!char.IsWhiteSpace(source[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static int NearestBoundary(MarkdownImageToken token, int position) =>
        position - token.Start <= token.End - position ? token.Start : token.End;

    /// <summary>
    /// Свести два текста к одной непрерывной правке: общий префикс и общий хвост отбрасываются,
    /// меняется только середина. Так перенос картинки остаётся одним шагом отмены.
    /// </summary>
    private static MarkdownEdit MinimalEdit(string before, string after, int caretInAfter)
    {
        var prefix = 0;
        var limit = Math.Min(before.Length, after.Length);
        while (prefix < limit && before[prefix] == after[prefix])
        {
            prefix++;
        }

        var suffix = 0;
        while (suffix < limit - prefix
            && before[before.Length - 1 - suffix] == after[after.Length - 1 - suffix])
        {
            suffix++;
        }

        var replacement = after[prefix..(after.Length - suffix)];
        var caret = Math.Clamp(caretInAfter - prefix, 0, replacement.Length);

        return new MarkdownEdit(prefix, before.Length - prefix - suffix, replacement, caret);
    }
}
