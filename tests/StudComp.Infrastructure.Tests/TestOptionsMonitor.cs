using Microsoft.Extensions.Options;

namespace StudComp.Infrastructure.Tests;

/// <summary>Мутабельный <see cref="IOptionsMonitor{TOptions}"/> для тестов — задать значение и менять на лету.</summary>
internal sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; set; } = value;

    public T Get(string? name) => CurrentValue;

    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
