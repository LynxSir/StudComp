using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Services;

namespace StudComp.ViewModels.Archivist;

/// <summary>
/// Раздел «Архивариус» (ARCHITECTURE §8): вкладки «Неразобранное», «Правила», «Журнал».
/// Активная вкладка перечитывает данные при показе и переключении (ADR §16.23).
/// </summary>
public sealed partial class ArchivistPageViewModel : ObservableObject, INavigationAware, IPersistentPage, IDisposable
{
    public ArchivistPageViewModel(UnsortedFilesViewModel unsorted, RulesViewModel rules, OperationsLogViewModel history)
    {
        Unsorted = unsorted;
        Rules = rules;
        History = history;

        // Ссылка «Открыть в Неразобранном» на строке Журнала (Phase 12.2) — переключает вкладку без
        // отдельного контракта между дочерними VM.
        History.RequestOpenUnsorted = () => SelectedTabIndex = 0;
    }

    public UnsortedFilesViewModel Unsorted { get; }

    public RulesViewModel Rules { get; }

    public OperationsLogViewModel History { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        UiActivity.Mark($"Вкладка Архивариус/{value}");
        _ = RefreshCurrentAsync();
    }

    /// <inheritdoc />
    public void OnNavigatedTo(object? parameter)
    {
        // Страница кешируется и переключается через Visibility, Loaded повторно не стреляет —
        // единственный источник обновления при показе теперь здесь (Phase 13.10).
        _ = RefreshCurrentAsync();
    }

    /// <inheritdoc />
    public void OnNavigatedFrom()
    {
    }

    /// <summary>Перечитать данные активной вкладки.</summary>
    [RelayCommand]
    public Task RefreshCurrentAsync() => SelectedTabIndex switch
    {
        1 => Rules.RefreshAsync(),
        2 => History.RefreshAsync(),
        _ => Unsorted.RefreshAsync(),
    };

    /// <summary>
    /// Зовётся навигацией при уходе со страницы (Phase 13.10): дочерние вкладки подписаны на
    /// singleton-наблюдатель, без отписки они жили бы до конца процесса и на каждое его событие
    /// ходили в БД.
    /// </summary>
    public void Dispose()
    {
        Unsorted.Dispose();
        History.Dispose();
    }
}
