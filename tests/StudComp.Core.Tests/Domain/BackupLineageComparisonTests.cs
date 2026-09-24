using StudComp.Core.Domain;

namespace StudComp.Core.Tests.Domain;

/// <summary>Табличные тесты <see cref="BackupLineageComparison"/> (Phase 13.8, new_addons.md §13).</summary>
public sealed class BackupLineageComparisonTests
{
    private static readonly Guid LineageA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid LineageB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Same_lineage_with_higher_remote_version_is_newer()
    {
        var relation = BackupLineageComparison.Classify(LineageA, remoteVersion: 5, LineageA, localVersion: 3);

        Assert.Equal(BackupLineageRelation.SameLineageNewer, relation);
    }

    [Fact]
    public void Same_lineage_with_lower_remote_version_is_older_or_equal()
    {
        var relation = BackupLineageComparison.Classify(LineageA, remoteVersion: 2, LineageA, localVersion: 3);

        Assert.Equal(BackupLineageRelation.SameLineageOlderOrEqual, relation);
    }

    [Fact]
    public void Same_lineage_with_equal_version_is_older_or_equal()
    {
        var relation = BackupLineageComparison.Classify(LineageA, remoteVersion: 3, LineageA, localVersion: 3);

        Assert.Equal(BackupLineageRelation.SameLineageOlderOrEqual, relation);
    }

    [Fact]
    public void Different_lineage_is_always_reported_as_different_regardless_of_version()
    {
        var relation = BackupLineageComparison.Classify(LineageB, remoteVersion: 99, LineageA, localVersion: 1);

        Assert.Equal(BackupLineageRelation.DifferentOrUnknownLineage, relation);
    }

    [Fact]
    public void Empty_remote_lineage_is_reported_as_unknown_even_if_local_is_also_empty()
    {
        var relation = BackupLineageComparison.Classify(Guid.Empty, remoteVersion: 0, Guid.Empty, localVersion: 0);

        Assert.Equal(BackupLineageRelation.DifferentOrUnknownLineage, relation);
    }

    [Fact]
    public void Empty_local_lineage_against_a_real_remote_lineage_is_reported_as_unknown()
    {
        var relation = BackupLineageComparison.Classify(LineageA, remoteVersion: 1, Guid.Empty, localVersion: 0);

        Assert.Equal(BackupLineageRelation.DifferentOrUnknownLineage, relation);
    }
}
