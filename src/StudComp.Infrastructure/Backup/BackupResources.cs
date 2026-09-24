using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using StudComp.Core.Abstractions.Data;
using StudComp.Core.Domain;
using StudComp.Infrastructure.Settings;

namespace StudComp.Infrastructure.Backup;

public sealed partial class BackupService
{
    private async Task<List<BackupResource>> StageResourcesAsync(string snapshot, string staging,
        string destination, string temporary, CancellationToken ct)
    {
        var resources = new List<BackupResource>();
        var root = _settings.Get<WorkspaceOptions>(WorkspaceOptions.SectionName).StudyRootPath;
        if (!string.IsNullOrWhiteSpace(root))
        {
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Учебная папка недоступна: {root}");
            resources.Add(new BackupResource(Path.GetFullPath(root), "workspace", true));
        }
        var paths = (await _database.GetResourcePathsAsync(snapshot, ct, root).ConfigureAwait(false))
            .Append(_settings.Get<ArchivistOptions>(ArchivistOptions.SectionName).ArchiveRootFolder)
            .Append(_settings.Get<ReportForgeOptions>(ReportForgeOptions.SectionName).OutputFolder)
            .Where(x => !string.IsNullOrWhiteSpace(x) && Path.IsPathRooted(x))
            .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Length);
        var fileParents = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (resources.Any(r => Covers(r, path))) continue;
            var directory = Directory.Exists(path);
            if (!directory && !File.Exists(path)) continue; // Deleted historical files are not attachments.
            var bucket = $"workspace/_Внешние/Папка_{resources.Count:D4}";
            if (!directory)
            {
                var parent = Path.GetDirectoryName(path)!;
                if (!fileParents.TryGetValue(parent, out bucket))
                    fileParents[parent] = bucket = $"workspace/_Внешние/Файлы_{resources.Count:D4}";
            }
            var archivePath = $"{bucket}/{Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar))}";
            resources.Add(new BackupResource(path, archivePath, directory));
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { Path.GetFullPath(destination), Path.GetFullPath(temporary), Path.GetFullPath(snapshot),
            Path.GetFullPath(_layout.DatabaseFilePath), Path.GetFullPath(_layout.SettingsFilePath) };
        foreach (var resource in resources)
        {
            ct.ThrowIfCancellationRequested();
            if (resource.IsDirectory)
            {
                Directory.CreateDirectory(SafeChild(staging, resource.ArchivePath));
                CopyDirectory(resource.SourcePath, SafeChild(staging, resource.ArchivePath));
            }
            else CopyFile(resource.SourcePath, SafeChild(staging, resource.ArchivePath));
        }
        return resources;

