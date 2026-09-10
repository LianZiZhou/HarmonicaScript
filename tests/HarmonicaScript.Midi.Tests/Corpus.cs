using HarmonicaScript.Core.Source;
using HarmonicaScript.Midi;
using HarmonicaScript.Midi.Fixtures;

namespace HarmonicaScript.Midi.Tests;

/// <summary>
/// Regenerates the whole corpus from source once per test run, so the suite tests the
/// generators rather than a stale committed binary that may have drifted from them.
/// </summary>
public static class Corpus
{
    private static readonly Lazy<string> RootValue = new(() =>
    {
        var root = FixtureCorpus.FindTestDataRoot()
            ?? throw new DirectoryNotFoundException("testdata/ not found above the test assembly.");
        FixtureCorpus.WriteAll(root);
        return root;
    });

    public static string Root => RootValue.Value;

    public static string Path(string category, string name) =>
        System.IO.Path.Combine(Root, category, name + ".mid");

    public static SourceSong Read(string category, string name) => MidiReader.Read(Path(category, name));

    public static TheoryData<string> PublicDomainNames()
    {
        var data = new TheoryData<string>();
        foreach (var m in PublicDomainCorpus.All)
        {
            data.Add(m.FileName);
        }

        return data;
    }

    public static TheoryData<string> ProceduralNames()
    {
        var data = new TheoryData<string>();
        foreach (var f in ProceduralFixtures.All)
        {
            data.Add(f.Name);
        }

        return data;
    }

    public static TheoryData<string> MalformedNames()
    {
        var data = new TheoryData<string>();
        foreach (var f in MalformedFixtures.All)
        {
            data.Add(f.Name);
        }

        return data;
    }

    /// <summary>Every readable fixture, for sweeps that should hold across the whole corpus.</summary>
    public static IEnumerable<(string Category, string Name)> Everything()
    {
        foreach (var m in PublicDomainCorpus.All)
        {
            yield return ("pd", m.FileName);
        }

        foreach (var f in ProceduralFixtures.All)
        {
            yield return ("generated", f.Name);
        }
    }
}
