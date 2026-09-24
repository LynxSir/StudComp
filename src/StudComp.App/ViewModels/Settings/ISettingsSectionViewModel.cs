using StudComp.ViewModels.Shell;
using Wpf.Ui.Controls;

namespace StudComp.ViewModels.Settings;

/// <summary>
/// Один раздел модалки настроек (new_addons.md §7). Рельс слева перечисляет разделы, справа
/// показывается View выбранного (через <c>DataTemplate</c>). Все настройки применяются немедленно —
/// кнопки «Сохранить» нет.
/// </summary>
public interface ISettingsSectionViewModel
{
    /// <summary>Идентичность раздела — для рельса и дип-линков <c>OpenSettings(section)</c>.</summary>
    SettingsSection Section { get; }

    /// <summary>Подпись раздела в рельсе.</summary>
    string Title { get; }

    /// <summary>Иконка раздела в рельсе.</summary>
    SymbolRegular Icon { get; }

    /// <summary>
    /// Слова для поиска по настройкам: подписи строк и краткие описания. Пусто — раздел ищется
    /// только по заголовку.
    /// </summary>
    IEnumerable<string> SearchKeywords { get; }

    /// <summary>
    /// Вызывается при каждом открытии раздела (открытие модалки, клик по рельсу). По умолчанию —
    /// ничего; разделы с данными из БД (семестры) здесь перечитывают список.
    /// </summary>
    Task OnActivatedAsync();
}
