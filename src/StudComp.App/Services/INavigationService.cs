namespace StudComp.Services;

/// <summary>
/// Страница-ViewModel, которой навигация передаёт параметр (например, идентификатор предмета для
/// Хаба) и сообщает об уходе с экрана.
/// </summary>
/// <remarks>
/// <see cref="OnNavigatedTo"/> синхронный — асинхронную загрузку страница запускает сама
/// (<c>_ = LoadAsync()</c>), как это уже делает <c>OnSelectedTabIndexChanged</c> в разделах.
/// <see cref="OnNavigatedFrom"/> нужен редактору заметок, чтобы дописать несохранённое.
/// </remarks>
public interface INavigationAware
{
    void OnNavigatedTo(object? parameter);

    void OnNavigatedFrom();
}

/// <summary>
/// Маркер страницы-раздела сайдбара (Phase 13.10): её ViewModel и View создаются один раз и
/// переиспользуются при каждом заходе. Замер показал, что при навигации почти всё время UI-потока
/// уходит на построение визуального дерева страницы (применение шаблонов WPF-UI, поиск ресурсов,
/// layout) — 110–390 мс на Release даже на прогретом круге, а данные после переноса запросов на пул
/// стоят единицы миллисекунд. Страницы с параметром (Хаб предмета) и с собственным жизненным циклом
/// (сессия с таймером) маркер не носят и остаются Transient.
/// </summary>
/// <remarks>
/// Следствия для реализующей VM: <see cref="INavigationAware.OnNavigatedTo"/> зовётся при каждом
/// заходе (данные обновляются, как и раньше), состояние вкладки/фильтров переживает переход,
/// <see cref="IDisposable.Dispose"/> навигацией не вызывается.
/// </remarks>
public interface IPersistentPage;

/// <summary>
/// Простая навигация Shell'а (ARCHITECTURE §11.5): текущая страница-VM + стек «назад». Без внешних
/// URI/регионов — только переключение ViewModel, которую <c>ContentControl</c> в главном окне
/// рендерит через <c>DataTemplate</c> (либо готовый кешированный View для <see cref="IPersistentPage"/>).
/// </summary>
public interface INavigationService
{
    /// <summary>Текущая отображаемая ViewModel (или <c>null</c> до первой навигации).</summary>
    object? CurrentViewModel { get; }

    /// <summary>
    /// Что показывать в хосте: готовый View для кешируемой страницы либо сама ViewModel, для которой
    /// хост подберёт <c>DataTemplate</c>.
    /// </summary>
    object? CurrentView { get; }

    /// <summary>Есть ли куда возвращаться.</summary>
    bool CanGoBack { get; }

    /// <summary>Срабатывает после каждой смены страницы — по нему сайдбар держит подсветку.</summary>
    event EventHandler? Navigated;

    /// <summary>Перейти к странице по типу её ViewModel (резолвится из контейнера).</summary>
    void NavigateTo(Type viewModelType, object? parameter = null);

    /// <summary>Перейти к странице по типу её ViewModel (резолвится из контейнера).</summary>
    void NavigateTo<TViewModel>(object? parameter = null) where TViewModel : class;

    /// <summary>Вернуться к предыдущей странице, если она есть.</summary>
    void GoBack();
}
