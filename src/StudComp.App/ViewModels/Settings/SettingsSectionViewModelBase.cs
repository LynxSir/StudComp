using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>
/// Общая база разделов настроек: идентичность для рельса + guard немедленного применения.
/// </summary>
/// <remarks>
/// Гидрация значений из <c>IOptions</c> оборачивается в <c>using (BeginLoad())</c>, а каждый
/// сгенерированный <c>OnXxxChanged</c> начинается с <c>if (IsLoading) return;</c> — так сеттеры не
/// пишут в <c>usersettings.json</c> во время загрузки. Счётчик, а не флаг: вложенная/повторная
/// гидрация (в <see cref="OnActivatedAsync"/>) безопасна.
/// </remarks>
public abstract partial class SettingsSectionViewModelBase : ObservableObject, ISettingsSectionViewModel
{
    private int _loadDepth;

    /// <inheritdoc />
    public abstract SettingsSection Section { get; }

    /// <inheritdoc />
    public abstract string Title { get; }

    /// <inheritdoc />
    public abstract SymbolRegular Icon { get; }

    /// <inheritdoc />
    public virtual IEnumerable<string> SearchKeywords => [];

    /// <inheritdoc />
    public virtual Task OnActivatedAsync() => Task.CompletedTask;

    /// <summary>Идёт гидрация — сеттеры не должны писать в файл настроек.</summary>
    protected bool IsLoading => _loadDepth > 0;

    /// <summary>Открывает окно гидрации; <c>Dispose</c> его закрывает.</summary>
    protected IDisposable BeginLoad() => new LoadScope(this);

    private sealed class LoadScope : IDisposable
    {
        private readonly SettingsSectionViewModelBase _owner;
        private bool _disposed;

        public LoadScope(SettingsSectionViewModelBase owner)
        {
            _owner = owner;
            _owner._loadDepth++;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _owner._loadDepth--;
        }
    }
}
