using Velopack;

namespace StudComp;

/// <summary>
/// Явная точка входа (Phase 13). WPF генерирует свой <c>Main</c>, но Velopack требует, чтобы
/// <see cref="VelopackApp"/> отработал самым первым — до создания <see cref="App"/>, любого окна и
/// сборки хоста: на первом запуске после установки/обновления процесс получает служебные аргументы
/// (<c>--veloapp-install</c> и т.п.), обрабатывает их и завершается, не поднимая UI.
/// Подключается через <c>&lt;StartupObject&gt;</c> в <c>StudComp.App.csproj</c>.
/// </summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Хуки установки/обновления/удаления Velopack. На обычном запуске — быстрый no-op.
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
