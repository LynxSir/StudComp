namespace StudComp.Core.Common;

/// <summary>
/// Результат операции, которая ожидаемо может не получиться (файл залочен, markdown не распарсился).
/// Исключения остаются для действительно исключительных ситуаций (ARCHITECTURE §11.3).
/// </summary>
public readonly struct Result
{
    private Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>Описание ошибки. При успехе — <see cref="Error.None"/>.</summary>
    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result Failure(string code, string message) => new(false, new Error(code, message));

    public static implicit operator Result(Error error) => Failure(error);

    public override string ToString() => IsSuccess ? "Success" : $"Failure({Error})";
}

/// <summary>
/// Результат операции, которая при успехе возвращает значение. См. <see cref="Result"/>.
/// </summary>
/// <typeparam name="T">Тип возвращаемого значения.</typeparam>
public readonly struct Result<T>
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error error)
    {
        IsSuccess = isSuccess;
        _value = value;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    /// <summary>Описание ошибки. При успехе — <see cref="Error.None"/>.</summary>
    public Error Error { get; }

    /// <summary>Полученное значение.</summary>
    /// <exception cref="InvalidOperationException">Если результат — ошибка.</exception>
    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read {nameof(Value)} of a failed result. {Error}");

    public static Result<T> Success(T value) => new(true, value, Error.None);

    public static Result<T> Failure(Error error) => new(false, default, error);

    public static Result<T> Failure(string code, string message) => new(false, default, new Error(code, message));

    /// <summary>Сводит обе ветки к одному значению — чтобы вокруг <see cref="Value"/> не писать if/else.</summary>
    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure)
    {
        Guard.NotNull(onSuccess);
        Guard.NotNull(onFailure);

        return IsSuccess ? onSuccess(_value!) : onFailure(Error);
    }

    /// <summary>Отбрасывает значение, оставляя только исход — для вызывающих, которым значение не нужно.</summary>
    public Result WithoutValue() => IsSuccess ? Result.Success() : Result.Failure(Error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);

    public override string ToString() => IsSuccess ? $"Success({_value})" : $"Failure({Error})";
}
