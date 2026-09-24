using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>
/// Геометрия виртуализирующей сетки карточек (new_addons.md §8.2). Именно этот расчёт решает, что
/// сейчас создать на экране, а что выбросить, — и проверять его надо здесь, а не глазами.
/// </summary>
public sealed class VirtualWrapLayoutTests
{
    [Theory]
    [InlineData(1000, 250, 4)]
    [InlineData(999, 250, 3)]
    [InlineData(250, 250, 1)]
    [InlineData(100, 250, 1)]
    [InlineData(0, 250, 1)]
    [InlineData(1000, 0, 1)]
    public void Columns_fit_the_viewport_and_never_drop_below_one(
        double viewportWidth,
        double itemWidth,
        int expected)
    {
        Assert.Equal(expected, VirtualWrapLayout.ColumnsFor(viewportWidth, itemWidth));
    }

    [Fact]
    public void Rows_and_extent_are_computed_from_item_count()
    {
        var metrics = VirtualWrapLayout.Compute(
            itemCount: 10,
            itemWidth: 250,
            itemHeight: 150,
            viewportWidth: 750,
            viewportHeight: 600,
            verticalOffset: 0);

        Assert.Equal(3, metrics.Columns);
        Assert.Equal(4, metrics.Rows);
        Assert.Equal(600, metrics.ExtentHeight);
    }

    [Fact]
    public void Empty_list_has_no_visible_items()
    {
        var metrics = VirtualWrapLayout.Compute(0, 250, 150, 750, 600, 0);

        Assert.Equal(0, metrics.Rows);
        Assert.Equal(0, metrics.ExtentHeight);
        Assert.Equal(-1, metrics.LastVisibleIndex);
        Assert.Equal(0, metrics.VisibleCount);
    }

    [Fact]
    public void Visible_range_starts_at_the_top_when_not_scrolled()
    {
        var metrics = VirtualWrapLayout.Compute(
            itemCount: 100,
            itemWidth: 250,
            itemHeight: 150,
            viewportWidth: 750,
            viewportHeight: 300,
            verticalOffset: 0,
            overscanRows: 0);

        Assert.Equal(0, metrics.FirstVisibleIndex);

        // Два ряда видны целиком, третий частично — его тоже надо создать, иначе при прокрутке
        // на пиксель откроется пустота.
        Assert.Equal(8, metrics.LastVisibleIndex);
    }

    [Fact]
    public void Scrolled_range_keeps_a_row_of_overscan_above_and_below()
    {
        var metrics = VirtualWrapLayout.Compute(
            itemCount: 100,
            itemWidth: 250,
            itemHeight: 150,
            viewportWidth: 750,
            viewportHeight: 300,
            verticalOffset: 600,
            overscanRows: 1);

        // Смещение 600 — это четвёртый ряд; с запасом в ряд диапазон начинается с третьего.
        Assert.Equal(9, metrics.FirstVisibleIndex);
        Assert.Equal(23, metrics.LastVisibleIndex);
    }

    [Fact]
    public void Range_never_runs_past_the_last_item()
    {
        var metrics = VirtualWrapLayout.Compute(
            itemCount: 7,
            itemWidth: 250,
            itemHeight: 150,
            viewportWidth: 750,
            viewportHeight: 900,
            verticalOffset: 0);

        Assert.Equal(6, metrics.LastVisibleIndex);
        Assert.Equal(7, metrics.VisibleCount);
    }

    [Fact]
    public void Offset_beyond_the_content_is_clamped_instead_of_showing_emptiness()
    {
        var metrics = VirtualWrapLayout.Compute(
            itemCount: 6,
            itemWidth: 250,
            itemHeight: 150,
            viewportWidth: 750,
            viewportHeight: 300,
            verticalOffset: 100_000,
            overscanRows: 0);

        Assert.Equal(0, metrics.FirstVisibleIndex);
        Assert.Equal(5, metrics.LastVisibleIndex);
    }

    [Fact]
    public void Unmeasured_viewport_still_realizes_one_row()
    {
        // Первый проход измерения приходит с нулевой высотой: не создать ничего означало бы
        // никогда не получить настоящий размер.
        var metrics = VirtualWrapLayout.Compute(20, 250, 150, 750, 0, 0, overscanRows: 0);

        Assert.Equal(0, metrics.FirstVisibleIndex);
        Assert.Equal(5, metrics.LastVisibleIndex);
    }

    [Theory]
    [InlineData(0, 3, 0, 0)]
    [InlineData(2, 3, 2, 0)]
    [InlineData(3, 3, 0, 1)]
    [InlineData(5, 3, 2, 1)]
    public void Position_is_a_plain_grid_cell(int index, int columns, int column, int row)
    {
        var (x, y) = VirtualWrapLayout.PositionOf(index, columns, 250, 150);

        Assert.Equal(column * 250, x);
        Assert.Equal(row * 150, y);
    }

    [Fact]
    public void Position_survives_degenerate_arguments()
    {
        var (x, y) = VirtualWrapLayout.PositionOf(-5, 0, -250, -150);

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void Reveal_scrolls_up_to_an_item_above_the_viewport()
    {
        var offset = VirtualWrapLayout.OffsetToReveal(
            index: 0,
            columns: 3,
            itemHeight: 150,
            viewportHeight: 300,
            currentOffset: 600);

        Assert.Equal(0, offset);
    }

    [Fact]
    public void Reveal_scrolls_down_just_enough_to_show_the_item()
    {
        // Элемент 12 лежит в пятом ряду: его низ на 750, значит окно высотой 300 должно стоять на 450.
        var offset = VirtualWrapLayout.OffsetToReveal(12, 3, 150, 300, 0);

        Assert.Equal(450, offset);
    }

    [Fact]
    public void Reveal_leaves_a_visible_item_alone()
    {
        var offset = VirtualWrapLayout.OffsetToReveal(4, 3, 150, 300, 150);

        Assert.Equal(150, offset);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void Reveal_is_a_no_op_without_usable_input(int index)
    {
        var offset = VirtualWrapLayout.OffsetToReveal(index, 3, itemHeight: 0, viewportHeight: 300, currentOffset: 42);

        Assert.Equal(42, offset);
    }

    [Fact]
    public void Infinities_and_nan_do_not_break_the_layout()
    {
        var metrics = VirtualWrapLayout.Compute(
            itemCount: 10,
            itemWidth: 250,
            itemHeight: 150,
            viewportWidth: double.PositiveInfinity,
            viewportHeight: double.NaN,
            verticalOffset: double.NaN);

        Assert.Equal(1, metrics.Columns);
        Assert.True(metrics.FirstVisibleIndex >= 0);
        Assert.True(metrics.LastVisibleIndex < 10);
    }

    [Fact]
    public void Visible_range_stays_inside_the_list_for_any_offset()
    {
        const int count = 137;

        for (var offset = 0; offset < 3000; offset += 37)
        {
            var metrics = VirtualWrapLayout.Compute(count, 240, 160, 1024, 420, offset);

            Assert.True(metrics.FirstVisibleIndex >= 0);
            Assert.True(metrics.LastVisibleIndex < count);
            Assert.True(metrics.LastVisibleIndex >= metrics.FirstVisibleIndex);
        }
    }
}
