using System.Xml;
using System.Xml.Linq;

namespace HarmonicaScript.Policy.Tests;

/// <summary>
/// Asserts every MSBuild file is well-formed XML.
///
/// This exists because of how badly the failure presents. An XML comment may not contain a
/// double hyphen, so writing "--version" inside one silently makes the file unparseable - and
/// MSBuild then reports it as `RestoreTask returned false but logged no error` followed by
/// `NETSDK1013: TargetFramework value "" not recognized`, which points at a completely different
/// file and suggests a typo in a property that is in fact correct. The parse error names the
/// actual line.
/// </summary>
public sealed class BuildFileTests
{
    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HarmonicaScript.slnx")))
        {
            dir = dir.Parent;
        }

        return dir ?? throw new DirectoryNotFoundException("could not locate the repository root");
    }

    /// <summary>Every MSBuild file in the repository, excluding build output.</summary>
    private static List<string> DiscoverBuildFiles()
    {
        var root = RepositoryRoot();
        var found = new List<string>();

        foreach (var pattern in new[] { "*.props", "*.targets", "*.csproj", "*.slnx" })
        {
            foreach (var file in root.EnumerateFiles(pattern, SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root.FullName, file.FullName);
                if (relative.Contains($"obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    || relative.Contains($"bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
                {
                    continue;
                }

                found.Add(relative);
            }
        }

        return found;
    }

    public static TheoryData<string> BuildFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in DiscoverBuildFiles())
        {
            data.Add(file);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(BuildFiles))]
    public void IsWellFormedXml(string relativePath)
    {
        var path = Path.Combine(RepositoryRoot().FullName, relativePath);

        var exception = Record.Exception(() => XDocument.Load(path, LoadOptions.SetLineInfo));

        Assert.True(
            exception is null,
            $"{relativePath} is not well-formed XML: {exception?.Message}"
            + (exception is XmlException ? " (an XML comment may not contain '--')" : string.Empty));
    }

    [Fact]
    public void FindsTheBuildFilesItIsSupposedTo()
    {
        // Guards the guard: a broken discovery pattern would make every case above vacuous.
        var files = DiscoverBuildFiles();

        Assert.Contains("Directory.Build.props", files);
        Assert.Contains("Directory.Build.targets", files);
        Assert.Contains("Directory.Packages.props", files);
        Assert.Contains("HarmonicaScript.slnx", files);
        Assert.True(files.Count >= 20, $"expected at least 20 build files, found {files.Count}");
    }
}
