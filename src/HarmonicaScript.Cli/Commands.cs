using System.CommandLine;
using System.Globalization;
using HarmonicaScript.Audition;
using HarmonicaScript.Core.Conversion;
using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Midi.Fixtures;
using HarmonicaScript.Profiles;
using HarmonicaScript.Project;

namespace HarmonicaScript.Cli;

/// <summary>
/// Every core capability is reachable headlessly, with no GUI assembly loaded. That is not a
/// convenience: it is what makes the engine testable in CI, scriptable in bulk, and debuggable
/// from a user's reproduction without a screen.
/// </summary>
internal static class Commands
{
    private static readonly Argument<FileInfo> MidiArgument = new("midi") { Description = "The MIDI file to convert." };

    private static readonly Option<string> InstrumentOption =
        new("--instrument") { Description = "Instrument profile id.", DefaultValueFactory = _ => "df.harmonica.v1" };

    private static readonly Option<string> TimingOption =
        new("--timing") { Description = "Timing profile id.", DefaultValueFactory = _ => "df.reference" };

    private static readonly Option<double> SpeedOption =
        new("--speed") { Description = "Playback speed multiplier.", DefaultValueFactory = _ => 1.0 };

    private static readonly Option<int?> TransposeOption =
        new("--transpose") { Description = "Force a semitone transposition instead of searching." };

    private static readonly Option<string> ReductionOption =
        new("--reduce") { Description = "Highest | Lowest | Loudest | TrackPriority.", DefaultValueFactory = _ => "Highest" };

    private static ConversionSettings SettingsFrom(ParseResult parse) => new()
    {
        Speed = parse.GetValue(SpeedOption),
        Reduction = Enum.Parse<ReductionPolicy>(parse.GetValue(ReductionOption) ?? "Highest", ignoreCase: true),
        TranspositionMode = parse.GetValue(TransposeOption) is null
            ? TranspositionMode.Balanced
            : TranspositionMode.Manual,
        ManualTranspose = parse.GetValue(TransposeOption) ?? 0,
    };

    private static ConversionService ServiceFrom(ParseResult parse) =>
        new(parse.GetValue(InstrumentOption)!, parse.GetValue(TimingOption)!);

    internal static Command Convert()
    {
        var command = new Command("convert", "Convert a MIDI file and print the playability report.");
        var json = new Option<bool>("--json") { Description = "Emit the report as JSON instead of text." };

        command.Arguments.Add(MidiArgument);
        foreach (var option in new Option[] { InstrumentOption, TimingOption, SpeedOption, TransposeOption, ReductionOption, json })
        {
            command.Options.Add(option);
        }

        command.SetAction(parse =>
        {
            var outcome = ServiceFrom(parse).Convert(parse.GetRequiredValue(MidiArgument).FullName, SettingsFrom(parse));
            Report.Print(outcome, Console.Out, parse.GetValue(json));
            return 0;
        });

        return command;
    }

    internal static Command Export()
    {
        var command = new Command("export", "Convert and write a macro or notation file.");
        var target = new Option<string>("--target", "-t") { Description = "Exporter id. See 'hsc targets'.", Required = true };
        var output = new Option<string?>("--out", "-o") { Description = "Output path. Defaults to the input's name and the target's extension." };

        command.Arguments.Add(MidiArgument);
        foreach (var option in new Option[] { target, output, InstrumentOption, TimingOption, SpeedOption, TransposeOption, ReductionOption })
        {
            command.Options.Add(option);
        }

        command.SetAction(parse =>
        {
            var midi = parse.GetRequiredValue(MidiArgument);
            var registry = ConversionService.BuildRegistry();
            var id = parse.GetValue(target)!;
            var exporter = registry.ById(id);

            if (exporter is null)
            {
                Console.Error.WriteLine($"Unknown target '{id}'. Known targets: {string.Join(", ", registry.All.Select(e => e.Id))}");
                return 2;
            }

            var outcome = ServiceFrom(parse).Convert(midi.FullName, SettingsFrom(parse));
            var stem = Path.GetFileNameWithoutExtension(midi.Name);
            var path = parse.GetValue(output) ?? Path.Combine(midi.DirectoryName ?? ".", stem + exporter.FileExtension);

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            using var stream = File.Create(path);
            var result = exporter.Write(ConversionService.BuildExportContext(outcome, stem), stream);

            foreach (var warning in result.Warnings)
            {
                Console.Error.WriteLine($"warning [{warning.Code}]: {warning.Detail}");
            }

            if (!result.Ok)
            {
                Console.Error.WriteLine($"{exporter.Id} refused to write this file. See the warnings above.");
                return 3;
            }

            Console.WriteLine($"wrote {path}");
            foreach (var instruction in result.ExtraInstructions)
            {
                Console.WriteLine("  " + instruction);
            }

            return 0;
        });

        return command;
    }

