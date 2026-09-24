using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StudComp.Controls;

/// <summary>
/// Ввод меток чипами с автодополнением (new_addons.md §8.2): готовые метки — «таблетками», новая
/// набирается в той же строке.
/// </summary>
/// <remarks>
/// Поле со списком через запятую, каким метки набирались в 12.6, честно работало, но не показывало
/// главного: какие метки уже есть в картотеке. Здесь подсказки живут прямо под курсором, а сама
/// коллекция остаётся обычным <see cref="ObservableCollection{T}"/> — вьюмодель ничего не знает
/// про контрол.
/// </remarks>
public partial class TagChipsEditor : UserControl
{
    /// <summary>Сколько подсказок показывать — дальше список превращается в стену текста.</summary>
    private const int MaxSuggestions = 8;

    public static readonly DependencyProperty TagsProperty = DependencyProperty.Register(
        nameof(Tags),
        typeof(IList),
        typeof(TagChipsEditor),
        new FrameworkPropertyMetadata(null, OnTagsChanged));

    public static readonly DependencyProperty SuggestionsProperty = DependencyProperty.Register(
        nameof(Suggestions),
        typeof(IEnumerable),
        typeof(TagChipsEditor),
        new PropertyMetadata(null));

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.Register(
        nameof(Placeholder),
        typeof(string),
        typeof(TagChipsEditor),
        new PropertyMetadata("метка"));

    public TagChipsEditor()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Метки карточки. Контрол правит эту коллекцию <b>на месте</b> и никогда не присваивает новую
    /// ссылку — поэтому привязка односторонняя. Двусторонняя здесь не просто лишняя: на свойстве
    /// только для чтения (а вьюмодели именно так и отдают коллекции) она валит биндинг исключением.
    /// </summary>
    public IList? Tags
    {
        get => (IList?)GetValue(TagsProperty);
        set => SetValue(TagsProperty, value);
    }

    /// <summary>Существующие метки картотеки — из них собирается автодополнение.</summary>
    public IEnumerable? Suggestions
    {
        get => (IEnumerable?)GetValue(SuggestionsProperty);
        set => SetValue(SuggestionsProperty, value);
    }

    /// <summary>Подсказка в пустой строке ввода.</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>Передать фокус вводу — панель зовёт это, когда открывает правку меток.</summary>
    public void FocusInput() => Input.Focus();

    private static void OnTagsChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TagChipsEditor editor)
        {
            editor.ChipsHost.ItemsSource = e.NewValue as IEnumerable;
        }
    }

    private void OnFrameClick(object sender, MouseButtonEventArgs e) => Input.Focus();

    private void OnInputChanged(object sender, TextChangedEventArgs e)
    {
        Watermark.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSuggestions();
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
            case Key.Tab when Input.Text.Length > 0:
                Commit(SuggestionsPopup.IsOpen && SuggestionsList.SelectedItem is string picked
                    ? picked
                    : Input.Text);
                e.Handled = true;
                break;

            case Key.OemComma:
            case Key.OemSemicolon:
                Commit(Input.Text);
                e.Handled = true;
                break;

            case Key.Back when Input.Text.Length == 0:
                RemoveLast();
                e.Handled = true;
                break;

            case Key.Down when SuggestionsPopup.IsOpen:
                SuggestionsList.SelectedIndex = 0;
                SuggestionsList.Focus();
                e.Handled = true;
                break;

            case Key.Escape when SuggestionsPopup.IsOpen:
                SuggestionsPopup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void OnInputLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Набранное, но не подтверждённое слово — всё равно метка: терять её при уходе фокуса
        // означало бы наказывать за то, что пользователь не нажал Enter. Исключение — открытые
        // подсказки: там уход фокуса означает клик по одной из них, и добавить надо её, а не
        // недонабранный обрывок.
        if (!SuggestionsPopup.IsOpen)
        {
            Commit(Input.Text);
        }
    }

    private void OnSuggestionsKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter when SuggestionsList.SelectedItem is string picked:
                Commit(picked);
                e.Handled = true;
                break;

            case Key.Escape:
                SuggestionsPopup.IsOpen = false;
                Input.Focus();
                e.Handled = true;
                break;

            case Key.Up when SuggestionsList.SelectedIndex == 0:
                Input.Focus();
                e.Handled = true;
                break;
        }
    }

    private void OnSuggestionClick(object sender, MouseButtonEventArgs e)
    {
        if (SuggestionsList.SelectedItem is string picked)
        {
            Commit(picked);
        }
    }

    private void OnRemoveChipClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag })
        {
            Tags?.Remove(tag);
            UpdateSuggestions();
        }
    }

    private void Commit(string? value)
    {
        var name = (value ?? string.Empty).Trim().TrimStart('#').Trim();
        Input.Clear();
        Watermark.Visibility = Visibility.Visible;
        SuggestionsPopup.IsOpen = false;

        if (name.Length == 0 || Tags is null || Contains(name))
        {
            return;
        }

        Tags.Add(name);
    }

    private void RemoveLast()
    {
        if (Tags is { Count: > 0 })
        {
            Tags.RemoveAt(Tags.Count - 1);
        }
    }

    private bool Contains(string name)
    {
        if (Tags is null)
        {
            return false;
        }

        foreach (var existing in Tags)
        {
            if (existing is string text && string.Equals(text, name, StringComparison.CurrentCultureIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void UpdateSuggestions()
    {
        var typed = Input.Text.Trim().TrimStart('#').Trim();
        if (typed.Length == 0 || Suggestions is null)
        {
            SuggestionsPopup.IsOpen = false;
            return;
        }

        var matches = new List<string>(MaxSuggestions);
        foreach (var candidate in Suggestions)
        {
            if (candidate is not string text
                || text.IndexOf(typed, StringComparison.CurrentCultureIgnoreCase) < 0
                || Contains(text))
            {
                continue;
            }

            matches.Add(text);
            if (matches.Count == MaxSuggestions)
            {
                break;
            }
        }

        SuggestionsList.ItemsSource = matches;
        SuggestionsPopup.IsOpen = matches.Count > 0;
    }
}
