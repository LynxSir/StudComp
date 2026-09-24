namespace StudComp.Core.Common;

/// <summary>
/// Ожидаемая, не исключительная ошибка. Переносится в <see cref="Result"/> и <see cref="Result{T}"/>.
/// </summary>
/// <param name="Code">
/// Устойчивый машиночитаемый код вида <c>область.причина</c> (например, <c>archivist.file_locked</c>).
/// Вызывающий ветвится по нему, а не по <paramref name="Message"/>; он же станет ключом ресурса,
/// когда строки UI поедут в <c>.resx</c> (ARCHITECTURE §14).
/// </param>
/// <param name="Message">Человекочитаемое описание для UI и логов. Не stack trace (ARCHITECTURE §11.3).</param>
public readonly record struct Error(string Code, string Message)
{
    /// <summary>Значение по умолчанию — «ошибки нет».</summary>
    public static readonly Error None = default;

    /// <summary>True, если это дефолтный экземпляр, то есть никакая ошибка не описана.</summary>
    public bool IsNone => string.IsNullOrEmpty(Code);

    public override string ToString() => IsNone ? "<none>" : $"{Code}: {Message}";
}
