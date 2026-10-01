using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>Раскладка корзины картинок: дерево само является описью, поэтому разбор обязан быть точным.</summary>
public sealed class NoteImageTrashLayoutTests
{
    private static readonly Guid Note = Guid.Parse("11112222-3333-4444-5555-666677778888");
    private static readonly DateTimeOffset Stamp = new(2026, 9, 24, 13, 45, 7, 123, TimeSpan.Zero);
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "rubrica-trash-tests");

    [Fact]
    public void A_staged_path_round_trips_back_to_its_parts()
    {
        const string relative = "Математика/Рисунки/Фото 1 (2).png";
        var staged = NoteImageTrashLayout.StagePathFor(Root, Note, Stamp, relative);

        Assert.NotNull(staged);
        Assert.True(NoteImageTrashLayout.TryParse(Root, staged, out var noteId, out var stagedAt, out var parsed));
        Assert.Equal(Note, noteId);
        Assert.Equal(Stamp.UtcDateTime, stagedAt.UtcDateTime);
        Assert.Equal(relative, parsed);
    }

    [Fact]
    public void A_file_at_the_root_of_the_study_folder_round_trips_too()
    {
        var staged = NoteImageTrashLayout.StagePathFor(Root, Note, Stamp, "a.png");

        Assert.True(NoteImageTrashLayout.TryParse(Root, staged!, out _, out _, out var parsed));
        Assert.Equal("a.png", parsed);
    }

    [Theory]
    [InlineData("../../escape.png")]
    [InlineData("Рисунки/../../../escape.png")]
    [InlineData("C:/Windows/system32/drivers/etc/hosts")]
    [InlineData("")]
    [InlineData(null)]
    public void A_path_that_escapes_the_trash_is_refused(string? relative) =>
        Assert.Null(NoteImageTrashLayout.StagePathFor(Root, Note, Stamp, relative!));

    [Fact]
    public void A_path_that_stays_inside_after_climbing_is_allowed()
    {
        var staged = NoteImageTrashLayout.StagePathFor(Root, Note, Stamp, "Рисунки/../a.png");

        Assert.NotNull(staged);
        Assert.True(NoteImageTrashLayout.TryParse(Root, staged, out _, out _, out var parsed));
        Assert.Equal("a.png", parsed);
    }

    [Fact]
    public void Parsing_something_outside_the_trash_fails()
    {
        var outside = Path.Combine(Root, "Математика", "Рисунки", "a.png");

        Assert.False(NoteImageTrashLayout.TryParse(Root, outside, out _, out _, out _));
    }

    [Theory]
    [InlineData("not-a-guid/20260924134507123/a.png")]
    [InlineData("11112222333344445555666677778888/not-a-stamp/a.png")]
    [InlineData("11112222333344445555666677778888/20260924134507123")]
    public void A_malformed_trash_path_fails_without_throwing(string tail)
    {
        var candidate = Path.Combine(NoteImageTrashLayout.TrashRoot(Root), tail.Replace('/', Path.DirectorySeparatorChar));

        Assert.False(NoteImageTrashLayout.TryParse(Root, candidate, out var noteId, out _, out _));
        Assert.Equal(Guid.Empty, noteId);
    }

    [Fact]
    public void The_trash_is_recognised_and_ordinary_folders_are_not()
    {
        var staged = NoteImageTrashLayout.StagePathFor(Root, Note, Stamp, "a.png")!;

        Assert.True(NoteImageTrashLayout.IsInsideTrash(Root, staged));
        Assert.True(NoteImageTrashLayout.IsInsideTrash(Root, NoteImageTrashLayout.NoteDirectory(Root, Note)));
        Assert.False(NoteImageTrashLayout.IsInsideTrash(Root, Path.Combine(Root, "Математика", "Рисунки")));
        Assert.False(NoteImageTrashLayout.IsInsideTrash(Root, Path.Combine(Root, ".studcomp")));
    }

    [Fact]
    public void Sessions_of_one_note_sort_by_name_in_chronological_order()
    {
        var earlier = NoteImageTrashLayout.SessionDirectory(Root, Note, Stamp);
        var later = NoteImageTrashLayout.SessionDirectory(Root, Note, Stamp.AddSeconds(1));

        Assert.True(string.CompareOrdinal(Path.GetFileName(earlier), Path.GetFileName(later)) < 0);
    }
}
