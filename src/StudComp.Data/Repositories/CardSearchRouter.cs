using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StudComp.Core.Domain;

namespace StudComp.Data.Repositories;

/// <summary>
/// Выбирает реализацию поиска: FTS5, если виртуальная таблица на месте, иначе деградированный
/// режим (new_addons.md §4.3).
/// </summary>
/// <remarks>
/// Проверка однократная и <b>ленивая</b> — при первом обращении к поиску, а не на старте
/// приложения: раздел не имеет права утяжелять холодный старт (new_addons.md §9).
/// </remarks>
internal sealed class CardSearchRouter(
    IDbContextFactory<StudCompDbContext> contextFactory,
    CardSearchRepository fullText,
    FallbackCardSearchRepository fallback,
    ILogger<CardSearchRouter> logger) : ICardSearchRepository
{
    private readonly SemaphoreSlim _probeLock = new(1, 1);

    private bool? _fullTextAvailable;

    public async Task<IReadOnlyList<CardSearchHit>> SearchAsync(
        CardQuerySpec spec,
        CardSearchOptions options,
        CancellationToken ct = default)
    {
        var active = await ResolveAsync(ct).ConfigureAwait(false);
        return await active.SearchAsync(spec, options, ct).ConfigureAwait(false);
    }

    public async Task<bool> IsFullTextAvailableAsync(CancellationToken ct = default)
    {
        await ResolveAsync(ct).ConfigureAwait(false);
        return _fullTextAvailable ?? false;
    }

    public async Task<int> RebuildAsync(CancellationToken ct = default)
    {
        var active = await ResolveAsync(ct).ConfigureAwait(false);
        return await active.RebuildAsync(ct).ConfigureAwait(false);
    }

    private async Task<ICardSearchRepository> ResolveAsync(CancellationToken ct)
    {
        if (_fullTextAvailable is { } known)
        {
            return known ? fullText : fallback;
        }

        await _probeLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_fullTextAvailable is not { } cached)
            {
                cached = await ProbeAsync(ct).ConfigureAwait(false);
                _fullTextAvailable = cached;

                if (!cached)
                {
                    logger.LogWarning(
                        "Поисковый индекс картотеки недоступен — поиск работает в деградированном "
                        + "режиме. Помогает «Перестроить поисковый индекс» в настройках раздела.");
                }
            }

            return cached ? fullText : fallback;
        }
        finally
        {
            _probeLock.Release();
        }
    }

    private async Task<bool> ProbeAsync(CancellationToken ct)
    {
        try
        {
            await using var context = await contextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
            return await CardSearchRepository.IsAvailableAsync(context, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Проверка наличия таблицы не имеет права ронять поиск: в худшем случае деградируем.
            logger.LogWarning(ex, "Не удалось проверить наличие поискового индекса картотеки");
            return false;
        }
    }
}
