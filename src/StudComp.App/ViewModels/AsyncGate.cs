namespace StudComp.ViewModels;

/// <summary>
/// Асинхронная защёлка «по одному за раз» для методов обновления ViewModel (Phase 13.10).
/// </summary>
/// <remarks>
/// Пока запросы к SQLite завершались синхронно, два вызова <c>RefreshAsync</c> подряд (например,
/// <c>Loaded</c> страницы и смена вкладки) физически не могли наложиться. После переноса запросов
/// на пул потоков наложение стало возможным, а типовой код обновления — <c>Clear()</c>, затем
/// <c>await</c>, затем цикл <c>Add</c> — при наложении даёт дубли строк. Защёлка возвращает прежнюю
/// последовательную семантику: второй вызов ждёт первого и выполняется после него. Используется
/// как <c>using var _ = await _gate.EnterAsync();</c> в начале метода — продолжение возвращается на
/// UI-поток (контекст не сбрасывается намеренно), поэтому мутации коллекций остаются безопасными.
/// </remarks>
public sealed class AsyncGate
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    /// <summary>Войти в защёлку; освободить — через <see cref="IDisposable.Dispose"/> результата.</summary>
    public async Task<Releaser> EnterAsync()
    {
        await _semaphore.WaitAsync();
        return new Releaser(_semaphore);
    }

    /// <summary>Держатель защёлки — освобождает её при <see cref="Dispose"/>.</summary>
    public readonly struct Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