    internal static Command Targets()
    {
        var command = new Command("targets", "List the available export targets.");
        command.SetAction(_ =>
        {
            foreach (var exporter in ConversionService.BuildRegistry().All)
            {
                var mouse = exporter.Capabilities.Mouse;
                var verified = mouse.Count == 3 ? "all three mouse buttons" : $"only {string.Join('/', mouse)}";
                Console.WriteLine($"{exporter.Id,-16} {exporter.FileExtension,-16} {exporter.DisplayName.En}");
                Console.WriteLine($"{string.Empty,-16} {string.Empty,-16} verified: {verified}");
            }

            return 0;
        });

        return command;
    }

    internal static Command Audition()
    {
        var command = new Command("audition", "Render what the instrument will actually play, to a WAV file.");
        var output = new Option<string?>("--out", "-o") { Description = "Output WAV path." };
        var soundFont = new Option<string?>("--soundfont") { Description = "Optional SoundFont (.sf2). Falls back to a built-in reed voice." };

        command.Arguments.Add(MidiArgument);
        foreach (var option in new Option[] { output, soundFont, InstrumentOption, TimingOption, SpeedOption, TransposeOption })
        {
            command.Options.Add(option);
        }

        command.SetAction(parse =>
        {
            var midi = parse.GetRequiredValue(MidiArgument);
            var outcome = ServiceFrom(parse).Convert(midi.FullName, SettingsFrom(parse));
            var simulation = ConversionService.Simulate(outcome);

            var path = parse.GetValue(output)
                ?? Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(midi.Name) + ".wav");

            AuditionRenderer.RenderToFile(
                simulation.Notes, path, new AuditionOptions(SoundFontPath: parse.GetValue(soundFont)));

            Console.WriteLine($"wrote {path} ({simulation.Notes.Count} notes)");
            return 0;
        });

