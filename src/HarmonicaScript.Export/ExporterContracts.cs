using System.Text;
using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Timeline;
using HarmonicaScript.Profiles;

namespace HarmonicaScript.Export;

/// <summary>What a target can and cannot express. Drives the shared lowering, so writers stay pure serialisation.</summary>
/// <param name="VerifiedMouseButtons">
/// THE load-bearing field. An exporter refuses rather than guessing an unverified encoding,
/// because a guessed mouse button produces a file that imports perfectly and then presses the
/// wrong control - which is far worse than a clear refusal.
/// </param>
public sealed record ExporterCapabilities(
    int? MaxEvents = null,
    int? MaxDelayMs = null,
    int MinDelayMs = 0,
    int DelayGranularityMs = 1,
    bool SupportsSeparateDownUp = true,
    bool SupportsMouse = true,
    IReadOnlyList<MouseButton>? VerifiedMouseButtons = null,
    bool SupportsComments = true,
    Encoding? TextEncoding = null,
    string NewLine = "\n",
    IReadOnlyDictionary<string, int?>? Quirks = null)
{
    public IReadOnlyList<MouseButton> Mouse => VerifiedMouseButtons ?? [MouseButton.Left, MouseButton.Right, MouseButton.Middle];

    public Encoding Encoding => TextEncoding ?? new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
}

/// <summary>
/// Everything an exporter is allowed to see.
///
/// Macro exporters consume <see cref="Timeline"/> and nothing else - that is what guarantees an
/// exported macro does exactly what the live preview did. NOTATION exporters (jianpu, key tape)
/// legitimately need <see cref="Score"/>, because a note is their unit rather than an event.
/// Neither kind may RE-DERIVE anything: they read what the converter decided, never recompute it.
/// </summary>
public sealed record ExportContext(
    InputTimeline Timeline,
    HarmonicaScore Score,
    InstrumentProfile Instrument,
    KeyTable Keys,
    ScoreMetadata Meta,
    IReadOnlyDictionary<int, string> NoteLabels,
    string OutputStem);

public sealed record ExportWarning(string Code, string Detail);

/// <summary>An extra file the target needs alongside the main one.</summary>
public sealed record ExtraFile(string FileName, byte[] Content, string Purpose);

public sealed record ExportResult(
    bool Ok,
    IReadOnlyList<ExportWarning> Warnings,
    IReadOnlyList<string> ExtraInstructions,
    IReadOnlyList<ExtraFile> ExtraFiles)
{
    public static ExportResult Success(params string[] instructions) => new(true, [], instructions, []);

    /// <summary>Refusal with a NAMED missing capability, never a silent guess.</summary>
    public static ExportResult Refused(string code, string detail) =>
        new(false, [new ExportWarning(code, detail)], [], []);
}

public interface IMacroExporter
{
    string Id { get; }

    LocalizedText DisplayName { get; }

    string FileExtension { get; }

    ExporterCapabilities Capabilities { get; }

    ExportResult Write(ExportContext context, Stream output);
}

/// <summary>Lets the CLI and the GUI enumerate targets without a hard-coded switch, so a new vendor is a leaf change.</summary>
public interface IExporterRegistry
{
    IReadOnlyList<IMacroExporter> All { get; }

    IMacroExporter? ById(string id);
}

public sealed class ExporterRegistry(IEnumerable<IMacroExporter> exporters) : IExporterRegistry
{
    public IReadOnlyList<IMacroExporter> All { get; } = [.. exporters.OrderBy(e => e.Id, StringComparer.Ordinal)];

    public IMacroExporter? ById(string id) =>
        All.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
}
