using System.Text.Json;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.ReportForge.Services;

/// <summary>
/// Шаблоны оформления отчётов: заводской сид, список для UI и операции редактора профилей —
/// клонирование, сохранение формы, удаление (ARCHITECTURE §10.4, Phase 9).
/// </summary>
public interface IReportTemplateService
{
    /// <summary>
    /// Возвращает заводской шаблон ГОСТ 7.32-2017, создавая его при первом обращении.
    /// Нужен всегда: <c>ReportJob.TemplateId</c> — обязательный внешний ключ (ARCHITECTURE §7.2).
    /// </summary>
    Task<ReportTemplate> EnsureDefaultTemplateAsync(CancellationToken ct = default);

    /// <summary>Все шаблоны; заводской гарантированно есть в списке.</summary>
    Task<IReadOnlyList<ReportTemplate>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Разобранный профиль шаблона — для загрузки в форму редактора.</summary>
    Task<GostStyleProfile> GetProfileAsync(Guid templateId, CancellationToken ct = default);

    /// <summary>
    /// Создаёт новый шаблон — копию профиля источника (<paramref name="sourceTemplateId"/> = <see langword="null"/> —
    /// встроенного заводского). Клон всегда получает вариант <c>custom</c>: заводской вариант зарезервирован
    /// за сидируемой строкой.
    /// </summary>
    Task<ReportTemplate> CloneAsync(Guid? sourceTemplateId, string newName, CancellationToken ct = default);

    /// <summary>Сохраняет имя и профиль формы в существующий шаблон.</summary>
    Task SaveAsync(Guid templateId, string name, GostStyleProfile profile, CancellationToken ct = default);

    /// <summary>
    /// Удаляет пользовательский шаблон. Заводской (<see cref="DefaultGostVariant"/>) и шаблон, на который
    /// уже ссылаются отчёты, не удаляются — метод возвращает <see langword="false"/> без изменений.
    /// </summary>
    Task<bool> DeleteAsync(Guid templateId, CancellationToken ct = default);

    /// <summary>Ссылается ли на шаблон хотя бы одна задача генерации.</summary>
    Task<bool> IsInUseAsync(Guid templateId, CancellationToken ct = default);
}

internal sealed class ReportTemplateService(
    IReportTemplateRepository templates,
    IReportJobRepository jobs,
    IGostStyleProfileProvider profiles) : IReportTemplateService
{
    /// <summary>Обозначение варианта ГОСТ у заводского шаблона — по нему он и находится повторно.</summary>
    internal const string DefaultGostVariant = "7.32-2017";

    internal const string DefaultTemplateName = "ГОСТ 7.32-2017 (по умолчанию)";

    /// <summary>Вариант, который получают все клоны: не пересекается с заводским сидом.</summary>
    internal const string CustomGostVariant = "custom";

    /// <summary>Сид идемпотентен, но параллельные генерации не должны создать два шаблона.</summary>
    private readonly SemaphoreSlim _seedGate = new(1, 1);

    public async Task<ReportTemplate> EnsureDefaultTemplateAsync(CancellationToken ct = default)
    {
        var existing = await templates.GetByGostVariantAsync(DefaultGostVariant, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing;
        }

        await _seedGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            existing = await templates.GetByGostVariantAsync(DefaultGostVariant, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return existing;
            }

            var template = new ReportTemplate
            {
                Id = Guid.NewGuid(),
                Name = DefaultTemplateName,
                GostVariant = DefaultGostVariant,
                StyleProfileJson = profiles.GetDefaultJson(),
            };

            await templates.AddAsync(template, ct).ConfigureAwait(false);
            return template;
        }
        finally
        {
            _seedGate.Release();
        }
    }

    public async Task<IReadOnlyList<ReportTemplate>> GetAllAsync(CancellationToken ct = default)
    {
        await EnsureDefaultTemplateAsync(ct).ConfigureAwait(false);
        return await templates.GetAllAsync(ct).ConfigureAwait(false);
    }

    public Task<GostStyleProfile> GetProfileAsync(Guid templateId, CancellationToken ct = default) =>
        profiles.GetForTemplateAsync(templateId, ct);

    public async Task<ReportTemplate> CloneAsync(Guid? sourceTemplateId, string newName, CancellationToken ct = default)
    {
        var sourceJson = profiles.GetDefaultJson();

        if (sourceTemplateId is { } id)
        {
            var source = await templates.GetByIdAsync(id, ct).ConfigureAwait(false);
            if (source is not null && !string.IsNullOrWhiteSpace(source.StyleProfileJson))
            {
                sourceJson = source.StyleProfileJson;
            }
        }

        var clone = new ReportTemplate
        {
            Id = Guid.NewGuid(),
            Name = string.IsNullOrWhiteSpace(newName) ? "Новый профиль" : newName.Trim(),
            GostVariant = CustomGostVariant,
            StyleProfileJson = sourceJson,
        };

        await templates.AddAsync(clone, ct).ConfigureAwait(false);
        return clone;
    }

    public async Task SaveAsync(Guid templateId, string name, GostStyleProfile profile, CancellationToken ct = default)
    {
        var template = await templates.GetByIdAsync(templateId, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Шаблон {templateId} не найден.");

        template.Name = string.IsNullOrWhiteSpace(name) ? template.Name : name.Trim();
        template.StyleProfileJson = JsonSerializer.Serialize(profile, GostStyleProfileProvider.SerializerOptions);

        await templates.UpdateAsync(template, ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(Guid templateId, CancellationToken ct = default)
    {
        var template = await templates.GetByIdAsync(templateId, ct).ConfigureAwait(false);
        if (template is null || template.GostVariant == DefaultGostVariant)
        {
            return false;
        }

        if (await IsInUseAsync(templateId, ct).ConfigureAwait(false))
        {
            return false;
        }

        await templates.DeleteAsync(templateId, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<bool> IsInUseAsync(Guid templateId, CancellationToken ct = default) =>
        await jobs.CountByTemplateAsync(templateId, ct).ConfigureAwait(false) > 0;
}
