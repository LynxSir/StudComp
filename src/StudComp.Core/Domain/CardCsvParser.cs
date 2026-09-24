using System.Text;

namespace StudComp.Core.Domain;

/// <summary>
/// Одна строка разобранного CSV/TSV. <see cref="LineNumber"/> — физическая строка исходного текста, с
/// которой началась запись (единица, для сообщений пользователю). <see cref="HasUnterminatedQuote"/>
/// проставляется только последней строке результата — это единственная ошибка, которую в силах
/// обнаружить построчный разбор: незакрытая кавычка проглатывает весь остаток файла в одно поле.
/// </summary>
public readonly record struct CardCsvRow(int LineNumber, IReadOnlyList<string> Fields, bool HasUnterminatedQuote);

/// <summary>
/// Результат разбора. <see cref="Header"/> заполнен только когда вызывающий код попросил трактовать
/// первую строку как заголовок (<c>hasHeader: true</c>) — сам парсер о смысле колонок ничего не знает.
/// </summary>
public sealed record CardCsvParseResult(IReadOnlyList<string>? Header, IReadOnlyList<CardCsvRow> Rows);

/// <summary>
/// Разбор CSV/TSV (new_addons.md §7.6) — чистая функция на голом BCL, покрывается таблично. Работает с
/// уже декодированной строкой: кодировка (BOM/UTF-8/Windows-1251) — забота вызывающего кода в
/// <c>StudComp.Modules.Cards</c>, здесь только поле-в-поле разбор.
/// </summary>
/// <remarks>
/// RFC4180-подобный: кавычки распознаются только в начале поля (иначе символ кавычки внутри
/// незакавыченного поля — просто литеральный символ, не ошибка), удвоенная кавычка внутри
/// закавыченного поля — экранированная кавычка, переводы строк и разделитель внутри кавычек не рвут
/// запись. Разбор <b>никогда не бросает</b>: единственная обнаружимая проблема — незакрытая кавычка —
/// проглатывает остаток файла в одну строку и помечается <see cref="CardCsvRow.HasUnterminatedQuote"/>,
/// а не роняет весь импорт.
/// </remarks>
public static class CardCsvParser
{
    public static CardCsvParseResult Parse(string text, char delimiter, bool hasHeader)
    {
        var rows = ParseRows(text ?? string.Empty, delimiter);

        if (!hasHeader || rows.Count == 0)
        {
            return new CardCsvParseResult(null, rows);
        }

        return new CardCsvParseResult(rows[0].Fields, rows.Skip(1).ToArray());
    }

    private static List<CardCsvRow> ParseRows(string text, char delimiter)
    {
        var rows = new List<CardCsvRow>();
        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var lineNumber = 1;
        var rowStartLine = 1;
        var i = 0;

        void FlushField()
        {
            fields.Add(field.ToString());
            field.Clear();
        }

        void FlushRow(bool unterminated)
        {
            FlushField();
            rows.Add(new CardCsvRow(rowStartLine, fields.ToArray(), unterminated));
            fields.Clear();
            rowStartLine = lineNumber;
        }

        while (i < text.Length)
        {
            var ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i += 2;
                        continue;
                    }

                    inQuotes = false;
                    i++;
                    continue;
                }

                if (ch == '\n')
                {
                    lineNumber++;
                }

                field.Append(ch);
                i++;
                continue;
            }

            if (ch == '"' && field.Length == 0)
            {
                inQuotes = true;
                i++;
                continue;
            }

            if (ch == delimiter)
            {
                FlushField();
                i++;
                continue;
            }

            if (ch == '\r' || ch == '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                i++;
                lineNumber++;

                // Пустая строка (без единого символа и без разделителей) — не запись, а просто пропуск.
                if (fields.Count == 0 && field.Length == 0)
                {
                    rowStartLine = lineNumber;
                    continue;
                }

                FlushRow(unterminated: false);
                continue;
            }

            field.Append(ch);
            i++;
        }

        if (inQuotes)
        {
            // Незакрытая кавычка проглотила всё до конца файла — честно помечаем и на этом стоп.
            FlushRow(unterminated: true);
        }
        else if (fields.Count > 0 || field.Length > 0)
        {
            FlushRow(unterminated: false);
        }

        return rows;
    }
}