        void CopyFile(string source, string target)
        {
            ct.ThrowIfCancellationRequested();
            if (excluded.Contains(Path.GetFullPath(source))) return;
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Файл является ссылкой: {source}. Поместите сам файл в учебную папку.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: false);
        }

        void CopyDirectory(string source, string target)
        {
            ct.ThrowIfCancellationRequested();
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"Папка является ссылкой: {source}. Выберите её фактическое расположение.");
            Directory.CreateDirectory(target);
            foreach (var file in Directory.EnumerateFiles(source)) CopyFile(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (var directory in Directory.EnumerateDirectories(source))
            {
                // A broad study root must never recursively include backup staging.
                if (Path.GetFullPath(staging).StartsWith(Path.GetFullPath(directory).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFullPath(directory).Equals(staging, StringComparison.OrdinalIgnoreCase)) continue;
                CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
            }
        }
    }

    private static bool Covers(BackupResource resource, string path) =>
        path.Equals(resource.SourcePath, StringComparison.OrdinalIgnoreCase)
        || resource.IsDirectory && path.StartsWith(resource.SourcePath.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string SafeChild(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':')) throw new InvalidDataException("Абсолютный путь в архиве.");
        var segments = relative.Replace('\\', '/').Split('/');
        if (segments.Any(x => x is "." or ".." || x.EndsWith(' ') || x.EndsWith('.')))
            throw new InvalidDataException("Недопустимый путь в архиве.");
        var full = Path.GetFullPath(Path.Combine(root, relative));
        if (!full.StartsWith(Path.GetFullPath(root).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Путь выходит за пределы папки восстановления.");
        return full;
    }

    private static void ExtractResources(ZipArchive archive, string staging, BackupManifest? manifest, CancellationToken ct)
    {
        if (manifest is { SchemaVersion: > BackupManifest.CurrentSchemaVersion })
            throw new InvalidDataException("Эта резервная копия создана более новой версией приложения.");
        if (manifest?.SchemaVersion < 3 || manifest is null) return;
        if (manifest.Resources is null || manifest.FileHashes is null || manifest.Files is null)
            throw new InvalidDataException("В архиве нет списка файлов или контрольных сумм.");

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in manifest.Files)
        {
            ct.ThrowIfCancellationRequested();
            if (!names.Add(name)) throw new InvalidDataException("Повторяющийся путь в архиве.");
            if (name != BackupManifest.DatabaseEntryName && name != BackupManifest.SettingsEntryName && !name.StartsWith("workspace/", StringComparison.Ordinal))
                throw new InvalidDataException("Неизвестный файл в архиве.");
            var target = SafeChild(staging, name);
            var entries = archive.Entries.Where(x => x.FullName == name).ToArray();
            if (entries.Length != 1 || !manifest.FileHashes.TryGetValue(name, out var hash))
                throw new InvalidDataException($"В архиве отсутствует файл или его контрольная сумма: {name}");
            if (name.StartsWith("workspace/", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                entries[0].ExtractToFile(target, overwrite: false);
            }
            if (!File.Exists(target) || !Sha256(target).Equals(hash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Повреждён файл: {name}");
        }
        foreach (var resource in manifest.Resources)
        {
            if (!Path.IsPathRooted(resource.SourcePath)
                || (resource.ArchivePath != "workspace" && !resource.ArchivePath.StartsWith("workspace/", StringComparison.Ordinal)))
                throw new InvalidDataException("Недопустимое расположение вложений.");
            var target = SafeChild(staging, resource.ArchivePath);
            if (resource.IsDirectory) Directory.CreateDirectory(target);
            else if (!File.Exists(target)) throw new InvalidDataException("Отсутствует вложение.");
        }
        if (!names.Contains(BackupManifest.DatabaseEntryName)
            || archive.GetEntry(BackupManifest.SettingsEntryName) is not null && !names.Contains(BackupManifest.SettingsEntryName))
            throw new InvalidDataException("Неполный список файлов резервной копии.");
    }

    private string PrepareRestoredSettings(string staging, BackupManifest? manifest, string? restoredRoot,
        IReadOnlyList<BackupPathMapping> mappings)
    {
        var path = Path.Combine(staging, BackupManifest.SettingsEntryName);
        JsonObject document;
        if (File.Exists(path)) document = JsonNode.Parse(File.ReadAllText(path)) as JsonObject
            ?? throw new InvalidDataException("Некорректные настройки в архиве.");
        else
        {
            try { document = JsonNode.Parse(File.ReadAllText(_layout.SettingsFilePath)) as JsonObject ?? new(); }
            catch (Exception ex) when (ex is IOException or JsonException) { document = new(); }
        }
        var rubrica = document["Rubrica"] as JsonObject ?? new JsonObject();
        document["Rubrica"] = rubrica;
        JsonObject Section(string name)
        {
            var section = rubrica[name] as JsonObject ?? new JsonObject();
            rubrica[name] = section;
            return section;
        }
        var localRoot = _settings.Get<WorkspaceOptions>(WorkspaceOptions.SectionName).StudyRootPath;
        Section("Workspace")["StudyRootPath"] = restoredRoot ?? (Directory.Exists(localRoot) ? localRoot : string.Empty);
        var localArchivist = _settings.Get<ArchivistOptions>(ArchivistOptions.SectionName);
        var archivist = Section("Archivist");
        archivist["Enabled"] = false;
        archivist["WatchedFolders"] = JsonSerializer.SerializeToNode(localArchivist.WatchedFolders.Where(Directory.Exists).ToArray());
        var oldArchive = archivist["ArchiveRootFolder"]?.GetValue<string>() ?? string.Empty;
        var newArchive = BackupPathMapping.Remap(oldArchive, mappings);
        archivist["ArchiveRootFolder"] = newArchive != oldArchive ? newArchive : string.Empty;
        var report = Section("ReportForge");
        var oldOutput = report["OutputFolder"]?.GetValue<string>() ?? string.Empty;
        var newOutput = BackupPathMapping.Remap(oldOutput, mappings);
        report["OutputFolder"] = newOutput != oldOutput ? newOutput : string.Empty;
        if (manifest is not null)
        {
            var data = Section("Data");
            data["BackupLineageId"] = manifest.LineageId == Guid.Empty ? Guid.NewGuid() : manifest.LineageId;
            data["BackupVersion"] = manifest.Version;
        }
        File.WriteAllText(path, document.ToJsonString(JsonOptions));
        return path;
    }

    private string? RestoreResourceFiles(string staging, BackupManifest? manifest, List<BackupPathMapping> mappings,
        CancellationToken ct, string? studyParentDirectory)
    {
        if (manifest?.Resources is not { Count: > 0 } resources) return null;
        var local = _settings.Get<WorkspaceOptions>(WorkspaceOptions.SectionName).StudyRootPath;
        var parent = !string.IsNullOrWhiteSpace(studyParentDirectory) ? Path.GetFullPath(studyParentDirectory)
            : Directory.Exists(local) ? local : Path.Combine(Path.GetDirectoryName(_layout.SettingsFilePath)!, "Учебные файлы");
        var root = Path.Combine(parent, $"Восстановлено_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid():N}");
        foreach (var resource in resources)
        {
            var target = resource.ArchivePath == "workspace" ? root : SafeChild(root, resource.ArchivePath["workspace/".Length..]);
            mappings.Add(new BackupPathMapping(resource.SourcePath, target, resource.IsDirectory));
        }
        var sourceRoot = Path.Combine(staging, "workspace");
        Directory.CreateDirectory(root);
        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(SafeChild(root, Path.GetRelativePath(sourceRoot, directory)));
        foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            var relative = Path.GetRelativePath(staging, file).Replace('\\', '/');
            var target = SafeChild(root, Path.GetRelativePath(sourceRoot, file));
            if (Path.GetExtension(file).Equals(".md", StringComparison.OrdinalIgnoreCase))
            {
                var resource = resources.OrderByDescending(x => x.ArchivePath.Length).FirstOrDefault(x =>
                    relative == x.ArchivePath || x.IsDirectory && relative.StartsWith(x.ArchivePath + "/", StringComparison.Ordinal));
                var original = resource is null ? null : resource.IsDirectory
                    ? Path.Combine(resource.SourcePath, relative[(resource.ArchivePath.Length + 1)..]) : resource.SourcePath;
                var originalMarkdown = File.ReadAllText(file);
                var markdown = originalMarkdown;
                if (original is not null)
                    markdown = MarkdownLocalImages.Rewrite(markdown, image =>
                    {
                        var absolute = Path.IsPathRooted(image) ? image : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(original)!, image));
                        var mapped = BackupPathMapping.Remap(absolute, mappings);
                        return Path.GetRelativePath(Path.GetDirectoryName(target)!, mapped);
                    });
                if (markdown == originalMarkdown) File.Copy(file, target, overwrite: false);
                else File.WriteAllText(target, markdown);
            }
            else File.Copy(file, target, overwrite: false);
        }
        return root;
    }
}
