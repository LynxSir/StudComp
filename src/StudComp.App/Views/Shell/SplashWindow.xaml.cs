using System.Windows;

namespace StudComp.Views.Shell;

/// <summary>
/// Экран-заставка на время старта хоста и миграции БД (ARCHITECTURE §14: не оставлять пользователя
/// перед замершим экраном). Без ViewModel — живёт только пока идёт инициализация.
/// </summary>
public partial class SplashWindow : Window
{
    public SplashWindow() => InitializeComponent();
}
