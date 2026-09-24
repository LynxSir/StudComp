using StudComp.Core.Common;

namespace StudComp.Core.Tests;

/// <summary>
/// Стережёт Dependency Rule машинно, а не на глаз: <c>StudComp.Core</c> не знает ни про EF Core,
/// ни про WPF, ни про OpenXML, ни про hosting (ARCHITECTURE §2, §5.1; DoD Phase 1 в PLAN).
/// </summary>
public class CoreDependenciesTests
{
    [Fact]
    public void Core_references_nothing_but_the_base_class_library()
    {
        var foreign = typeof(Result).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .Where(name => !IsBcl(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(foreign);
    }

    private static bool IsBcl(string assemblyName) =>
        assemblyName is "netstandard" or "mscorlib"
        || assemblyName == "System"
        || assemblyName.StartsWith("System.", StringComparison.Ordinal);
}
