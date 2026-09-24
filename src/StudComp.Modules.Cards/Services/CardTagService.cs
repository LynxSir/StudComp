using System.Globalization;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;

namespace StudComp.Modules.Cards.Services;

/// <summary>
/// Метки карточек (new_addons.md §3.1): нормализация имён, «найти или создать», слияние и счётчики.
/// </summary>
public interface ICardTagService
{
    Task<IReadOnlyList<CardTag>> GetAllAsync(CancellationToken ct = default);

    /// <summary>Самые частые метки — облако меток и автодополнение в форме карточки.</summary>
    Task<IReadOnlyList<CardTag>> GetTopAsync(int take, CancellationToken ct = default);

    /// <summary>
    /// Найти метку по имени, а если её нет — завести. Имя нормализуется, поэтому «#Формулы»,
    /// «формулы» и «ФОРМУЛЫ» — одна и та же метка.
    /// </summary>
    Task<Result<CardTag>> EnsureAsync(string name, CancellationToken ct = default);

    /// <summary>То же пачкой: пустые и повторяющиеся имена отсеиваются молча.</summary>
    Task<IReadOnlyList<CardTag>> EnsureManyAsync(
        IReadOnlyList<string> names,
        CancellationToken ct = default);

    /// <summary>Метки карточек пачкой — список библиотеки не должен делать запрос на строку.</summary>
    Task<IReadOnlyDictionary<Guid, IReadOnlyList<CardTag>>> GetForCardsAsync(
        IReadOnlyList<Guid> cardIds,
        CancellationToken ct = default);

    /// <summary>Переименовать метку. Меняется и отображаемое имя, и нормализованное.</summary>
    Task<Result> RenameAsync(Guid id, string displayName, CancellationToken ct = default);

    /// <summary>
    /// Слить метку в другую: все карточки первой получают вторую, первая удаляется.
    /// Обычное дело, когда одно и то же назвали двумя словами.
    /// </summary>
    Task<Result> MergeAsync(Guid fromId, Guid toId, CancellationToken ct = default);

    /// <summary>Удалить метку. Карточки при этом остаются — исчезают только связки.</summary>
    Task<Result> DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>Пересчитать счётчики использований по фактическим связкам.</summary>
    Task<int> RecalculateUsageAsync(CancellationToken ct = default);

    /// <summary>
    /// Нормализованное имя метки: без решётки, в нижнем регистре, с «ё» как «е». Публичный —
    /// им же пользуется разбор строки поиска, и оба обязаны давать одинаковый результат.
    /// </summary>
    static string Normalize(string? name)
    {
        var trimmed = (name ?? string.Empty).Trim().TrimStart('#').Trim();
        return trimmed.ToLower(CultureInfo.CurrentCulture).Replace('ё', 'е');
    }
}

internal sealed class CardTagService(ICardTagRepository tags) : ICardTagService
{
    private const int MaxNameLength = 100;

    public Task<IReadOnlyList<CardTag>> GetAllAsync(CancellationToken ct = default) =>
        tags.GetAllAsync(ct);

    public Task<IReadOnlyList<CardTag>> GetTopAsync(int take, CancellationToken ct = default) =>
        tags.GetTopAsync(take <= 0 ? 1 : take, ct);

    public async Task<Result<CardTag>> EnsureAsync(string name, CancellationToken ct = default)
    {
        var normalized = ICardTagService.Normalize(name);
        if (normalized.Length == 0)
        {
            return Result<CardTag>.Failure("cards.tag_name_required", "Название метки не может быть пустым.");
        }

        if (normalized.Length > MaxNameLength)
        {
            normalized = normalized[..MaxNameLength];
        }

        var existing = await tags.GetByNameAsync(normalized, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return Result<CardTag>.Success(existing);
        }

        var display = (name ?? string.Empty).Trim().TrimStart('#').Trim();
        var tag = new CardTag
        {
            Id = Guid.NewGuid(),
            Name = normalized,
            DisplayName = display.Length > MaxNameLength ? display[..MaxNameLength] : display,
            CreatedAt = DateTimeOffset.Now,
        };

        await tags.AddAsync(tag, ct).ConfigureAwait(false);
        return Result<CardTag>.Success(tag);
    }

    public async Task<IReadOnlyList<CardTag>> EnsureManyAsync(
        IReadOnlyList<string> names,
        CancellationToken ct = default)
    {
        var result = new List<CardTag>(names.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var name in names)
        {
            var normalized = ICardTagService.Normalize(name);
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            var ensured = await EnsureAsync(name, ct).ConfigureAwait(false);
            if (ensured.IsSuccess)
            {
                result.Add(ensured.Value);
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<Guid, IReadOnlyList<CardTag>>> GetForCardsAsync(
        IReadOnlyList<Guid> cardIds,
        CancellationToken ct = default)
    {
        if (cardIds.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<CardTag>>();
        }

        var links = await tags.GetLinksAsync(cardIds, ct).ConfigureAwait(false);
        if (links.Count == 0)
        {
            return new Dictionary<Guid, IReadOnlyList<CardTag>>();
        }

        var all = await tags.GetAllAsync(ct).ConfigureAwait(false);
        var byId = all.ToDictionary(x => x.Id);

        return links
            .GroupBy(x => x.CardId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CardTag>)group
                    .Select(link => byId.GetValueOrDefault(link.TagId))
                    .OfType<CardTag>()
                    .OrderBy(tag => tag.Name, StringComparer.CurrentCulture)
                    .ToList());
    }

    public async Task<Result> RenameAsync(Guid id, string displayName, CancellationToken ct = default)
    {
        var tag = await tags.GetByIdAsync(id, ct).ConfigureAwait(false);
        if (tag is null)
        {
            return Result.Failure("cards.tag_not_found", "Метка не найдена.");
        }

        var normalized = ICardTagService.Normalize(displayName);
        if (normalized.Length == 0)
        {
            return Result.Failure("cards.tag_name_required", "Название метки не может быть пустым.");
        }

        var clash = await tags.GetByNameAsync(normalized, ct).ConfigureAwait(false);
        if (clash is not null && clash.Id != id)
        {
            return Result.Failure(
                "cards.tag_already_exists",
                $"Метка «{normalized}» уже есть. Слейте метки, если это одно и то же.");
        }

        tag.Name = normalized;
        tag.DisplayName = displayName.Trim().TrimStart('#').Trim();
        await tags.UpdateAsync(tag, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> MergeAsync(Guid fromId, Guid toId, CancellationToken ct = default)
    {
        if (fromId == toId)
        {
            return Result.Failure("cards.tag_merge_into_itself", "Метку нельзя слить саму в себя.");
        }

        if (await tags.GetByIdAsync(fromId, ct).ConfigureAwait(false) is null
            || await tags.GetByIdAsync(toId, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("cards.tag_not_found", "Метка не найдена.");
        }

        await tags.MoveLinksAsync(fromId, toId, ct).ConfigureAwait(false);
        await tags.DeleteAsync(fromId, ct).ConfigureAwait(false);
        await tags.RecalculateUsageAsync(ct).ConfigureAwait(false);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await tags.GetByIdAsync(id, ct).ConfigureAwait(false) is null)
        {
            return Result.Failure("cards.tag_not_found", "Метка не найдена.");
        }

        await tags.DeleteAsync(id, ct).ConfigureAwait(false);
        return Result.Success();
    }

    public Task<int> RecalculateUsageAsync(CancellationToken ct = default) =>
        tags.RecalculateUsageAsync(ct);
}