        return command;
    }

    internal static Command Simulate()
    {
        var command = new Command("simulate", "Print what the simulator believes the timeline will sound.");
        command.Arguments.Add(MidiArgument);
        foreach (var option in new Option[] { InstrumentOption, TimingOption, SpeedOption, TransposeOption })
        {
            command.Options.Add(option);
        }

        command.SetAction(parse =>
        {
            var outcome = ServiceFrom(parse).Convert(parse.GetRequiredValue(MidiArgument).FullName, SettingsFrom(parse));
            var simulation = ConversionService.Simulate(outcome);

            Console.WriteLine($"{"down(ms)",10} {"up(ms)",8} {"pitch",6}  key");
            foreach (var note in simulation.Notes.Take(200))
            {
                var binding = outcome.Profiles.Instrument.Degrees[note.DegreeIndex].Binding;
                Console.WriteLine(
                    $"{note.DownMs,10} {note.UpMs,8} {note.Pitch,6}  {outcome.Profiles.Keys.Describe(binding)}");
            }

            if (simulation.Repitches.Count > 0)
            {
                Console.Error.WriteLine($"{simulation.Repitches.Count} MID-NOTE RE-PITCH(ES) - this is a bug, please report it.");
                return 1;
            }

            return 0;
        });

        return command;
    }

    internal static Command Profile()
    {
        var command = new Command("profile", "Inspect the effective instrument and timing profiles.");

        var candidates = new Command("candidates", "Print every (degree, modifier state) rendering for every playable offset.");
        candidates.Options.Add(InstrumentOption);
        candidates.Options.Add(TimingOption);
        candidates.SetAction(parse =>
        {
            var loaded = ServiceFrom(parse).LoadProfiles(parse.GetValue(InstrumentOption)!, parse.GetValue(TimingOption)!);
            var table = loaded.Emission;

            Console.WriteLine($"{loaded.Instrument.Ref}: {table.StateCount} states, offsets {table.LoOffset}..{table.HiOffset} "
                + $"({table.OffsetCount}), {table.RenderingCount} renderings");
            Console.WriteLine();
            Console.WriteLine($"{"offset",7} {"ways",5}  renderings");

            foreach (var offset in table.Offsets())
            {
                var renderings = new List<string>();
                for (var s = 0; s < table.StateCount; s++)
                {
                    var degree = table.DegreeOf(s, offset);
                    if (degree < 0)
                    {
                        continue;
                    }

                    var mask = table.States[s].Mask;
                    var mods = mask == 0
                        ? "-"
                        : string.Join('+', loaded.Instrument.Modifiers.Where((_, i) => (mask & (1 << i)) != 0).Select(m => m.Id));
                    renderings.Add($"{loaded.Instrument.Degrees[degree].Jianpu}[{mods}]");
                }

                Console.WriteLine($"{offset,7} {renderings.Count,5}  {string.Join("  ", renderings)}");
            }

            return 0;
        });

        var dump = new Command("dump", "Show every effective field and which layer supplied it.");
        var effective = new Option<bool>("--effective") { Description = "Show the per-field origin." };
        dump.Options.Add(effective);
        dump.Options.Add(InstrumentOption);
        dump.Options.Add(TimingOption);
        dump.SetAction(parse =>
        {
            var loaded = ServiceFrom(parse).LoadProfiles(parse.GetValue(InstrumentOption)!, parse.GetValue(TimingOption)!);
            Console.WriteLine($"layers: {string.Join(" -> ", loaded.LayersUsed)}");
            Console.WriteLine($"verifiedInGame: {loaded.Instrument.Provenance.VerifiedInGame}  confidence: {loaded.Instrument.Provenance.Confidence}");
            Console.WriteLine();

            if (parse.GetValue(effective))
            {
                foreach (var origin in loaded.Origins)
                {
                    Console.WriteLine($"{origin.Path,-46} {origin.Value,-22} <- {origin.Layer}");
                }
            }

            Console.WriteLine();
            Console.WriteLine("timing (values marked ! have never been measured in the game):");
            foreach (var (name, value) in TimingFields(loaded.Timing))
            {
                var badge = value.Measured ? " " : "!";
                Console.WriteLine($"  {badge} {name,-24} {value.V,6} ms   {value.Source}");
            }

            return 0;
        });

        command.Subcommands.Add(candidates);
        command.Subcommands.Add(dump);
        return command;
    }

    internal static Command Fixtures()
    {
        var command = new Command("fixtures", "Regenerate the deterministic test corpus.");
        var dir = new Argument<string?>("dir") { Description = "testdata root.", DefaultValueFactory = _ => null };
        command.Arguments.Add(dir);

        command.SetAction(parse =>
        {
            var root = parse.GetValue(dir)
                ?? FixtureCorpus.FindTestDataRoot()
                ?? throw new DirectoryNotFoundException("Could not locate testdata/.");

            var written = FixtureCorpus.WriteAll(root);
            Console.WriteLine($"wrote {written.Count} fixture(s) under {root}");
            return 0;
        });

        return command;
    }

    private static IEnumerable<(string Name, TimingValue Value)> TimingFields(TimingProfile t) =>
    [
        (nameof(t.SameKeyRetriggerMs), t.SameKeyRetriggerMs),
        (nameof(t.KeyChangeGapMs), t.KeyChangeGapMs),
        (nameof(t.NoteHoldMinMs), t.NoteHoldMinMs),
        (nameof(t.KeyHeldMinMs), t.KeyHeldMinMs),
        (nameof(t.GlobalLeadMs), t.GlobalLeadMs),
        (nameof(t.ExclusiveSwapMs), t.ExclusiveSwapMs),
        (nameof(t.InterModifierStaggerMs), t.InterModifierStaggerMs),
        (nameof(t.MaxOnsetShiftMs), t.MaxOnsetShiftMs),
        (nameof(t.MaxDriftMs), t.MaxDriftMs),
        (nameof(t.MaxKeyHoldMs), t.MaxKeyHoldMs),
        (nameof(t.MaxModifierHoldMs), t.MaxModifierHoldMs),
    ];
}
