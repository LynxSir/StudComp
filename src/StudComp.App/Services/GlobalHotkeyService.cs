using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StudComp.Infrastructure.Settings;

namespace StudComp.Services;

/// <summary>
/// Глобальная системная горячая клавиша вызова компактного режима-шпаргалки (new_addons.md §8.6,
/// §14 вопрос 3). Опция, выключенная по умолчанию.
/// </summary>
public interface IGlobalHotkeyService
{
    /// <summary>
    /// Подготовить окно-владельца (создать его Win32-хендл, не показывая само окно) и подписаться на
    /// изменения настроек. Вызывается один раз при старте приложения.
    /// </summary>
    void Initialize(Window owner, Action onPressed);
}

/// <summary>
/// Win32-интероп в App-слое: <c>Infrastructure</c> не должен тянуть <c>System.Windows.Interop</c>
/// (ADR §16.16 — WPF-зависимые реализации живут здесь же, где <c>ThemeService</c>/<c>TrayService</c>).
/// </summary>
internal sealed class GlobalHotkeyService(
    IOptionsMonitor<CardsOptions> options,
    ILogger<GlobalHotkeyService> logger) : IGlobalHotkeyService, IDisposable
{
    private const int WM_HOTKEY = 0x0312;

    /// <summary>Идентификатор в пределах процесса — второй такой же горячей клавиши в приложении нет.</summary>
    private const int HotkeyId = 0x4341;

    private const uint ModAlt = 0x1;
    private const uint ModControl = 0x2;
    private const uint ModShift = 0x4;
    private const uint ModWin = 0x8;

    /// <summary>Не повторять срабатывание, пока клавиши зажаты.</summary>
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private HwndSource? _source;
    private Action? _onPressed;
    private bool _registered;
    private IDisposable? _subscription;

    public void Initialize(Window owner, Action onPressed)
    {
        _onPressed = onPressed;

        // EnsureHandle создаёт Win32-окно, не показывая его — RegisterHotKey нужен только хендл.
        var handle = new WindowInteropHelper(owner).EnsureHandle();
        _source = HwndSource.FromHwnd(handle);
        _source?.AddHook(WndProc);

        ApplySettings(options.CurrentValue);
        _subscription = options.OnChange(o => owner.Dispatcher.BeginInvoke(() => ApplySettings(o)));
    }

    private void ApplySettings(CardsOptions current)
    {
        Unregister();

        if (!current.GlobalHotkeyEnabled || _source is null)
        {
            return;
        }

        if (!TryParseGesture(current.GlobalHotkeyGesture, out var modifiers, out var vk))
        {
            logger.LogWarning(
                "Не удалось разобрать сочетание клавиш «{Gesture}» для глобального вызова шпаргалки.",
                current.GlobalHotkeyGesture);
            return;
        }

        _registered = RegisterHotKey(_source.Handle, HotkeyId, modifiers | ModNoRepeat, vk);
        if (!_registered)
        {
            // Конфликтует с другим приложением — молча, без диалогов и тостов (new_addons.md §8.6).
            logger.LogWarning(
                "Не удалось зарегистрировать глобальную горячую клавишу «{Gesture}» — возможно, занята другим приложением.",
                current.GlobalHotkeyGesture);
        }
    }

    private void Unregister()
    {
        if (_registered && _source is not null)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            _onPressed?.Invoke();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static bool TryParseGesture(string? gesture, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;

        if (string.IsNullOrWhiteSpace(gesture))
        {
            return false;
        }

        try
        {
            if (new KeyGestureConverter().ConvertFromInvariantString(gesture.Trim()) is not KeyGesture parsed)
            {
                return false;
            }

            if (parsed.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                modifiers |= ModAlt;
            }

            if (parsed.Modifiers.HasFlag(ModifierKeys.Control))
            {
                modifiers |= ModControl;
            }

            if (parsed.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                modifiers |= ModShift;
            }

            if (parsed.Modifiers.HasFlag(ModifierKeys.Windows))
            {
                modifiers |= ModWin;
            }

            vk = (uint)KeyInterop.VirtualKeyFromKey(parsed.Key);
            return vk != 0;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        Unregister();
        _source?.RemoveHook(WndProc);
        _subscription?.Dispose();
    }
}
