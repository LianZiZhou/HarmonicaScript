using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HarmonicaScript.Core.Instrument;
using HarmonicaScript.Core.Timeline;

namespace HarmonicaScript.Export.Exporters;

// Wire shapes for our own interchange formats, with a reader for each so a round-trip test has
// a genuine consumer rather than being a mirror of the writer.

public sealed class HsEventDto
{
    [JsonPropertyName("t")] public int TimeMs { get; set; }
    [JsonPropertyName("phase")] public string Phase { get; set; } = string.Empty;
    [JsonPropertyName("device")] public string Device { get; set; } = string.Empty;
    [JsonPropertyName("code")] public ushort Code { get; set; }
    [JsonPropertyName("down")] public bool IsDown { get; set; }
    [JsonPropertyName("note")] public int NoteId { get; set; }
}

public sealed class HsTimelineDto
{
    [JsonPropertyName("formatVersion")] public int FormatVersion { get; set; }
    [JsonPropertyName("instrument")] public string Instrument { get; set; } = string.Empty;
    [JsonPropertyName("timing")] public string Timing { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("scoreSha256")] public string ScoreSha256 { get; set; } = string.Empty;
    [JsonPropertyName("settingsSha256")] public string SettingsSha256 { get; set; } = string.Empty;
    [JsonPropertyName("totalDurationMs")] public int TotalDurationMs { get; set; }
    [JsonPropertyName("events")] public List<HsEventDto> Events { get; set; } = [];
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(HsTimelineDto))]
public sealed partial class ExportJsonContext : JsonSerializerContext;

/// <summary>Our own JSON interchange format: fully self-describing, and readable back in.</summary>
public sealed class HarmonicaScoreJsonExporter : IMacroExporter
{
    public string Id => "hscore-json";

    public LocalizedText DisplayName => new("HarmonicaScript timeline (JSON)", "HarmonicaScript 时间轴 (JSON)");

    public string FileExtension => ".hscore.json";

    public ExporterCapabilities Capabilities { get; } = new();

    public ExportResult Write(ExportContext context, Stream output)
    {
        ArgumentNullException.ThrowIfNull(context);

        var dto = new HsTimelineDto
        {
            FormatVersion = context.Timeline.FormatVersion,
            Instrument = context.Timeline.InstrumentRef,
            Timing = context.Timeline.TimingRef,
            Title = context.Meta.Title,
            ScoreSha256 = context.Timeline.ScoreSha256,
            SettingsSha256 = context.Timeline.SettingsSha256,
            TotalDurationMs = context.Timeline.TotalDurationMs,
            Events = [.. context.Timeline.Events.Select(e => new HsEventDto
            {
                TimeMs = e.TimeMs,
                Phase = e.Phase.ToString(),
                Device = e.Device.ToString(),
                Code = e.Code,
                IsDown = e.IsDown,
                NoteId = e.NoteId,
            })],
        };

        JsonSerializer.Serialize(output, dto, ExportJsonContext.Default.HsTimelineDto);
        return ExportResult.Success();
    }

    /// <summary>Round-trip reader. Has a real consumer: <c>hsc replay</c> and the golden-file tests.</summary>
    public static InputTimeline Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var dto = JsonSerializer.Deserialize(input, ExportJsonContext.Default.HsTimelineDto)
            ?? throw new InvalidDataException("timeline JSON deserialised to null");

        var events = dto.Events.Select(e => new InputEvent(
            e.TimeMs,
            Enum.Parse<InputPhase>(e.Phase),
            Enum.Parse<InputDeviceKind>(e.Device),
            e.Code,
            e.IsDown,
            e.NoteId)).ToList();

        return new InputTimeline(
            dto.FormatVersion, dto.Instrument, dto.Timing, dto.ScoreSha256, dto.SettingsSha256,
            events, dto.TotalDurationMs, []);
    }
}

/// <summary>CSV, for anyone who would rather open it in a spreadsheet than write a parser.</summary>
public sealed class HarmonicaScoreCsvExporter : IMacroExporter
{
    public string Id => "hscore-csv";

    public LocalizedText DisplayName => new("HarmonicaScript timeline (CSV)", "HarmonicaScript 时间轴 (CSV)");

    public string FileExtension => ".hscore.csv";

    public ExporterCapabilities Capabilities { get; } = new(NewLine: "\n");

    public ExportResult Write(ExportContext context, Stream output)
    {
        ArgumentNullException.ThrowIfNull(context);

        var builder = new StringBuilder();
        builder.Append("timeMs,deltaMs,phase,device,code,label,isDown,noteId\n");

        var lowered = MacroLowering.Lower(context.Timeline, Capabilities);
        foreach (var part in lowered.Parts)
        {
            foreach (var e in part.Events)
            {
                var binding = new Binding(e.Event.Device, e.Event.Code);
                builder.Append(e.Event.TimeMs.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(e.DelayBeforeMs.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(e.Event.Phase).Append(',')
                    .Append(e.Event.Device).Append(',')
                    .Append(e.Event.Code.ToString(CultureInfo.InvariantCulture)).Append(',')
                    .Append(context.Keys.Describe(binding)).Append(',')
                    .Append(e.Event.IsDown ? '1' : '0').Append(',')
                    .Append(e.Event.NoteId.ToString(CultureInfo.InvariantCulture)).Append('\n');
            }
        }

        var bytes = Capabilities.Encoding.GetBytes(builder.ToString());
        output.Write(bytes, 0, bytes.Length);
        return new ExportResult(true, lowered.Warnings, [], []);
    }

    public static IReadOnlyList<InputEvent> Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);
        using var reader = new StreamReader(input);

        var events = new List<InputEvent>();
        _ = reader.ReadLine();   // header

        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0)
            {
                continue;
            }

            var f = line.Split(',');
            events.Add(new InputEvent(
                int.Parse(f[0], CultureInfo.InvariantCulture),
                Enum.Parse<InputPhase>(f[2]),
                Enum.Parse<InputDeviceKind>(f[3]),
                ushort.Parse(f[4], CultureInfo.InvariantCulture),
                f[6] == "1",
                int.Parse(f[7], CultureInfo.InvariantCulture)));
        }

        return events;
    }
}
