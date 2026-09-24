using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using StudComp.Core.Common;

namespace StudComp.Infrastructure.Settings;

/// <summary>
/// Читает и пишет пользовательский оверлей настроек <c>%LocalAppData%\Rubrica\usersettings.json</c>
/// (ARCHITECTURE §11.2). Файл уже подключён в цепочку конфигурации с <c>reloadOnChange: true</c>,
/// поэтому после <see cref="Update{T}"/> <c>IOptionsMonitor&lt;T&gt;</c> сам увидит новые значения —
/// провайдер отвечает только за корректную запись нужного поддерева JSON.
/// </summary>
public sealed class UserSettingsProvider
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    // Настройки могут писаться из разных мест UI — сериализуем доступ к файлу.
    private readonly Lock _gate = new();
    private readonly ILogger<UserSettingsProvider> _logger;
    private readonly string _filePath;

    /// <summary>Боевой конструктор: файл оверлея — <see cref="RubricaPaths.UserSettingsFile"/>.</summary>
    public UserSettingsProvider(ILogger<UserSettingsProvider> logger)
        : this(logger, RubricaPaths.UserSettingsFile)
    {
    }

    /// <summary>Конструктор с явным путём к файлу — для тестов.</summary>
    public UserSettingsProvider(ILogger<UserSettingsProvider> logger, string filePath)
    {
        _logger = logger;
        _filePath = filePath;
    }

    /// <summary>
    /// Текущее состояние секции из файла оверлея (без учёта заводских дефолтов из
    /// <c>appsettings.json</c> — те доедут через <c>IOptions&lt;T&gt;</c>). Если секции в файле нет,
    /// возвращает <c>new T()</c>.
    /// </summary>
    /// <param name="sectionName">Путь секции через двоеточие, напр. <c>"Rubrica:Appearance"</c>.</param>
    public T Get<T>(string sectionName) where T : class, new()
    {
        lock (_gate)
        {
            var root = Load();
            var node = Resolve(root, sectionName, createMissing: false);
            return node is JsonObject obj
                ? obj.Deserialize<T>(SerializerOptions) ?? new T()
                : new T();
        }
    }

    /// <summary>
    /// Загружает секцию, применяет <paramref name="mutate"/> и пишет обратно, сохраняя остальное
    /// содержимое файла нетронутым.
    /// </summary>
    /// <param name="sectionName">Путь секции через двоеточие, напр. <c>"Rubrica:General"</c>.</param>
    public void Update<T>(string sectionName, Action<T> mutate) where T : class, new()
    {
        lock (_gate)
        {
            var root = Load();
            var current = Resolve(root, sectionName, createMissing: false) is JsonObject obj
                ? obj.Deserialize<T>(SerializerOptions) ?? new T()
                : new T();

            mutate(current);

            var (parent, key) = ResolveParent(root, sectionName);
            parent[key] = JsonSerializer.SerializeToNode(current, SerializerOptions);
            Save(root);
        }
    }

    private JsonObject Load()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        try
        {
            return JsonNode.Parse(File.ReadAllText(_filePath)) as JsonObject ?? [];
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "usersettings.json повреждён — перезаписываю пустым: {Path}", _filePath);
            return [];
        }
    }

    private void Save(JsonObject root)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Пишем через временный файл рядом, затем атомарно подменяем — чтобы reloadOnChange не поймал
        // половину файла и настройки не потерялись при сбое посреди записи.
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, root.ToJsonString(SerializerOptions));
        File.Move(tempPath, _filePath, overwrite: true);
        _logger.LogDebug("Пользовательские настройки сохранены: {Path}", _filePath);
    }

    /// <summary>Возвращает узел по пути секции, при необходимости создавая промежуточные объекты.</summary>
    private static JsonNode? Resolve(JsonObject root, string sectionName, bool createMissing)
    {
        JsonNode? node = root;
        foreach (var segment in sectionName.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (node is not JsonObject obj)
            {
                return null;
            }

            if (obj[segment] is null && createMissing)
            {
                obj[segment] = new JsonObject();
            }

            node = obj[segment];
        }

        return node;
    }

    /// <summary>Возвращает родительский объект последнего сегмента пути и сам сегмент-ключ.</summary>
    private static (JsonObject Parent, string Key) ResolveParent(JsonObject root, string sectionName)
    {
        var segments = sectionName.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var parent = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (parent[segments[i]] is not JsonObject next)
            {
                next = [];
                parent[segments[i]] = next;
            }

            parent = next;
        }

        return (parent, segments[^1]);
    }
}
