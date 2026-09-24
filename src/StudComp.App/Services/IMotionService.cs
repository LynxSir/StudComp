using System.ComponentModel;
using StudComp.Infrastructure.Settings;

namespace StudComp.Services;

/// <summary>
/// Применяет визуальные настройки раздела «Оформление» немедленно (new_addons.md §7 §2): акцентный
/// цвет, плотность интерфейса, масштаб, включение анимаций. Живёт в App (зависит от WPF), по образцу
/// <see cref="IThemeService"/>.
/// </summary>
public interface IMotionService : INotifyPropertyChanged
{
    /// <summary>Текущий множитель масштаба интерфейса — к нему привязан <c>LayoutTransform</c> окна.</summary>
    double FontScale { get; }

    /// <summary>Разрешены ли анимации сейчас.</summary>
    bool AnimationsEnabled { get; }

    /// <summary>Прочитать сохранённые значения и применить всё разом. Зовётся один раз при старте.</summary>
    void Initialize();

    /// <summary>Применить акцентный цвет (ключ пресета или <c>#RRGGBB</c>).</summary>
    void ApplyAccent(string accent);

    /// <summary>Применить плотность интерфейса.</summary>
    void ApplyDensity(AppDensity density);

    /// <summary>Применить масштаб интерфейса (зажимается в <c>[0.8, 1.4]</c>).</summary>
    void ApplyFontScale(double scale);

    /// <summary>Включить/выключить анимации (выключение делает переходы мгновенными).</summary>
    void ApplyAnimations(bool enabled);
}
