using CommunityToolkit.Mvvm.ComponentModel;

namespace StudComp.Controls;

public sealed partial class NoteImageSizeViewModel : ObservableObject
{
    public NoteImageSizeViewModel(int? width) => _widthText = width?.ToString() ?? string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _widthText;

    public bool CanSave => string.IsNullOrWhiteSpace(WidthText)
        || int.TryParse(WidthText, out var width) && width is >= 32 and <= 2400;

    public int? Width => int.TryParse(WidthText, out var width) ? width : null;
}
