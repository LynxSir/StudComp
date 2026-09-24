namespace StudComp.ViewModels.ReportForge;

/// <summary>
/// Параметр навигации в раздел «Отчёты» (new_addons.md §6): точка входа «Сгенерировать отчёт
/// по предмету» из Хаба/Дашборда. <see langword="record"/> — чтобы навигация могла сравнить
/// параметр структурно (<c>NavigationService</c>), как <c>SubjectHubParameter</c>.
/// </summary>
/// <param name="SubjectId">Предмет для предзаполнения. <see langword="null"/> — обычный переход без предмета.</param>
public sealed record ReportForgeParameter(Guid? SubjectId);
