using Microsoft.Extensions.Options;

namespace StudComp.Modules.Cards.Tests;

/// <summary>
/// Мутабельный <see cref="IOptionsMonitor{TOptions}"/> для тестов — задать значение и менять на лету.
/// Копия из <c>StudComp.Modules.Organizer.Tests</c>: общего тестового проекта в решении нет, а
/// заводить сборку ради тринадцати строк дороже, чем их продублировать.
/// </summary>
public sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
