namespace StudComp.Infrastructure.FileSystem;

/// <inheritdoc cref="IFileSystem"/>
/// <remarks>Прямые вызовы <see cref="System.IO"/> без какой-либо логики поверх.</remarks>
internal sealed class SystemFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    public Stream OpenRead(string path) => File.OpenRead(path);

    public FileStream Open(string path, FileMode mode, FileAccess access, FileShare share) =>
        new(path, mode, access, share);

    public void Move(string sourcePath, string destinationPath, bool overwrite = false) =>
        File.Move(sourcePath, destinationPath, overwrite);

    public void Copy(string sourcePath, string destinationPath, bool overwrite = false) =>
        File.Copy(sourcePath, destinationPath, overwrite);

    public long GetFileSize(string path) => new FileInfo(path).Length;

    public DateTimeOffset GetLastWriteTimeUtc(string path) => File.GetLastWriteTimeUtc(path);

    public DateTimeOffset GetCreationTimeUtc(string path) => File.GetCreationTimeUtc(path);

    public FileAttributes GetAttributes(string path) => File.GetAttributes(path);

    public IEnumerable<string> EnumerateFiles(string directoryPath, string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
        Directory.EnumerateFiles(directoryPath, searchPattern, searchOption);

    public IEnumerable<string> EnumerateDirectories(string directoryPath, string searchPattern = "*",
        SearchOption searchOption = SearchOption.TopDirectoryOnly) =>
        Directory.EnumerateDirectories(directoryPath, searchPattern, searchOption);
}
