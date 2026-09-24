using System.Text;

namespace StudComp.Core.Domain;

/// <summary>Кусок текста с пропусками: обычный текст или закрываемый фрагмент.</summary>
public enum ClozeSegmentKind
{
    Text = 0,
    Gap = 1,
}

/// <summary>Один кусок разобранного текста.</summary>
/// <param name="Kind">Обычный текст или пропуск.</param>
/// <param name="Text">Содержимое куска — уже без фигурных скобок.</param>
/// <param name="GapIndex">Порядковый номер пропуска, начиная с нуля; <c>-1</c> для обычного текста.</param>
public sealed record ClozeSegment(ClozeSegmentKind Kind, string Text, int GapIndex);

/// <summary>Разобранный текст карточки с пропусками.</summary>
public sealed record ClozeText(string Raw, IReadOnlyList<ClozeSegment> Segments)
{
    /// <summary>Сколько в тексте закрываемых фрагментов.</summary>
    public int GapCount { get; } = Segments.Count(s => s.Kind == ClozeSegmentKind.Gap);

    /// <summary>Есть ли что закрывать.</summary>
    public bool HasGaps => GapCount > 0;

    /// <summary>Текст целиком, без маркеров — то, что видит пользователь после раскрытия.</summary>
    public string ToPlainText()
    {
        var builder = new StringBuilder(Raw.Length);
        foreach (var segment in Segments)
        {
            builder.Append(segment.Text);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Текст, в котором первые <paramref name="revealedCount"/> пропусков раскрыты, а остальные
    /// заменены заглушкой: пропуски раскрываются по одному (new_addons.md §5.3).
    /// </summary>
    public string ToMaskedText(int revealedCount, string mask = "…")
    {
        var builder = new StringBuilder(Raw.Length);
        foreach (var segment in Segments)
        {
            var hidden = segment.Kind == ClozeSegmentKind.Gap && segment.GapIndex >= revealedCount;
            builder.Append(hidden ? mask : segment.Text);
        }

        return builder.ToString();
    }
}

/// <summary>
/// Разбор пропусков <c>{{текст}}</c> в обороте карточки (new_addons.md §5.3). Синтаксис намеренно
/// совместим по духу с Anki. Чистая функция на голом BCL, покрывается таблично.
/// </summary>
/// <remarks>
/// Вход — текст, написанный человеком, поэтому разбор <b>никогда</b> не бросает и не сообщает об
/// ошибке синтаксиса: вложенность запрещена (внутренние скобки остаются обычным текстом), незакрытая
/// скобка трактуется как обычный текст, пустой <c>{{}}</c> — тоже (прятать там нечего).
/// </remarks>
public static class ClozeParser
{
    /// <summary>Открывающий маркер пропуска.</summary>
    public const string GapOpen = "{{";

    /// <summary>Закрывающий маркер пропуска.</summary>
    public const string GapClose = "}}";

    /// <summary>Дешёвая проверка «есть ли смысл предлагать режим пропусков» — без сборки сегментов.</summary>
    public static bool ContainsGaps(string? text) => Parse(text).HasGaps;

    /// <summary>Разобрать текст на куски.</summary>
    public static ClozeText Parse(string? text)
    {
        var raw = text ?? string.Empty;
        if (raw.Length == 0)
        {
            return new ClozeText(raw, []);
        }

        var segments = new List<ClozeSegment>();
        var buffer = new StringBuilder();
        var gapIndex = 0;
        var position = 0;

        void FlushText()
        {
            if (buffer.Length > 0)
            {
                segments.Add(new ClozeSegment(ClozeSegmentKind.Text, buffer.ToString(), -1));
                buffer.Clear();
            }
        }

        while (position < raw.Length)
        {
            if (!StartsAt(raw, position, GapOpen))
            {
                buffer.Append(raw[position]);
                position++;
                continue;
            }

            var close = raw.IndexOf(GapClose, position + GapOpen.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                // Незакрытая скобка — обычный текст до самого конца.
                buffer.Append(raw, position, raw.Length - position);
                position = raw.Length;
                continue;
            }

            var inner = raw[(position + GapOpen.Length)..close];
            if (inner.Length == 0)
            {
                buffer.Append(GapOpen).Append(GapClose);
                position = close + GapClose.Length;
                continue;
            }

            FlushText();
            segments.Add(new ClozeSegment(ClozeSegmentKind.Gap, inner, gapIndex++));
            position = close + GapClose.Length;
        }

        FlushText();
        return new ClozeText(raw, segments);
    }

    private static bool StartsAt(string value, int index, string token) =>
        index + token.Length <= value.Length
        && string.CompareOrdinal(value, index, token, 0, token.Length) == 0;
}
