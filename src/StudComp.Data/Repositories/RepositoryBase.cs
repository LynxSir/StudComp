using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace StudComp.Data.Repositories;

/// <summary>
/// Общий предок тонких репозиториев (ARCHITECTURE §7.3): держит только
/// <see cref="IDbContextFactory{TContext}"/> и отдаёт свежий контекст на операцию.
/// Никакого состояния, никакого <c>IQueryable</c> наружу.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="CreateContextAsync"/> — единственная точка, где запрос уходит с вызывающего потока
/// (Phase 13.10). SQLite не умеет асинхронный ввод-вывод: все <c>*Async</c> у
/// <c>Microsoft.Data.Sqlite</c> завершаются синхронно, и EF-<c>await</c>'ы поверх них поток не
/// переключают. До этой правки каждый запрос из ViewModel — открытие соединения, компиляция LINQ,
/// выполнение, материализация — целиком выполнялся на UI-потоке, и навигация с 9–20 запросами
/// «залипала» на сотни миллисекунд.
/// </para>
/// <para>
/// Теперь метод возвращает <see cref="BackgroundContextAwaitable"/>: его <c>IsCompleted</c> всегда
/// <c>false</c>, продолжение ставится в пул потоков, а контекст создаётся уже там. Все методы
/// репозиториев пишут <c>await CreateContextAsync(ct).ConfigureAwait(false)</c>, поэтому и остаток
/// метода, и синхронно завершающиеся EF-вызовы внутри него выполняются на пуле. Вариант с
/// <c>Task.Run</c> отвергнут: уже завершённая задача продолжается инлайн на вызывающем потоке
/// (гонка между созданием контекста и <c>await</c>), у awaitable с жёстким <c>IsCompleted=false</c>
/// такой гонки нет.
/// </para>
/// </remarks>
internal abstract class RepositoryBase(IDbContextFactory<StudCompDbContext> contextFactory)
{
    /// <summary>
    /// Переключает выполнение на пул потоков и там создаёт контекст. Обязательно
    /// <c>await ... .ConfigureAwait(false)</c> — иначе продолжение вернётся на Dispatcher, и смысл
    /// переключения пропадёт.
    /// </summary>
    protected BackgroundContextAwaitable CreateContextAsync(CancellationToken ct) => new(contextFactory, ct);

    /// <summary>
    /// Awaitable «уйти на пул и создать контекст». Всегда планирует продолжение асинхронно;
    /// <see cref="ConfigureAwait"/> есть только ради единообразия с прежней сигнатурой на
    /// <see cref="Task"/> — ни на что не влияет.
    /// </summary>
    protected readonly struct BackgroundContextAwaitable(
        IDbContextFactory<StudCompDbContext> factory,
        CancellationToken ct) : ICriticalNotifyCompletion
    {
        public BackgroundContextAwaitable ConfigureAwait(bool continueOnCapturedContext) => this;

        public BackgroundContextAwaitable GetAwaiter() => this;

        /// <summary>Всегда <c>false</c>: продолжение никогда не выполняется инлайн на вызывающем потоке.</summary>
        public bool IsCompleted => false;

        public void OnCompleted(Action continuation) =>
            ThreadPool.QueueUserWorkItem(static c => c(), continuation, preferLocal: false);

        public void UnsafeOnCompleted(Action continuation) =>
            ThreadPool.UnsafeQueueUserWorkItem(static c => c(), continuation, preferLocal: false);

        /// <summary>Уже на пуле: проверить отмену и создать контекст.</summary>
        public StudCompDbContext GetResult()
        {
            ct.ThrowIfCancellationRequested();
            return factory.CreateDbContext();
        }
    }
}
