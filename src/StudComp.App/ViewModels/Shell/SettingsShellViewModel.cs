using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using StudComp.Services;
using StudComp.ViewModels.Settings;

namespace StudComp.ViewModels.Shell;

/// <summary>
/// ViewModel модалки настроек (new_addons.md §7): рельс из 10 разделов слева, View выбранного
/// раздела справа (смена вида с кроссфейдом), поле поиска по разделам. Каждый раздел — своя
/// <see cref="ISettingsSectionViewModel"/>; настройки применяются немедленно.
/// </summary>
public sealed partial class SettingsShellViewModel : ObservableObject
{
    private readonly IReadOnlyList<ISettingsSectionViewModel> _all;
    private readonly ILogger<SettingsShellViewModel> _logger;

    public SettingsShellViewModel(
        IEnumerable<ISettingsSectionViewModel> sections,
        ILogger<SettingsShellViewModel> logger)
    {
        _logger = logger;
        _all = sections.OrderBy(s => (int)s.Section).ToArray();
        Sections = new ObservableCollection<ISettingsSectionViewModel>(_all);
    }

    /// <summary>Разделы, видимые в рельсе сейчас (с учётом поиска).</summary>
    public ObservableCollection<ISettingsSectionViewModel> Sections { get; }

    [ObservableProperty]
    private bool _isOpen;

    [ObservableProperty]
    private ISettingsSectionViewModel? _currentSection;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    /// <summary>Ни один раздел не подходит под запрос.</summary>
    public bool NoSearchResults => Sections.Count == 0;

    /// <summary>Открыть модалку, опционально на конкретном разделе.</summary>
    public void Open(SettingsSection? section)
    {
        UiActivity.Mark("Настройки → открытие");
        IsOpen = true;
        SearchQuery = string.Empty;

        var target = (section is { } s ? _all.FirstOrDefault(x => x.Section == s) : null) ?? _all[0];
        if (ReferenceEquals(target, CurrentSection))
        {
            // Тот же раздел — событие смены не поднимется, активируем вручную.
            _ = ActivateAsync(target);
        }
        else
        {
            CurrentSection = target;
        }
    }

    [RelayCommand]
    private void Close() => IsOpen = false;

    async partial void OnCurrentSectionChanged(ISettingsSectionViewModel? value)
    {
        if (value is not null && IsOpen)
        {
            UiActivity.Mark($"Настройки → {value.Title}");
            await ActivateAsync(value);
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        var query = value?.Trim() ?? string.Empty;
        var visible = query.Length == 0
            ? _all
            : _all.Where(s => Matches(s, query)).ToArray();

        Sections.Clear();
        foreach (var section in visible)
        {
            Sections.Add(section);
        }

        OnPropertyChanged(nameof(NoSearchResults));

        if (Sections.Count > 0 && (CurrentSection is null || !Sections.Contains(CurrentSection)))
        {
            CurrentSection = Sections[0];
        }
    }

    private async Task ActivateAsync(ISettingsSectionViewModel section)
    {
        try
        {
            await section.OnActivatedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось обновить раздел настроек {Section}", section.Section);
        }
    }

    private static bool Matches(ISettingsSectionViewModel section, string query) =>
        section.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
        || section.SearchKeywords.Any(k => k.Contains(query, StringComparison.OrdinalIgnoreCase));
}
