namespace HarmonicaScript.Midi.Fixtures;

/// <summary>Writes the whole fixture corpus to disk. Deterministic: same inputs, same bytes.</summary>
public static class FixtureCorpus
{
    /// <summary>
    /// Guards the whole corpus write.
    ///
    /// Several test assemblies regenerate the fixtures, and `dotnet test` runs them in PARALLEL
    /// PROCESSES - so without a cross-process lock they race on the same files and fail
    /// intermittently. A named mutex rather than a lock object, because the contenders are
    /// separate processes.
    /// </summary>
    private const string LockName = @"Global\HarmonicaScript.FixtureCorpus";

    public static IReadOnlyList<string> WriteAll(string testDataRoot)
    {
        ArgumentException.ThrowIfNullOrEmpty(testDataRoot);

        using var mutex = new Mutex(initiallyOwned: false, LockName);
        var held = false;
        try
        {
            held = mutex.WaitOne(TimeSpan.FromSeconds(60));
        }
        catch (AbandonedMutexException)
        {
            // A previous holder died mid-write. We own it now and will rewrite everything.
            held = true;
        }

        try
        {
            return WriteAllCore(testDataRoot);
        }
        finally
        {
            if (held)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private static IReadOnlyList<string> WriteAllCore(string testDataRoot)
    {
        var written = new List<string>();

        foreach (var melody in PublicDomainCorpus.All)
        {
            var path = Path.Combine(testDataRoot, "pd", melody.FileName + ".mid");
            FixtureWriter.Write(FixtureWriter.FromMelody(melody), path);
            written.Add(path);
        }

        foreach (var fixture in ProceduralFixtures.All)
        {
            var path = Path.Combine(testDataRoot, "generated", fixture.Name + ".mid");
            FixtureWriter.Write(fixture.Build(), path);
            written.Add(path);
        }

        foreach (var fixture in MalformedFixtures.All)
        {
            var path = Path.GetFullPath(Path.Combine(testDataRoot, "malformed", fixture.Name + ".mid"));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var temporary = path + "." + Environment.ProcessId + ".tmp";
            File.WriteAllBytes(temporary, fixture.Build());
            File.Move(temporary, path, overwrite: true);
            written.Add(path);
        }

        return written;
    }

    /// <summary>Locates <c>testdata/</c> by walking up from the running assembly.</summary>
    public static string? FindTestDataRoot(string? start = null)
    {
        var dir = new DirectoryInfo(start ?? AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "testdata");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
