namespace StudComp.ViewModels.Shell;

/// <summary>
/// Карточки изменились: создана, отправлена в корзину, отложена или повторена (new_addons.md §2.1).
/// </summary>
/// <remarks>
/// Нужно ровно для одного — пересчитать бейдж «к повторению» на иконке раздела. Считать его по
/// таймеру было бы враньём в обе стороны: то с задержкой, то впустую. Получатель —
/// <c>MainWindowViewModel</c>, отправители — библиотека, Хаб и Дашборд.
/// </remarks>
public sealed record CardsChangedMessage;
