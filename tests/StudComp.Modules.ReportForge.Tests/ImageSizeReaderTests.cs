using StudComp.Modules.ReportForge.Services;

namespace StudComp.Modules.ReportForge.Tests;

/// <summary>
/// Чтение размеров и плотности картинки из заголовка. Плотность важна для скриншотов 144/192 dpi —
/// без неё Word вставит их «в пикселях» и раздует (Phase 9, ARCHITECTURE §10.5).
/// </summary>
public class ImageSizeReaderTests
{
    [Fact]
    public void Png_without_phys_defaults_to_96_dpi()
    {
        var size = ImageSizeReader.TryRead(TestImages.BuildPng(200, 120));

        Assert.NotNull(size);
        Assert.Equal(200, size!.Value.Width);
        Assert.Equal(120, size.Value.Height);
        Assert.Equal(96d, size.Value.DpiX);
        Assert.Equal(96d, size.Value.DpiY);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void Png_phys_density_is_read_back(int dpi)
    {
        var bytes = TestImages.BuildPng(300, 300, TestImages.PixelsPerMetreForDpi(dpi));

        var size = ImageSizeReader.TryRead(bytes);

        Assert.NotNull(size);
        Assert.Equal(dpi, size!.Value.DpiX);
        Assert.Equal(dpi, size.Value.DpiY);
    }

    [Fact]
    public void Sample_png_is_still_read_as_96_dpi()
    {
        var size = ImageSizeReader.TryRead(TestImages.SamplePng);

        Assert.NotNull(size);
        Assert.Equal(TestImages.SampleWidth, size!.Value.Width);
        Assert.Equal(96d, size.Value.DpiX);
    }
}
