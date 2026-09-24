using System.Threading.Channels;

namespace StudComp.Modules.Archivist.Services;

/// <summary>
/// Однобитный сигнал «пора свериться прямо сейчас» между наблюдателем за папками и сверщиком
/// (ARCHITECTURE §8.5: переполнение буфера <c>FileSystemWatcher</c> обязано форсировать
/// reconciliation).
/// </summary>
/// <remarks>
/// Существует ради развязки: наблюдателю нужно будить сверщика, а сверщику — класть найденные файлы
/// в очередь наблюдателя. Прямые ссылки друг на друга дали бы цикл на двух singleton-ах, и контейнер
/// не смог бы их построить. Канал ёмкости 1 с <c>DropWrite</c>: десять подряд идущих запросов — это
/// всё равно один ближайший проход (ADR §16.53).
/// </remarks>
internal sealed class ReconciliationSignal
{
    private readonly Channel<string> _channel = Channel.CreateBounded<string>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    /// <summary>Попросить внеочередной проход. Причина попадает в лог сверщика.</summary>
    public void Request(string reason) => _channel.Writer.TryWrite(reason);

    /// <summary>Дождаться запроса. Возвращает причину; бросает <see cref="OperationCanceledException"/> при остановке хоста.</summary>
    public ValueTask<string> WaitAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}
