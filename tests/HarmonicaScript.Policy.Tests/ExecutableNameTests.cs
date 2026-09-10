using System.Xml.Linq;

namespace HarmonicaScript.Policy.Tests;

/// <summary>
/// Guards the name of every shipped executable against platform extension collisions.
///
/// This exists because of a real, silent failure. The GUI project is HarmonicaScript.App, so its
/// apphost was named "HarmonicaScript.App" - and macOS matches file extensions
/// CASE-INSENSITIVELY, so a plain file ending in ".App" is classified as com.apple.application.
/// Finder then tries to open it as an application BUNDLE, finds a file where a directory is
/// required, and reports "this Mac does not support this application". Nothing in that message
/// hints at the cause, and the identical binary named anything else runs fine.
///
/// Proven at the time with mdls: "HarmonicaScript.App" reported com.apple.application-file while
/// the byte-identical "hsc" reported public.unix-executable, and an EMPTY file named
/// "notanapp.App" also reported com.apple.application-file.
/// </summary>
public sealed class ExecutableNameTests
{
    /// <summary>Extensions macOS reserves for bundle directories. A plain FILE with one of these is broken by construction.</summary>
    private static readonly string[] MacOsBundleExtensions =
        [".app", ".bundle", ".framework", ".kext", ".plugin", ".prefpane", ".qlgenerator", ".xpc"];

    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "HarmonicaScript.slnx")))
        {
            dir = dir.Parent;
        }

        return dir ?? throw new DirectoryNotFoundException("could not locate the repository root");
    }

    /// <summary>Every project that produces an executable, with the file name it will actually ship under.</summary>
    public static TheoryData<string, string> ExecutableProjects()
    {
        var root = RepositoryRoot();
        var data = new TheoryData<string, string>();

        foreach (var csproj in root.EnumerateFiles("*.csproj", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(root.FullName, csproj.FullName);
            if (relative.StartsWith("tests", StringComparison.Ordinal)
                || relative.Contains($"obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var document = XDocument.Load(csproj.FullName);
            var outputType = document.Descendants("OutputType").FirstOrDefault()?.Value;
            if (outputType is not ("Exe" or "WinExe"))
            {
                continue;
            }

            var assemblyName = document.Descendants("AssemblyName").FirstOrDefault()?.Value
                ?? Path.GetFileNameWithoutExtension(csproj.Name);

            data.Add(relative, assemblyName);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ExecutableProjects))]
    public void ShippedExecutableNameDoesNotCollideWithAMacOsBundleExtension(string project, string assemblyName)
    {
        var offender = MacOsBundleExtensions.FirstOrDefault(
            ext => assemblyName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

        Assert.True(
            offender is null,
            $"{project} would ship an executable named '{assemblyName}', which ends in '{offender}'. "
            + "macOS matches extensions case-insensitively and treats that as a bundle DIRECTORY, so Finder "
            + "refuses the file with \"this Mac does not support this application\". Set <AssemblyName> to "
            + "something that does not end in a bundle extension.");
    }

    [Fact]
    public void BothExecutablesAreAccountedFor()
    {
        // Guards the guard: a broken discovery would make the theory above vacuous.
        var names = ExecutableProjects().Select(row => row.Data.Item2).ToList();

        Assert.Contains("hsc", names);
        Assert.Contains("HarmonicaScript", names);
        Assert.Equal(2, names.Count);
    }
}
