using System.IO;
using Microsoft.Extensions.Logging;
using StudComp.Core.Abstractions.Workspace;

namespace StudComp.Services;

/// <inheritdoc cref="IRecycleBinPort"/>
internal sealed class WindowsRecycleBin(ILogger<WindowsRecycleBin> logger) : IRecycleBinPort
{
    public bool TrySendToRecycleBin(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        // На томах без «Корзины» (сетевые диски, часть флешек) RecycleOption.SendToRecycleBin молча
        // удаляет файл НАВСЕГДА. Поэтому том проверяется заранее: отказ означает «файл остался».
        if (!HasRecycleBin(path))
        {
            logger.LogWarning("Том не поддерживает «Корзину», файл оставлен на месте: {Path}", path);
            return false;
        }

        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(
                path,
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or OperationCanceledException or ArgumentException)
        {
            logger.LogWarning(exception, "Не удалось отправить файл в «Корзину»: {Path}", path);
            return false;
        }
    }

    public bool TryRemoveEmptyDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            // recursive: false — непустой каталог бросит исключение, и данные останутся целы.
            Directory.Delete(path, recursive: false);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool HasRecycleBin(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException
            or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }
}
