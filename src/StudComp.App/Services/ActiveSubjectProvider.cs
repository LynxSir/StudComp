using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Settings;
using StudComp.Modules.Organizer.Services;

namespace StudComp.Services;

/// <summary>
/// Реализация <see cref="IActiveSubjectProvider"/>. Реализует <see cref="IHostedService"/> ради момента
/// создания и запуска минутного таймера (прецедент <c>DeadlineLinkSuggestionService</c>).
/// </summary>
/// <remarks>
/// Даты семестра берутся из <see cref="ISemesterService"/> — он держит активный семестр в кеше,
/// поэтому минутный тик не ходит за ними в БД (new_addons.md §5).
/// </remarks>
internal sealed class ActiveSubjectProvider(
    IScheduleService scheduleService,
    ISubjectService subjectService,
    ISemesterService semesterService,
    ILogger<ActiveSubjectProvider> logger) : IActiveSubjectProvider, IHostedService, IDisposable
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(1);

    // Первый расчёт — с задержкой: StartAsync хостед-сервиса выполняется до IDbInitializer, и на
    // первом запуске таблиц ещё нет. К этому моменту миграции уже накатаны.
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(4);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _loopCts;
    private Task? _loop;
    private Guid? _pinnedSubjectId;

    public ActiveSubjectInfo? Current { get; private set; }

    public event EventHandler? Changed;

    public void PinSubject(Guid? subjectId)
    {
        _pinnedSubjectId = subjectId;
        _ = RefreshAsync();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _loopCts = new CancellationTokenSource();
        _loop = RunLoopAsync(_loopCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_loopCts is null)
        {
            return;
        }

        await _loopCts.CancelAsync().ConfigureAwait(false);
        if (_loop is not null)
        {
            try
            {
                await _loop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // штатное завершение
            }
        }
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var next = await ComputeAsync(ct).ConfigureAwait(false);
            if (!Equals(next, Current))
            {
                Current = next;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось пересчитать активный предмет");
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(StartupDelay, ct).ConfigureAwait(false);
            await RefreshAsync(ct).ConfigureAwait(false);

            using var timer = new PeriodicTimer(TickInterval);
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                await RefreshAsync(ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // штатное завершение
        }
    }

    private async Task<ActiveSubjectInfo?> ComputeAsync(CancellationToken ct)
    {
        var subjects = await subjectService.GetAllAsync(ct).ConfigureAwait(false);
        if (subjects.Count == 0)
        {
            return null;
        }

        if (_pinnedSubjectId is { } pinned && subjects.FirstOrDefault(s => s.Id == pinned) is { } pinnedSubject)
        {
            return new ActiveSubjectInfo(
                pinnedSubject.Id, pinnedSubject.Name, pinnedSubject.ColorHex,
                IsOnNow: true, StartsIn: null, EndsIn: null, ClassType: null, IsPinned: true);
        }

        var entries = await scheduleService.GetAllAsync(ct).ConfigureAwait(false);
        var semester = await semesterService.GetActiveAsync(ct).ConfigureAwait(false);
        var active = ActiveClassResolver.Resolve(
            entries, DateTimeOffset.Now, semester?.StartDate, semester?.FirstWeekIsOdd ?? true);
        if (active is not { } cls)
        {
            return null;
        }

        var subject = subjects.FirstOrDefault(s => s.Id == cls.Entry.SubjectId);
        if (subject is null)
        {
            return null;
        }

        return new ActiveSubjectInfo(
            subject.Id, subject.Name, subject.ColorHex,
            cls.IsOnNow,
            cls.IsOnNow ? null : cls.StartsIn,
            cls.EndsIn,
            cls.Entry.Type,
            IsPinned: false);
    }

    public void Dispose()
    {
        _loopCts?.Dispose();
        _gate.Dispose();
    }
}
