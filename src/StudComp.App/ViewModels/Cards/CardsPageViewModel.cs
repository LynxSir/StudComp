using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StudComp.Services;

namespace StudComp.ViewModels.Cards;

/// <summary>Вкладки раздела «Картотека» (new_addons.md §2.2).</summary>
public enum CardsTab
{
    Library = 0,
    Review = 1,
    Exam = 2,
    Stats = 3,
}

/// <summary>
/// Параметр навигации в раздел «Картотека»: открыть вкладку, карточку или заранее заданный запрос.
/// </summary>
/// <param name="CardId">Карточка, которую нужно показать и выделить.</param>
/// <param name="Query">Строка поиска, с которой открыть библиотеку.</param>
/// <param name="Tab">Вкладка, на которой открыть раздел.</param>
public sealed record CardsPageParameter(Guid? CardId = null, string? Query = null, CardsTab? Tab = null);

/// <summary>
/// Раздел «Картотека» (new_addons.md §2.2): библиотека, повторение, экзамен и статистика.
/// </summary>
/// <remarks>
/// Загрузка идёт из <see cref="OnNavigatedTo"/>, а обработчика <c>Loaded</c> у страницы нет:
/// смешение этих двух источников уже давало двойную загрузку в Отчётах (Phase 12.4).
/// </remarks>
public sealed partial class CardsPageViewModel : ObservableObject, INavigationAware, IPersistentPage, IDisposable
{
    public CardsPageViewModel(
        CardLibraryViewModel library,
        CardReviewViewModel review,
        CardExamViewModel exam,
        CardStatsViewModel stats)
    {
        Library = library;
        Review = review;
        Exam = exam;
        Stats = stats;
    }

    public CardLibraryViewModel Library { get; }

    public CardReviewViewModel Review { get; }

    public CardExamViewModel Exam { get; }

    public CardStatsViewModel Stats { get; }

    [ObservableProperty]
    private int _selectedTabIndex;

    partial void OnSelectedTabIndexChanged(int value)
    {
        UiActivity.Mark($"Вкладка Картотека/{value}");
        _ = RefreshCurrentAsync();
    }

    /// <summary>Перечитать данные активной вкладки.</summary>
    [RelayCommand]
    public Task RefreshCurrentAsync() => (CardsTab)SelectedTabIndex switch
    {
        CardsTab.Review => Review.RefreshAsync(),
        CardsTab.Exam => Exam.RefreshAsync(),
        CardsTab.Stats => Stats.RefreshAsync(),
        _ => Library.RefreshAsync(),
    };

    /// <inheritdoc />
    public void OnNavigatedTo(object? parameter)
    {
        _ = OpenAsync(parameter as CardsPageParameter);
    }

    /// <inheritdoc />
    public void OnNavigatedFrom()
    {
        // Уходим со страницы — несохранённая правка в панели обязана доехать до БД.
        _ = Library.FlushAsync();
    }

    /// <summary>Зовётся навигацией после ухода со страницы (Phase 13.10): отписки и отмена поиска.</summary>
    public void Dispose() => Library.Dispose();

    private async Task OpenAsync(CardsPageParameter? parameter)
    {
        if (parameter?.Query is { Length: > 0 } query)
        {
            Library.Query = query;
        }

        // Смена вкладки сама тянет за собой загрузку — иначе данные читались бы дважды.
        var tab = (int)(parameter?.Tab ?? CardsTab.Library);
        if (SelectedTabIndex != tab)
        {
            SelectedTabIndex = tab;
        }
        else
        {
            await RefreshCurrentAsync().ConfigureAwait(true);
        }

        if (parameter?.CardId is { } cardId)
        {
            await Library.FocusCardAsync(cardId).ConfigureAwait(true);
        }
    }
}
