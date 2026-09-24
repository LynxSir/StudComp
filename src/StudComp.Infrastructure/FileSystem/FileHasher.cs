using System.Security.Cryptography;
using StudComp.Core.Common;

namespace StudComp.Infrastructure.FileSystem;

/// <summary>
/// Хэш содержимого файла для обнаружения дублей (ARCHITECTURE §7.2, §8.4 п.5). Полный файл читается
/// только если он меньше порога; у больших берём первые <c>HeadBytes</c> и подмешиваем размер —
/// этого достаточно, чтобы не спутать два разных файла, и на порядок дешевле полного хэша.
/// Живёт в инфраструктуре, а не в Архивариусе: с Phase 12.3 тот же хэш считает импорт файлов в
/// учебную папку, и совпадать он обязан байт в байт, иначе разложенный файл не опознается дублем.
/// </summary>
public interface IFileHasher
{
    Task<string> ComputeAsync(string path, CancellationToken ct = default);
}

public sealed class FileHasher(IFileSystem fileSystem) : IFileHasher
{
    private const int HeadBytes = 256 * 1024;

    public async Task<string> ComputeAsync(string path, CancellationToken ct = default)
    {
        Guard.NotNullOrWhiteSpace(path);

        var size = fileSystem.GetFileSize(path);

        await using var stream = fileSystem.OpenRead(path);
        using var sha = SHA256.Create();

        if (size <= HeadBytes)
        {
            var full = await sha.ComputeHashAsync(stream, ct).ConfigureAwait(false);
            return Convert.ToHexStringLower(full);
        }

        var buffer = new byte[HeadBytes];
        var read = await stream.ReadAtLeastAsync(buffer, HeadBytes, throwOnEndOfStream: false, ct).ConfigureAwait(false);
        sha.TransformBlock(buffer, 0, read, null, 0);

        // Размер подмешивается в хэш — два файла с одинаковым началом, но разной длиной, различимы.
        var sizeBytes = BitConverter.GetBytes(size);
        sha.TransformFinalBlock(sizeBytes, 0, sizeBytes.Length);

        return Convert.ToHexStringLower(sha.Hash!);
    }
}
