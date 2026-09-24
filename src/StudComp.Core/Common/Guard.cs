using System.Numerics;
using System.Runtime.CompilerServices;

namespace StudComp.Core.Common;

/// <summary>
/// Проверки аргументов — про ошибки программиста: нарушение контракта должно падать громко и чиниться в коде.
/// Ожидаемые ошибки времени выполнения — это <see cref="Result"/>, не сюда (ARCHITECTURE §11.3).
/// </summary>
public static class Guard
{
    /// <summary>Бросает, если <paramref name="value"/> равно <see langword="null"/>.</summary>
    public static T NotNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        return value;
    }

    /// <summary>Бросает, если строка пуста, состоит из пробелов или равна <see langword="null"/>.</summary>
    public static string NotNullOrWhiteSpace(
        string? value,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, paramName);
        return value;
    }

    /// <summary>Бросает, если значение отрицательное.</summary>
    public static T NotNegative<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : INumber<T>
    {
        if (T.IsNegative(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must not be negative.");
        }

        return value;
    }

    /// <summary>Бросает, если значение нулевое или отрицательное.</summary>
    public static T Positive<T>(T value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : INumber<T>
    {
        if (value <= T.Zero)
        {
            throw new ArgumentOutOfRangeException(paramName, value, "Value must be positive.");
        }

        return value;
    }

    /// <summary>Бросает, если значение вне отрезка [<paramref name="min"/>, <paramref name="max"/>] (границы включаются).</summary>
    public static T InRange<T>(
        T value,
        T min,
        T max,
        [CallerArgumentExpression(nameof(value))] string? paramName = null)
        where T : IComparable<T>
    {
        if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"Value must be within [{min}, {max}].");
        }

        return value;
    }

    /// <summary>Бросает, если это <see cref="Guid.Empty"/> — все Id генерируются в коде (ARCHITECTURE §7.2).</summary>
    public static Guid NotEmpty(Guid value, [CallerArgumentExpression(nameof(value))] string? paramName = null)
    {
        if (value == Guid.Empty)
        {
            throw new ArgumentException("Value must not be an empty GUID.", paramName);
        }

        return value;
    }
}
