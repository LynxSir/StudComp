namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Диагностика производительности. Секция конфигурации <c>Rubrica:Diagnostics</c>
/// (ARCHITECTURE §14 — подтверждение NFR измерениями, а не предположениями).
/// </summary>
/// <remarks>
/// По умолчанию всё выключено — это отладочный инструмент, а не постоянная нагрузка. Включается
/// на время замера через <c>usersettings.json</c> либо переменную окружения
/// <c>Rubrica__Diagnostics__PerformanceLoggingEnabled=true</c>.
/// </remarks>
public sealed class DiagnosticsOptions
{
    /// <summary>Имя секции в конфигурации.</summary>
    public const string SectionName = "Rubrica:Diagnostics";

    /// <summary>
    /// Писать ли в лог периодический снимок памяти/дескрипторов простаивающего процесса
    /// (рабочий набор, управляемая куча, число хендлов и потоков).
    /// </summary>
    public bool PerformanceLoggingEnabled { get; set; }

    /// <summary>Интервал между снимками, минуты. Значения ≤ 0 подтягиваются к 1.</summary>
    public int ProbeIntervalMinutes { get; set; } = 5;
}
