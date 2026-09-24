using System.Data.Common;
using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace StudComp.Data.Repositories;

/// <summary>
/// Помощник для агрегатов «по дням» (календарь активности, кривая нагрузки, точность за период).
/// </summary>
/// <remarks>
/// <para>
/// Такие запросы приходится писать сырым SQL: временны́е колонки Картотеки объявлены с конвертером
/// значения (<see cref="Configurations.DataConverters.NullableDateTimeOffsetToUtc"/>), а EF не умеет
/// транслировать обращение к членам конвертированного свойства — <c>x.ReviewedAt.Date</c> падает
/// уже на построении запроса. Прецедент сырого SQL в этом слое — <see cref="CardSearchRepository"/>;
/// за пределы <c>StudComp.Data</c> он по-прежнему не выходит (new_addons.md §10.3).
/// </para>
/// <para>
/// Даты хранятся в UTC, поэтому день считается со сдвигом: местное смещение минус час начала
/// «учебных суток». Иначе календарь уезжает на часовой пояс, а ночная сессия попадает не в тот день.
/// </para>
/// </remarks>
internal static class SqliteDayGrouping
{
    /// <summary>Формат, в котором EF Core пишет <c>DateTime</c> в SQLite.</summary>
    private const string StorageFormat = "yyyy-MM-dd HH:mm:ss.FFFFFFF";

    /// <summary>Добавить параметр сдвига для функции <c>date()</c>.</summary>
    public static void AddShift(DbCommand command, int offsetMinutes) =>
        command.Parameters.Add(new SqliteParameter("$shift", $"{offsetMinutes} minutes"));

    /// <summary>Добавить параметр-момент в том же виде, в каком его хранит EF.</summary>
    public static void AddInstant(DbCommand command, string name, DateTimeOffset moment) =>
        command.Parameters.Add(new SqliteParameter(
            name,
            moment.UtcDateTime.ToString(StorageFormat, CultureInfo.InvariantCulture)));

    /// <summary>Прочитать колонку <c>date(...)</c> как календарный день.</summary>
    public static DateOnly ReadDay(DbDataReader reader, int ordinal) =>
        DateOnly.ParseExact(reader.GetString(ordinal), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Выполнить запрос и собрать строки.</summary>
    public static async Task<IReadOnlyList<T>> ReadAsync<T>(
        StudCompDbContext context,
        string sql,
        Action<DbCommand> bind,
        Func<DbDataReader, T> map,
        CancellationToken ct)
    {
        await context.Database.OpenConnectionAsync(ct).ConfigureAwait(false);

        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            bind(command);

            var rows = new List<T>();
            await using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(map(reader));
            }

            return rows;
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
