using System.Text;

namespace StudComp.Core.Domain;

/// <summary>Кусок текста оборота карточки: обычный текст или ссылка на другую карточку.</summary>
public enum WikiLinkSegmentKind
{
    Text = 0,
    Link = 1,
}

/// <summary>
/// Один кусок разобранного текста. <paramref name="Target"/> заполнен только для <see cref="WikiLinkSegmentKind.Link"/>
/// — название карточки, на которую ссылаются, без квадратных скобок.
/// </summary>
public sealed record WikiLinkSegment(WikiLinkSegmentKind Kind, string Text, string? Target = null);

/// <summary>Разобранный текст с wiki-ссылками.</summary>
public sealed record WikiLinkText(string Raw, IReadOnlyList<WikiLinkSegment> Segments)
{
    /// <summary>Есть ли в тексте хоть одна ссылка — дешёвая проверка перед перестройкой <c>FlowDocument</c>.</summary>
    public bool HasLinks { get; } = Segments.Any(s => s.Kind == WikiLinkSegmentKind.Link);
}

/// <summary>
/// Разбор wiki-ссылок <c>[[Название карточки]]</c> в обороте карточки (new_addons.md §7, объём Phase 12.9).
/// Чистая функция на голом BCL, покрывается таблично — соседи по папке: <see cref="ClozeParser"/>,
/// <see cref="CardQuery"/>.
/// </summary>
/// <remarks>
/// Никакого нового поля в модели данных и никакой миграции: это синтаксис поверх уже существующего
/// <c>Card.Back</c>, разбирается на лету при каждом рендере — ровно как <see cref="ClozeParser"/> не хранит
/// разбор пропусков. Вход — текст, написанный человеком, поэтому разбор <b>никогда</b> не бросает:
/// незакрытая скобка и пустой <c>[[]]</c> остаются обычным текстом.
/// </remarks>
public static class WikiLinkParser
{
    /// <summary>Открывающий маркер ссылки.</summary>
    public const string Open = "[[";

    /// <summary>Закрывающий маркер ссылки.</summary>
    public const string Close = "]]";

    /// <summary>Дешёвая проверка «есть ли смысл перестраивать текст» — без сборки сегментов.</summary>
    public static bool ContainsLinks(string? text) => Parse(text).HasLinks;

    /// <summary>Разобрать текст на куски.</summary>
    public static WikiLinkText Parse(string? text)
    {
        var raw = text ?? string.Empty;
        if (raw.Length == 0)
        {
            return new WikiLinkText(raw, []);
        }

        var segments = new List<WikiLinkSegment>();
        var buffer = new StringBuilder();
        var position = 0;

        void FlushText()
        {
            if (buffer.Length > 0)
            {
                segments.Add(new WikiLinkSegment(WikiLinkSegmentKind.Text, buffer.ToString()));
                buffer.Clear();
            }
        }

        while (position < raw.Length)
        {
            if (!StartsAt(raw, position, Open))
            {
                buffer.Append(raw[position]);
                position++;
                continue;
            }

            var close = raw.IndexOf(Close, position + Open.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                // Незакрытая скобка — обычный текст до самого конца.
                buffer.Append(raw, position, raw.Length - position);
                position = raw.Length;
                continue;
            }

            var target = raw[(position + Open.Length)..close].Trim();
            if (target.Length == 0)
            {
                buffer.Append(Open).Append(Close);
                position = close + Close.Length;
                continue;
            }

            FlushText();
            segments.Add(new WikiLinkSegment(WikiLinkSegmentKind.Link, $"{Open}{target}{Close}", target));
            position = close + Close.Length;
        }

        FlushText();
        return new WikiLinkText(raw, segments);
    }

    private static bool StartsAt(string value, int index, string token) =>
        index + token.Length <= value.Length
        && string.CompareOrdinal(value, index, token, 0, token.Length) == 0;
}
