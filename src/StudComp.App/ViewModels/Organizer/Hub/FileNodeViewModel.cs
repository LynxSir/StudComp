using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using StudComp.Infrastructure.FileSystem;

namespace StudComp.ViewModels.Organizer.Hub;

/// <summary>
/// Узел дерева подпапок предмета. Дети подгружаются лениво — при первом раскрытии, а не при
/// построении дерева: папка предмета может оказаться глубокой, и обходить её целиком незачем.
/// </summary>
public sealed partial class FileNodeViewModel : ObservableObject
{
    private readonly IFileSystem _fileSystem;
    private readonly Action<FileNodeViewModel>? _onSelected;
    private bool _childrenLoaded;

    public FileNodeViewModel(
        IFileSystem fileSystem, string path, string displayName, bool hasChildren = true,
        bool isRoot = false, Action<FileNodeViewModel>? onSelected = null)
    {
        _fileSystem = fileSystem;
        _onSelected = onSelected;
        Path = path;
        DisplayName = displayName;
        IsRoot = isRoot;

        // Фиктивный ребёнок нужен только затем, чтобы у узла появился шеврон раскрытия — но только
        // если в папке реально есть подпапки (new_addons.md §6.2, Тест.txt №16): раньше шеврон
        // рисовался у любого узла независимо от содержимого.
        if (hasChildren)
        {
            Children.Add(Placeholder);
        }
        else
        {
            _childrenLoaded = true;
        }
    }

    private static readonly FileNodeViewModel Placeholder = new();

    private FileNodeViewModel()
    {
        _fileSystem = null!;
        Path = string.Empty;
        DisplayName = "…";
        _childrenLoaded = true;
        IsPlaceholder = true;
    }

    public string Path { get; }

    public string DisplayName { get; }

    public bool IsRoot { get; }

    public bool IsPlaceholder { get; }

    public System.Collections.ObjectModel.ObservableCollection<FileNodeViewModel> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isSelected;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
        {
            _ = LoadChildrenAsync();
        }
    }

    /// <summary>
    /// <c>TreeView.SelectedItem</c> — read-only DP и не биндится напрямую; вместо этого каждый узел
    /// сам сообщает о своём выборе через колбэк, переданный при создании (new_addons.md §7.1: раньше
    /// это не было проведено вообще, и выбор подпапки в дереве никак не долетал до списка файлов).
    /// </summary>
    partial void OnIsSelectedChanged(bool value)
    {
        if (value)
        {
            _onSelected?.Invoke(this);
        }
    }

    /// <summary>Подгрузить подпапки узла. Повторные вызовы ничего не делают.</summary>
    public async Task LoadChildrenAsync()
    {
        if (_childrenLoaded)
        {
            return;
        }

        _childrenLoaded = true;

        var path = Path;
        var fileSystem = _fileSystem;

        // Перечисление каталога — дисковый ввод-вывод, в UI-потоке ему делать нечего. Заодно, пока
        // уже в фоновом потоке, дёшево проверяем каждую найденную подпапку на наличие своих
        // подпапок (короткое замыкание на первом элементе, не полный листинг) — чтобы шеврон
        // раскрытия рисовался только там, где действительно есть что раскрывать (§6.2).
        var directories = await Task.Run(() =>
        {
            try
            {
                if (!fileSystem.DirectoryExists(path))
                {
                    return [];
                }

                return fileSystem.EnumerateDirectories(path)
                    .OrderBy(System.IO.Path.GetFileName, StringComparer.CurrentCultureIgnoreCase)
                    .Select(dir => (Dir: dir, HasChildren: SafeHasSubdirectories(fileSystem, dir)))
                    .ToArray();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return [];
            }
        }).ConfigureAwait(true);

        Children.Clear();
        foreach (var (directory, hasChildren) in directories)
        {
            Children.Add(new FileNodeViewModel(
                _fileSystem, directory, System.IO.Path.GetFileName(directory), hasChildren,
                onSelected: _onSelected));
        }
    }

    private static bool SafeHasSubdirectories(IFileSystem fileSystem, string path)
    {
        try
        {
            return fileSystem.EnumerateDirectories(path).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Сбросить кеш детей — после импорта в папке могли появиться новые подпапки.</summary>
    public void Invalidate()
    {
        _childrenLoaded = false;
        Children.Clear();
        Children.Add(Placeholder);
        IsExpanded = false;
    }
}
