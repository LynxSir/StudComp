namespace StudComp.ViewModels;

/// <summary>
/// Пара «значение + русская подпись» для <c>ComboBox</c> в формах. Общий тип для всех разделов:
/// им пользуются и Органайзер, и Архивариус.
/// </summary>
public sealed record NamedChoice<T>(T Value, string Display);
