using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;
using Wpf.Ui.Appearance;

namespace StudComp.Services;

/// <inheritdoc cref="IMotionService"/>
internal sealed partial class MotionService(
    IOptionsMonitor<AppearanceOptions> options,
    ILogger<MotionService> logger) : ObservableObject, IMotionService
{
    // Длительности переходов в миллисекундах при включённых анимациях.
    internal const int FastMs = 130;
    internal const int MediumMs = 220;
    internal const int SlowMs = 340;

    [ObservableProperty]
    private double _fontScale = 1.0;

    [ObservableProperty]
    private bool _animationsEnabled = true;

    // Что уже применено. Нужно, потому что монитор настроек срабатывает на ЛЮБУЮ запись
    // usersettings.json, а не только на свою секцию: сменил человек папку Архивариуса — сюда всё
    // равно прилетает вызов. А каждая запись в словарь ресурсов приложения запускает обход всего
    // дерева с инвалидацией DynamicResource-ссылок, и делать это на ровном месте нельзя.
    private bool? _appliedAnimations;
    private AppDensity? _appliedDensity;
    private string? _appliedAccent;

    /// <summary>
    /// Зовётся из <c>App.OnStartup</c> <b>до</b> создания главного окна (Phase 13.10): пока дерева
    /// нет, записи ресурсов дешёвые и безопасные. На живом окне те же записи — полный обход дерева, и
    /// именно на нём ловили падение композиции WPF.
    /// </summary>
    public void Initialize()
    {
        Apply(options.CurrentValue);

        // Внешние изменения (восстановление из копии, правка файла) — применяем на UI-потоке.
        options.OnChange(o => Dispatch(() => Apply(o)));
    }

    public void ApplyAccent(string accent)
    {
        if (string.Equals(_appliedAccent, accent, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _appliedAccent = accent;

        var color = AccentPalette.Resolve(accent);
        var hover = AccentPalette.Lighten(color, 0.14);
        var pressed = AccentPalette.Darken(color, 0.14);

        SetResources(new Dictionary<string, object>
        {
            ["ColorPrimary"] = color,
            ["ColorPrimaryHover"] = hover,
            ["ColorPrimaryPressed"] = pressed,
        });
        SetBrush("ColorPrimaryBrush", color);
        SetBrush("ColorPrimaryHoverBrush", hover);
        SetBrush("ColorPrimaryPressedBrush", pressed);

        try
        {
            ApplicationAccentColorManager.Apply(color, ApplicationThemeManager.GetAppTheme());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Не удалось применить акцент WPF-UI");
        }
    }

    public void ApplyDensity(AppDensity density)
    {
        if (_appliedDensity == density)
        {
            return;
        }

        _appliedDensity = density;

        var compact = density == AppDensity.Compact;
        SetResources(new Dictionary<string, object>
        {
            ["Space.Page.Padding"] = compact ? new Thickness(20, 16, 20, 16) : new Thickness(32, 24, 32, 24),
            ["Space.Card.Padding"] = compact ? new Thickness(12, 9, 12, 9) : new Thickness(16, 14, 16, 14),
            ["Space.Card.Margin"] = compact ? new Thickness(0, 0, 0, 6) : new Thickness(0, 0, 0, 10),
            ["Space.Group.Margin"] = compact ? new Thickness(0, 12, 0, 6) : new Thickness(0, 20, 0, 8),
            ["Space.Control.MinHeight"] = compact ? 28.0 : 34.0,
        });
    }

    public void ApplyFontScale(double scale)
    {
        FontScale = double.IsFinite(scale) ? Math.Clamp(scale, 0.8, 1.4) : 1.0;
    }

    /// <summary>
    /// Переключение анимаций не пишет ни одного ресурса приложения: длительности живут в
    /// <see cref="Motion"/>, откуда их читает код-behind в момент запуска анимации. Это и есть
    /// исправление краха композиции из лога 2026-09-14 (см. <see cref="Motion"/>).
    /// </summary>
    public void ApplyAnimations(bool enabled)
    {
        if (_appliedAnimations == enabled)
        {
            return;
        }

        _appliedAnimations = enabled;

        Motion.Fast = new Duration(TimeSpan.FromMilliseconds(enabled ? FastMs : 0));
        Motion.Medium = new Duration(TimeSpan.FromMilliseconds(enabled ? MediumMs : 0));
        Motion.Slow = new Duration(TimeSpan.FromMilliseconds(enabled ? SlowMs : 0));

        AnimationsEnabled = enabled;
    }

    private void Apply(AppearanceOptions o)
    {
        ApplyAnimations(o.AnimationsEnabled);
        ApplyDensity(o.Density);
        ApplyAccent(o.Accent);
        ApplyFontScale(o.FontScale);
    }

    /// <summary>
    /// Записывает набор ресурсов приложения — в тот словарь, который уже владеет ключом
    /// (<c>Spacing.xaml</c>, <c>Colors.xaml</c>), а не в верхний уровень: замена существующего
    /// значения инвалидирует только ссылки на этот ключ, тогда как добавление нового ключа в
    /// верхний словарь заставляет WPF переоценить неявные стили у всех элементов окна. Сбой записи
    /// — ошибка в лог с полным стеком (не окно), но и не молчаливое предупреждение: в 13.6 именно
    /// оно скрыло корень падения композиции.
    /// </summary>
    private void SetResources(IReadOnlyDictionary<string, object> values)
    {
        var app = Application.Current.Resources;

        foreach (var (key, value) in values)
        {
            try
            {
                var owner = FindOwner(app, key) ?? app;
                owner[key] = value;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Не удалось применить ресурс оформления {Key}", key);
            }
        }
    }

    private void SetBrush(string key, Color color)
    {
        var app = Application.Current.Resources;
        if (app[key] is SolidColorBrush { IsFrozen: false } brush)
        {
            // Мутируем общий объект кисти — обновятся и StaticResource-, и DynamicResource-держатели,
            // а словарь ресурсов при этом не трогается вовсе.
            brush.Color = color;
            return;
        }

        try
        {
            var owner = FindOwner(app, key) ?? app;
            owner[key] = new SolidColorBrush(color);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Не удалось применить кисть оформления {Key}", key);
        }
    }

    /// <summary>Словарь (сам или один из вложенных), в чьих собственных ключах есть <paramref name="key"/>.</summary>
    private static ResourceDictionary? FindOwner(ResourceDictionary dictionary, string key)
    {
        foreach (var own in dictionary.Keys)
        {
            if (Equals(own, key))
            {
                return dictionary;
            }
        }

        foreach (var merged in dictionary.MergedDictionaries)
        {
            if (FindOwner(merged, key) is { } owner)
            {
                return owner;
            }
        }

        return null;
    }

    private void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
            return;
        }

        // Исключение внутри InvokeAsync никто не ждёт — без наблюдателя оно всплывает как
        // «необработанное в фоновой задаче». Наблюдаем и пишем предупреждение сами.
        dispatcher.InvokeAsync(action).Task.ContinueWith(
            t => logger.LogWarning(t.Exception, "Не удалось применить настройки оформления"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
