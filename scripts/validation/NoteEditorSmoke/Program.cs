using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;
using StudComp.Controls;
using StudComp.Core.Abstractions.ReportForge;
using StudComp.Core.Abstractions.Workspace;
using StudComp.Core.Common;
using StudComp.Core.Domain;
using StudComp.Data.Repositories;
using StudComp.Infrastructure.FileSystem;
using StudComp.Infrastructure.Notifications;
using StudComp.Modules.Organizer.Services;

// Сквозная проверка настоящих команд WPF без хоста, рабочей БД и наблюдателей папок.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        try { Run(); }
        catch (Exception error) { Console.Error.WriteLine(error); Environment.Exit(1); }
    }

    private static void Run()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Assembly.Load("Wpf.Ui");
        Assembly.Load("Rubrica");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var resourceRoot = XDocument.Load("src/StudComp.App/App.xaml").Descendants(presentation + "ResourceDictionary").First();
        var resources = new XElement(resourceRoot);
        resources.SetAttributeValue(XNamespace.Xmlns + "ui", "http://schemas.lepo.co/wpfui/2022/xaml");
        foreach (var source in resources.Descendants().Attributes("Source"))
            source.Value = "pack://application:,,,/Rubrica;component/" + source.Value;
        app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString());
        Console.WriteLine("WPF resources loaded");
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        var root = Path.Combine(Path.GetTempPath(), "rubrica-note-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var originalClipboard = Clipboard.GetDataObject();
        Window? window = null;
        try
        {
            var builder = (IMarkdownDocumentModelBuilder)CreateInternal(
                typeof(StudComp.Modules.ReportForge.DependencyInjection.ServiceCollectionExtensions).Assembly,
                "StudComp.Modules.ReportForge.Services.MarkdownDocumentModelBuilder");
            var note = new Note { Id = Guid.NewGuid(), Title = "Проверка", ContentMarkdown = "до заменить после" };
            var storage = new NoteStorage(root, "заметка.md", Path.Combine(root, "Предмет с пробелами", "Рисунки"));
            Task<NoteStorage?>? pendingStorage = null;
            var notes = Proxy.Create<INoteService>((method, args) => method.Name switch
            {
                "GetByIdAsync" => Task.FromResult<Note?>(note),
                "GetStorageAsync" => pendingStorage ?? Task.FromResult<NoteStorage?>(storage),
                "ExportMarkdownAsync" => Task.FromResult(Result<string>.Success(storage.ExportPath)),
                "UpdateContentAsync" => Task.FromResult(Result.Success()),
                _ => Proxy.Default(method.ReturnType),
            });
            var workspace = Proxy.Create<IStudyWorkspace>((method, args) => method.Name switch
            {
                "get_HasStudyRoot" => true,
                "get_StudyRootPath" => root,
                "ResolveRelative" => Path.GetRelativePath(root, (string)args![0]!),
                _ => Proxy.Default(method.ReturnType),
            });
            var errors = new List<string>();
            var toasts = Proxy.Create<IToastService>((method, args) => { errors.Add(string.Join(" ", args!)); return null; });
            var fileSystem = (IFileSystem)CreateInternal(typeof(IFileSystem).Assembly,
                "StudComp.Infrastructure.FileSystem.SystemFileSystem");
            using var vm = new NoteEditorViewModel(notes, builder,
                Proxy.Create<IActivityRepository>(), null!, workspace, null!, fileSystem, toasts,
                Proxy.Create<INoteImageTrash>(), null!, null!);
            Pump(vm.LoadAsync(note.Id));
            Console.WriteLine("Note loaded");
            vm.IsPreview = false;
            var editor = new NoteEditor { DataContext = vm };
            window = new Window { Content = editor, Width = 800, Height = 620, Left = -30000, Top = -30000,
                ShowInTaskbar = false, ShowActivated = false };
            window.Show();
            Console.WriteLine("Editor created");
            var textBox = (TextBox)editor.FindName("ContentTextBox");
            window.UpdateLayout();
            var bitmap = BitmapSource.Create(2, 1, 96, 96, PixelFormats.Bgra32, null,
                new byte[] { 0, 0, 255, 128, 0, 255, 0, 255 }, 8);
            bitmap.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var png = new MemoryStream();
            encoder.Save(png);

            // PNG с прозрачностью, реальная команда «Вставить», замена выделения и отмена.
            var pngData = new DataObject();
            pngData.SetData("PNG", new MemoryStream(png.ToArray()));
            Clipboard.SetDataObject(pngData);
            Console.WriteLine("PNG in clipboard");
            textBox.Select(3, 8);
            Console.WriteLine("Selection ready");
            Check(ApplicationCommands.Paste.CanExecute(null, textBox), "Вставка PNG недоступна");
            Console.WriteLine("Paste enabled");
            textBox.ContextMenu.PlacementTarget = textBox;
            textBox.ContextMenu.IsOpen = true;
            var pasteMenu = textBox.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Вставить"));
            Until(() => ReferenceEquals(pasteMenu.CommandTarget, textBox));
            ApplicationCommands.Paste.Execute(null, pasteMenu.CommandTarget);
            textBox.ContextMenu.IsOpen = false;
            Console.WriteLine("Paste command sent");
            try { Until(() => MarkdownLocalImages.Tokens(vm.Content).Count == 1 && !textBox.IsReadOnly); }
            catch
            {
                Console.Error.WriteLine($"Content: {vm.Content}; readOnly: {textBox.IsReadOnly}; errors: {string.Join(" / ", errors)}");
                throw;
            }
            Console.WriteLine("PNG pasted");
            Check(!vm.Content.Contains("заменить"), "Выделение не заменено");
            var token = MarkdownLocalImages.Tokens(vm.Content).Single();
            var imagePath = Path.Combine(root, token.Path);
            Check(File.Exists(imagePath) && token.Path.Contains("Рисунки"), "Копия PNG не сохранена");
            using (var stream = File.OpenRead(imagePath))
            {
                var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                var pixel = new byte[8];
                new FormatConvertedBitmap(decoded, PixelFormats.Bgra32, null, 0).CopyPixels(pixel, 8, 0);
                Check(pixel[3] == 128, "Потеряна прозрачность PNG");
            }
            ApplicationCommands.Undo.Execute(null, textBox);
            Check(vm.Content == note.ContentMarkdown, "Ctrl+Z не восстановил исходный текст");
            ApplicationCommands.Redo.Execute(null, textBox);
            Check(MarkdownLocalImages.Tokens(vm.Content).Count == 1, "Повтор вставки не работает");

            // Обычная вставка текста по-прежнему обрабатывается самим TextBox.
            textBox.CaretIndex = textBox.Text.Length;
            Clipboard.SetText(" обычный текст");
            ApplicationCommands.Paste.Execute(null, textBox);
            Check(vm.Content.EndsWith(" обычный текст", StringComparison.Ordinal), "Вставка текста сломана");

            // Скопированные файлы сохраняются независимыми копиями; посторонний файл игнорируется.
            var source = Path.Combine(root, "исходная картинка.png");
            File.WriteAllBytes(source, png.ToArray());
            var files = new DataObject(DataFormats.FileDrop, new[] { source, Path.Combine(root, "не картинка.txt") });
            Clipboard.SetDataObject(files);
            ApplicationCommands.Paste.Execute(null, textBox);
            Until(() => MarkdownLocalImages.Tokens(vm.Content).Count == 2 && !textBox.IsReadOnly);
            Check(File.ReadAllBytes(source).SequenceEqual(png.ToArray()), "Исходный файл изменился");

            // Bitmap без PNG тоже работает; просмотр остаётся просмотром.
            vm.IsPreview = true;
            window.UpdateLayout();
            Clipboard.SetImage(bitmap);
            var preview = (UIElement)editor.FindName("PreviewViewer");
            Check(ApplicationCommands.Paste.CanExecute(null, preview), "Вставка в просмотре недоступна");
            ApplicationCommands.Paste.Execute(null, preview);
            Until(() => MarkdownLocalImages.Tokens(vm.Content).Count == 3 && !textBox.IsReadOnly);
            Check(vm.IsPreview, "Вставка самовольно переключила режим");

            // Сброс файла мышью проходит через тот же путь копирования.
            var drop = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                [files, DragDropKeyStates.None, DragDropEffects.Copy, editor, new Point(10, 10)], null)!;
            drop.RoutedEvent = DragDrop.PreviewDropEvent;
            editor.RaiseEvent(drop);
            Until(() => MarkdownLocalImages.Tokens(vm.Content).Count == 4 && !textBox.IsReadOnly);
            Check(File.ReadAllBytes(source).SequenceEqual(png.ToArray()), "Перенос уничтожил исходник");

            // Настоящий WPF-предпросмотр системы со скриншота.
            var formula = "$r_{ij}=\\begin{cases}\n1, & \\text{если вершины смежны} \\\\\n0, & \\text{если не смежны}\n\\end{cases}$";
            vm.Content = formula;
            vm.IsPreview = false;
            vm.IsPreview = true;
            window.UpdateLayout();
            var host = new Border { Background = Brushes.White, Child = new FlowDocumentScrollViewer
                { Document = MarkdownFlowRenderer.Render(builder, formula, root) } };
            host.Measure(new Size(780, 200));
            host.Arrange(new Rect(0, 0, 780, 200));
            host.UpdateLayout();
            var screenshot = new RenderTargetBitmap(780, 200, 96, 96, PixelFormats.Pbgra32);
            screenshot.Render(host);
            var screenshotEncoder = new PngBitmapEncoder();
            screenshotEncoder.Frames.Add(BitmapFrame.Create(screenshot));
            var output = Path.GetFullPath("artifacts/validation/note-cases-preview.png");
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using (var stream = File.Create(output)) screenshotEncoder.Save(stream);

            // Пока выясняется папка вложений, пользователь может открыть другую заметку.
            var pending = new TaskCompletionSource<NoteStorage?>();
            pendingStorage = pending.Task;
            var insertion = vm.InsertClipboardImageAsync(bitmap, 0);
            note = new Note { Id = Guid.NewGuid(), Title = "Другая заметка", ContentMarkdown = "чужой текст" };
            Pump(vm.LoadAsync(note.Id));
            pending.SetResult(storage);
            Pump(insertion);
            pendingStorage = null;
            Check(insertion.Result is null && vm.Content == "чужой текст", "Картинка попала в другую заметку");
            Check(errors.Count == 0, string.Join("\n", errors));
            Console.WriteLine("WPF: PNG, прозрачность, Bitmap, файлы, перенос мышью, выделение, Undo/Redo, текст, просмотр, смена заметки — OK.");
            Console.WriteLine("Предпросмотр: " + output);
        }
        finally
        {
            Console.WriteLine("Cleaning up smoke test");
            window?.Close();
            if (originalClipboard is not null) Clipboard.SetDataObject(originalClipboard, true);
            else Clipboard.Clear();
            // Каталог создан этим прогоном; рабочие папки приложения не затрагиваются.
            if (root.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase)) Directory.Delete(root, true);
        }
    }

    private static object CreateInternal(Assembly assembly, string name) =>
        Activator.CreateInstance(assembly.GetType(name, throwOnError: true)!, nonPublic: true)!;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Pump(Task task)
    {
        Until(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private static void Until(Func<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(TimeSpan.FromMilliseconds(10), DispatcherPriority.Background,
            (_, _) => { if (completed() || DateTime.UtcNow >= deadline) frame.Continue = false; }, Dispatcher.CurrentDispatcher);
        Dispatcher.PushFrame(frame);
        timer.Stop();
        Check(completed(), "Истекло время ожидания команды WPF");
    }
}

public class Proxy : DispatchProxy
{
    private Func<MethodInfo, object?[]?, object?> _handler = null!;

    public static T Create<T>(Func<MethodInfo, object?[]?, object?>? handler = null) where T : class
    {
        var instance = Create<T, Proxy>();
        ((Proxy)(object)instance)._handler = handler ?? ((method, _) => Default(method.ReturnType));
        return instance;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => _handler(targetMethod!, args);

    public static object? Default(Type type)
    {
        if (type == typeof(Task)) return Task.CompletedTask;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var argument = type.GetGenericArguments()[0];
            return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(argument)
                .Invoke(null, [argument.IsValueType ? Activator.CreateInstance(argument) : null]);
        }
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }
}
