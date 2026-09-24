using StudComp.Infrastructure.FileSystem;

namespace StudComp.Infrastructure.Tests;

/// <summary>Базовые операции <see cref="SystemFileSystem"/> на реальном временном каталоге.</summary>
public sealed class SystemFileSystemTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "rubrica-fs-" + Guid.NewGuid().ToString("N"));
    private readonly IFileSystem _fs = new SystemFileSystem();

    public SystemFileSystemTests() => Directory.CreateDirectory(_tempDir);

    [Fact]
    public void CreateDirectory_and_FileExists_reflect_reality()
    {
        var nested = Path.Combine(_tempDir, "a", "b");
        _fs.CreateDirectory(nested);
        Assert.True(_fs.DirectoryExists(nested));

        var file = Path.Combine(nested, "note.txt");
        Assert.False(_fs.FileExists(file));
        File.WriteAllText(file, "hi");
        Assert.True(_fs.FileExists(file));
    }

    [Fact]
    public void GetFileSize_returns_byte_count()
    {
        var file = Path.Combine(_tempDir, "data.bin");
        File.WriteAllBytes(file, new byte[123]);

        Assert.Equal(123, _fs.GetFileSize(file));
    }

    [Fact]
    public void Move_relocates_file_without_overwriting_by_default()
    {
        var source = Path.Combine(_tempDir, "src.txt");
        var target = Path.Combine(_tempDir, "dst.txt");
        File.WriteAllText(source, "payload");

        _fs.Move(source, target);

        Assert.False(_fs.FileExists(source));
        Assert.Equal("payload", File.ReadAllText(target));

        File.WriteAllText(source, "other");
        Assert.Throws<IOException>(() => _fs.Move(source, target));
    }

    [Fact]
    public void EnumerateFiles_lists_top_level_entries()
    {
        File.WriteAllText(Path.Combine(_tempDir, "1.txt"), "");
        File.WriteAllText(Path.Combine(_tempDir, "2.txt"), "");

        var found = _fs.EnumerateFiles(_tempDir, "*.txt").ToArray();

        Assert.Equal(2, found.Length);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
