namespace StudComp.Core.Abstractions.Archivist;

/// <summary>
/// Определяет, дописан ли файл, чтобы архивариус не хватал недокачанный или ещё копирующийся файл
/// (ARCHITECTURE §8.3, §8.4 п.2).
/// </summary>
public interface IFileStabilityChecker
{
    /// <summary>
    /// Ждёт, пока размер файла перестанет меняться между замерами, а сам файл не окажется незалоченным.
    /// </summary>
    /// <returns><see langword="true"/>, если файл стабилизировался за <paramref name="timeout"/>; иначе <see langword="false"/>.</returns>
    Task<bool> WaitUntilStableAsync(string path, TimeSpan timeout, CancellationToken ct);
}
