namespace StudComp.Core.Domain;

/// <summary>
/// Размеры и видимый диапазон сетки плиток на один проход раскладки.
/// </summary>
/// <param name="Columns">Сколько плиток помещается в ряд. Всегда не меньше единицы.</param>
/// <param name="Rows">Сколько всего рядов занимает список.</param>
/// <param name="ExtentHeight">Полная высота содержимого — из неё панель считает полосу прокрутки.</param>
/// <param name="FirstVisibleIndex">Первый элемент, который надо материализовать (с учётом запаса).</param>
/// <param name="LastVisibleIndex">Последний материализуемый элемент; <c>-1</c> — список пуст.</param>
public readonly record struct VirtualWrapMetrics(
    int Columns,
    int Rows,
    double ExtentHeight,
    int FirstVisibleIndex,
    int LastVisibleIndex)
{
    /// <summary>Сколько элементов надо реально создать в этом проходе.</summary>
    public int VisibleCount => LastVisibleIndex < FirstVisibleIndex ? 0 : LastVisibleIndex - FirstVisibleIndex + 1;
}

/// <summary>
/// Геометрия виртуализирующей сетки карточек (new_addons.md §8.2): сколько колонок влезло, какой
/// кусок списка сейчас виден и куда прокрутиться, чтобы показать нужный элемент.
/// </summary>
/// <remarks>
/// <para>
/// Чистый расчёт без WPF — по тому же доводу, что <see cref="ScheduleLaneLayout"/>: самая
/// рискованная часть собственной панели это арифметика диапазона, и проверять её надо тестами, а не
/// глазами по экрану. Сама <c>VirtualizingWrapPanel</c> в App остаётся тонкой оболочкой.
/// </para>
/// <para>
/// Расчёт опирается на равный размер плиток: только он позволяет найти видимый диапазон за
/// константу, не пробегая список. Плитки библиотеки фиксированного размера именно поэтому.
/// </para>
/// </remarks>
public static class VirtualWrapLayout
{
    /// <summary>
    /// Посчитать раскладку. Отрицательные и нулевые размеры не роняют расчёт: они означают «окно
    /// ещё не измерено», и результат вырождается в одну колонку без видимых элементов.
    /// </summary>
    /// <param name="itemCount">Сколько всего элементов в списке.</param>
    /// <param name="itemWidth">Ширина плитки.</param>
    /// <param name="itemHeight">Высота плитки.</param>
    /// <param name="viewportWidth">Ширина окна прокрутки.</param>
    /// <param name="viewportHeight">Высота окна прокрутки.</param>
    /// <param name="verticalOffset">Текущее смещение прокрутки сверху.</param>
    /// <param name="overscanRows">Сколько рядов держать про запас сверху и снизу — от них зависит плавность.</param>
    public static VirtualWrapMetrics Compute(
        int itemCount,
        double itemWidth,
        double itemHeight,
        double viewportWidth,
        double viewportHeight,
        double verticalOffset,
        int overscanRows = 1)
    {
        var columns = ColumnsFor(viewportWidth, itemWidth);
        var count = Math.Max(0, itemCount);
        var height = IsUsable(itemHeight) ? itemHeight : 0d;

        var rows = count == 0 ? 0 : (count + columns - 1) / columns;
        var extent = rows * height;

        if (count == 0 || height <= 0)
        {
            return new VirtualWrapMetrics(columns, rows, extent, 0, -1);
        }

        var overscan = Math.Max(0, overscanRows);
        var offset = Math.Clamp(SafeValue(verticalOffset), 0d, Math.Max(0d, extent - Math.Max(0d, viewportHeight)));

        // Высота окна может быть нулевой на первом проходе измерения — тогда показываем один ряд,
        // иначе панель не создала бы ни одного контейнера и никогда не получила бы реальный размер.
        var rowsInView = Math.Max(1, (int)Math.Ceiling(Math.Max(0d, SafeValue(viewportHeight)) / height));

        var firstRow = Math.Max(0, (int)Math.Floor(offset / height) - overscan);
        var lastRow = Math.Min(rows - 1, firstRow + rowsInView + (2 * overscan));

        var first = firstRow * columns;
        var last = Math.Min(count - 1, ((lastRow + 1) * columns) - 1);

        return new VirtualWrapMetrics(columns, rows, extent, first, last);
    }

    /// <summary>Сколько плиток влезает в ряд. Окно уже одной плитки всё равно показывает одну.</summary>
    public static int ColumnsFor(double viewportWidth, double itemWidth)
    {
        if (!IsUsable(itemWidth) || !IsUsable(viewportWidth))
        {
            return 1;
        }

        return Math.Max(1, (int)(viewportWidth / itemWidth));
    }

    /// <summary>Левый верхний угол плитки внутри содержимого.</summary>
    public static (double X, double Y) PositionOf(int index, int columns, double itemWidth, double itemHeight)
    {
        var safeColumns = Math.Max(1, columns);
        var safeIndex = Math.Max(0, index);

        var column = safeIndex % safeColumns;
        var row = safeIndex / safeColumns;

        return (column * Math.Max(0d, itemWidth), row * Math.Max(0d, itemHeight));
    }

    /// <summary>
    /// Смещение прокрутки, при котором элемент целиком попадает в окно. Если он и так виден,
    /// смещение не меняется — навигация стрелками не должна дёргать список без нужды.
    /// </summary>
    public static double OffsetToReveal(
        int index,
        int columns,
        double itemHeight,
        double viewportHeight,
        double currentOffset)
    {
        var offset = Math.Max(0d, SafeValue(currentOffset));
        if (index < 0 || !IsUsable(itemHeight))
        {
            return offset;
        }

        var row = index / Math.Max(1, columns);
        var top = row * itemHeight;
        var bottom = top + itemHeight;
        var view = Math.Max(0d, SafeValue(viewportHeight));

        if (top < offset)
        {
            return top;
        }

        return bottom > offset + view ? Math.Max(0d, bottom - view) : offset;
    }

    /// <summary>Пригодная для расчёта величина: положительная и конечная.</summary>
    private static bool IsUsable(double value) => value > 0 && !double.IsInfinity(value) && !double.IsNaN(value);

    /// <summary>Бесконечность и NaN приходят из проходов измерения WPF — считаем их нулём.</summary>
    private static double SafeValue(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
}
