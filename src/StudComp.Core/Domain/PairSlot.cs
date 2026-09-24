using System.Text.Json;
using System.Text.Json.Serialization;

namespace StudComp.Core.Domain;

/// <summary>
/// Один слот сетки звонков: «пара №2, 09:50–11:20» (new_addons.md §5). Своя на каждый семестр,
/// хранится в <see cref="Semester.PairSlotsJson"/>.
/// </summary>
public readonly record struct PairSlot(int Order, TimeOnly Start, TimeOnly End);

/// <summary>
/// Сериализация сетки звонков в строку и обратно. Живёт в <c>Core</c>, потому что читают её и
/// Органайзер (предзаполнение поповера), и настройки; <c>System.Text.Json</c> — часть BCL, так что
/// <c>CoreDependenciesTests</c> остаётся зелёным.
/// </summary>
public static class PairSlots
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// Разбирает JSON сетки звонков. Пустая строка, <see langword="null"/> или испорченный JSON дают
    /// пустой список — сетка звонков не то, ради чего стоит ронять запуск приложения.
    /// Результат отсортирован по <see cref="PairSlot.Order"/>, затем по времени начала.
    /// </summary>
    public static IReadOnlyList<PairSlot> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var slots = JsonSerializer.Deserialize<List<PairSlot>>(json, SerializerOptions);
            if (slots is null)
            {
                return [];
            }

            return [.. slots.OrderBy(s => s.Order).ThenBy(s => s.Start)];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Сериализует сетку звонков; <see langword="null"/> и пустой список дают <c>"[]"</c>.</summary>
    public static string Serialize(IReadOnlyList<PairSlot>? slots) =>
        slots is null || slots.Count == 0
            ? "[]"
            : JsonSerializer.Serialize(slots.OrderBy(s => s.Order).ThenBy(s => s.Start), SerializerOptions);

    /// <summary>
    /// Слот, которому принадлежит момент <paramref name="time"/> (начало ≤ time &lt; конец), либо
    /// ближайший следующий по времени, либо <see langword="null"/>, если сетка пуста.
    /// Нужен предзаполнению формы при клике по пустой ячейке расписания.
    /// </summary>
    public static PairSlot? SlotFor(IReadOnlyList<PairSlot> slots, TimeOnly time)
    {
        if (slots is null || slots.Count == 0)
        {
            return null;
        }

        foreach (var slot in slots)
        {
            if (slot.Start <= time && time < slot.End)
            {
                return slot;
            }
        }

        PairSlot? next = null;
        foreach (var slot in slots)
        {
            if (slot.Start > time && (next is null || slot.Start < next.Value.Start))
            {
                next = slot;
            }
        }

        return next;
    }
}
