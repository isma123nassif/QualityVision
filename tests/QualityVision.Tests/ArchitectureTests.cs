using System.Xml.Linq;

using QualityVision.Core;

namespace QualityVision.Tests;

public class ArchitectureTests
{
    [Fact]
    public void CoreAssembly_ReferencedAssemblies_OnlyBaseClassLibrary()
    {
        var offending = typeof(CoreAssembly).Assembly.GetReferencedAssemblies()
            .Select(a => a.Name ?? string.Empty)
            .Where(name => !IsBaseClassLibrary(name))
            .ToList();

        Assert.True(offending.Count == 0,
            $"Core references non-BCL assemblies: {string.Join(", ", offending)}");
    }

    [Fact]
    public void CoreProject_ProjectFile_HasNoPackageOrFrameworkReferences()
    {
        var csproj = Path.Combine(FindSolutionRoot(), "src", "QualityVision.Core", "QualityVision.Core.csproj");
        var doc = XDocument.Load(csproj);

        Assert.Empty(doc.Descendants("PackageReference"));
        Assert.Empty(doc.Descendants("FrameworkReference"));
    }

    private static bool IsBaseClassLibrary(string name) =>
        name is "netstandard" or "mscorlib" or "System"
        || name.StartsWith("System.", StringComparison.Ordinal);

    private static string FindSolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "QualityVision.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new DirectoryNotFoundException("QualityVision.slnx not found above test base directory.");
    }
}
