using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;

namespace StudComp.Services;

/// <inheritdoc cref="INavigationService"/>
/// <remarks>
/// <see cref="ObservableObject"/>, чтобы <c>{Binding Navigation.CurrentView}</c> в XAML получал
/// уведомления об изменении. ViewModel-страницы резолвятся из <see cref="IServiceProvider"/>
/// (они <c>Transient</c>), поэтому стек «назад» хранит не сами объекты, а пару «тип + параметр»:
/// без параметра возврат в Хаб предмета открывал бы пустую страницу. Исключение — разделы сайдбара
/// с маркером <see cref="IPersistentPage"/>: их VM и View создаются один раз и живут до конца
/// процесса (Phase 13.10).
/// </remarks>
internal sealed partial class NavigationService(
    IServiceProvider services,
    IOptions<DiagnosticsOptions> diagnostics,
    ILogger<NavigationService> logger) : ObservableObject, INavigationService
{
    /// <summary>Потолок стека — за долгую сессию он иначе растёт без предела.</summary>
    private const int MaxBackStack = 16;

    private readonly List<Entry> _backStack = [];

    // Кеш разделов сайдбара: тип VM → (VM, готовый View).
    private readonly Dictionary<Type, (object ViewModel, FrameworkElement View)> _persistent = [];

    private object? _currentParameter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoBack))]
    private object? _currentViewModel;

    [ObservableProperty]
    private object? _currentView;

    public bool CanGoBack => _backStack.Count > 0;

    public event EventHandler? Navigated;

    public void NavigateTo<TViewModel>(object? parameter = null) where TViewModel : class =>
        NavigateTo(typeof(TViewModel), parameter);

    public void NavigateTo(Type viewModelType, object? parameter = null)
    {
        ArgumentNullException.ThrowIfNull(viewModelType);

        // Тот же тип с тем же параметром — уже открыт. Сравнение по параметру обязательно: без него
        // переход «Хаб Матана → Хаб Физики» молча не срабатывал бы.
        if (CurrentViewModel is not null
            && CurrentViewModel.GetType() == viewModelType
            && Equals(_currentParameter, parameter))
        {
            return;
        }

        if (CurrentViewModel is not null)
        {
            _backStack.Add(new Entry(CurrentViewModel.GetType(), _currentParameter));
            if (_backStack.Count > MaxBackStack)
            {
                _backStack.RemoveAt(0);
            }
        }

        Show(viewModelType, parameter);
    }

    public void GoBack()
    {
        if (_backStack.Count == 0)
        {
            return;
        }

        var previous = _backStack[^1];
        _backStack.RemoveAt(_backStack.Count - 1);
        Show(previous.ViewModelType, previous.Parameter);
    }

    private void Show(Type viewModelType, object? parameter)
    {
        var name = viewModelType.Name;
        UiActivity.Mark($"Навигация → {name}");
        var measure = diagnostics.Value.PerformanceLoggingEnabled;
        var clock = measure ? Stopwatch.StartNew() : null;

        var previous = CurrentViewModel;
        (previous as INavigationAware)?.OnNavigatedFrom();

        var (viewModel, view, cached) = Resolve(viewModelType);
        var resolveMs = clock?.ElapsedMilliseconds ?? 0;

        _currentParameter = parameter;
        CurrentViewModel = viewModel;
        CurrentView = view;
        var assignMs = (clock?.ElapsedMilliseconds ?? 0) - resolveMs;

        (viewModel as INavigationAware)?.OnNavigatedTo(parameter);
        var navigatedToMs = (clock?.ElapsedMilliseconds ?? 0) - resolveMs - assignMs;

        // Старая Transient-страница больше никому не нужна: подписки на singleton-сервисы
        // (наблюдатель Архивариуса, таймеры) обязаны отцепиться, иначе «зомби»-VM продолжают
        // реагировать на события и ходить в БД до конца процесса (Phase 13.10). Кешируемые разделы
        // живут дальше — их не трогаем.
        if (previous is IDisposable disposable && !_persistent.ContainsKey(previous.GetType()))
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Ошибка при освобождении страницы {Page}", previous.GetType().Name);
            }
        }

        OnPropertyChanged(nameof(CanGoBack));
        Navigated?.Invoke(this, EventArgs.Empty);

        if (clock is null)
        {
            return;
        }

        // Дерево страницы (для некешируемых) строится не в сеттере, а в ближайшем проходе layout
        // (ContentPresenter применяет DataTemplate при Measure), поэтому «до кадра» меряем операцией
        // с приоритетом Loaded — она встаёт в очередь после Render.
        Application.Current?.Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            () => logger.LogInformation(
                "Навигация {Page}{Cached}: VM {ResolveMs} мс, присваивание {AssignMs} мс, OnNavigatedTo {NavigatedToMs} мс, до кадра {TotalMs} мс",
                name,
                cached ? " (из кеша)" : string.Empty,
                resolveMs,
                assignMs,
                navigatedToMs,
                clock.ElapsedMilliseconds));
    }

    /// <summary>
    /// Для <see cref="IPersistentPage"/> — VM и View из кеша (при первом заходе строятся и
    /// запоминаются); для остальных — свежая VM, View подберёт хост по <c>DataTemplate</c>.
    /// </summary>
    private (object ViewModel, object View, bool Cached) Resolve(Type viewModelType)
    {
        if (!typeof(IPersistentPage).IsAssignableFrom(viewModelType))
        {
            var transient = services.GetRequiredService(viewModelType);
            return (transient, transient, false);
        }

        if (_persistent.TryGetValue(viewModelType, out var entry))
        {
            return (entry.ViewModel, entry.View, true);
        }

        var viewModel = services.GetRequiredService(viewModelType);
        var view = BuildView(viewModel);
        _persistent[viewModelType] = (viewModel, view);
        return (viewModel, view, false);
    }

    /// <summary>
    /// Строит View по неявному <c>DataTemplate</c> из ресурсов приложения — ровно так, как это сделал
    /// бы <c>ContentPresenter</c>, только результат сохраняется.
    /// </summary>
    private static FrameworkElement BuildView(object viewModel)
    {
        var key = new DataTemplateKey(viewModel.GetType());
        if (Application.Current?.TryFindResource(key) is DataTemplate template
            && template.LoadContent() is FrameworkElement view)
        {
            view.DataContext = viewModel;
            return view;
        }

        throw new InvalidOperationException(
            $"Для страницы {viewModel.GetType().Name} не найден DataTemplate в ресурсах приложения");
    }

    private readonly record struct Entry(Type ViewModelType, object? Parameter);
}
