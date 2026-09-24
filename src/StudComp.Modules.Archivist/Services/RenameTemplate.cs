using System.Globalization;
using System.Text;
using StudComp.Core.Domain;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Подстановка токенов шаблона переименования (ARCHITECTURE §8.4 п.4): <c>{Subject}</c>, <c>{Type}</c>,
/// <c>{Date:формат}</c>, <c>{OriginalName}</c>, <c>{Ext}</c>, <c>{Counter}</c>.
/// </summary>
/// <remarks>
/// <c>{Counter}</c> здесь всегда раскрывается в пустую строку: суффикс при конфликте имён добавляет
/// исполнитель операции, потому что только он имеет право смотреть на диск (движок правил — не имеет).
/// <c>{Type}</c> берётся из <see cref="ArchivistRule.WorkType"/> (напр. «ЛР»); если поле не заполнено —
/// откат на ключевое слово правила (<c>Keyword</c>) либо расширение без точки (<c>Extension</c>) (ADR §16.28).
/// </remarks>
internal static class RenameTemplate
{
    /// <summary>
    /// Собирает имя файла по шаблону. Пустой шаблон означает «оставить исходное имя». Результат всегда
    /// заканчивается расширением исходного файла и не содержит символов, запрещённых в именах файлов.
    /// </summary>
    public static string Apply(string? template, WatchedFileInfoValues file, ArchivistRule rule, string subjectName, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            return Sanitize(file.FileName);
        }

        var originalName = Path.GetFileNameWithoutExtension(file.FileName);
        var extension = file.Extension;

        var result = new StringBuilder(template.Length + 32);
        var index = 0;

        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            if (open < 0)
            {
                result.Append(template, index, template.Length - index);
                break;
            }

            var close = template.IndexOf('}', open + 1);
            if (close < 0)
            {
                result.Append(template, index, template.Length - index);
                break;
            }

            result.Append(template, index, open - index);

            var token = template[(open + 1)..close];
            result.Append(Resolve(token, originalName, extension, rule, subjectName, now));

            index = close + 1;
        }

        var name = result.ToString().Trim();

        // Токены могли схлопнуться в пустоту (нет предмета, нет типа) — не даём получить файл без имени.
        var withoutExtension = extension.Length > 0
            ? name.Replace(extension, string.Empty, StringComparison.OrdinalIgnoreCase)
            : name;
        if (string.IsNullOrWhiteSpace(withoutExtension.Trim('_', '-', '.', ' ')))
        {
            return Sanitize(file.FileName);
        }

        if (!name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        {
            name += extension;
        }

        return Sanitize(name);
    }

    private static string Resolve(
        string token,
        string originalName,
        string extension,
        ArchivistRule rule,
        string subjectName,
        DateTimeOffset now)
    {
        if (token.StartsWith("Date:", StringComparison.OrdinalIgnoreCase))
        {
            var format = token["Date:".Length..];
            return now.ToString(format, CultureInfo.InvariantCulture);
        }

        return token.ToLowerInvariant() switch
        {
            "subject" => subjectName,
            "type" => TypeLabel(rule, extension),
            "date" => now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            "originalname" => originalName,
            "ext" => extension,
            // Суффикс конфликта дописывает исполнитель — здесь токен просто исчезает.
            "counter" => string.Empty,
            _ => string.Concat("{", token, "}"),
        };
    }

    private static string TypeLabel(ArchivistRule rule, string extension)
    {
        if (!string.IsNullOrWhiteSpace(rule.WorkType))
        {
            return rule.WorkType.Trim();
        }

        return rule.MatchType switch
        {
            RuleMatchType.Keyword => rule.Pattern.Trim(),
            RuleMatchType.Extension => extension.TrimStart('.'),
            _ => string.Empty,
        };
    }

    /// <summary>
    /// Жёсткий предел длины имени файла в NTFS/FAT — 255 символов. Оставляем запас под суффикс
    /// конфликта « (999)», который потом допишет исполнитель (ARCHITECTURE §8.4 п.5, §8.8).
    /// </summary>
    private const int MaxFileNameLength = 255;

    private const int ConflictSuffixReserve = 8;

    /// <summary>Убирает символы, недопустимые в имени файла, схлопывает повторы разделителей и
    /// подрезает слишком длинное имя, сохраняя расширение.</summary>
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);

        foreach (var ch in name)
        {
            builder.Append(Array.IndexOf(invalid, ch) >= 0 ? '_' : ch);
        }

        var cleaned = builder.ToString().Trim();
        while (cleaned.Contains("__", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        }

        return Clamp(cleaned.Length == 0 ? "file" : cleaned);
    }

    /// <summary>
    /// Подрезает stem так, чтобы вместе с расширением и запасом под суффикс конфликта имя укладывалось
    /// в <see cref="MaxFileNameLength"/>. Расширение не трогаем — по нему всё ищется и открывается.
    /// </summary>
    private static string Clamp(string name)
    {
        if (name.Length <= MaxFileNameLength - ConflictSuffixReserve)
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        var stem = name[..^extension.Length];
        var budget = MaxFileNameLength - ConflictSuffixReserve - extension.Length;
        if (budget < 1)
        {
            // Патологический случай — одно расширение длиннее бюджета. Отдаём заведомо валидное имя.
            return "file";
        }

        return stem[..budget].TrimEnd('_', '-', '.', ' ') + extension;
    }
}

/// <summary>
/// Минимум сведений о файле, нужный шаблону. Отдельный тип, чтобы шаблон можно было тестировать без
/// сборки полного <c>WatchedFileInfo</c>.
/// </summary>
internal readonly record struct WatchedFileInfoValues(string FileName, string Extension);
