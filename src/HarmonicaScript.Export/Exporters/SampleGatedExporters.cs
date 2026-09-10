using System.Globalization;
using System.Text;
using System.Xml;
using HarmonicaScript.Core.Instrument;

namespace HarmonicaScript.Export.Exporters;

/// <summary>
/// Base for vendor exporters whose MOUSE encoding has never been seen in a real exported file.
///
/// The rule this enforces is the most important one in Branch B: an exporter REFUSES with a
/// named missing capability rather than guessing. A guessed mouse encoding produces a file that
/// imports perfectly and then presses the wrong button - which, for an instrument whose three
/// modifiers are the mouse buttons, means silently playing the wrong notes, or worse, holding
/// FIRE in an online shooter. A refusal costs a user five seconds; a wrong guess costs trust.
///
/// It turns "get one more sample" from a discovered surprise into a scheduled task.
/// </summary>
public abstract class SampleGatedExporter : IMacroExporter
{
    public abstract string Id { get; }

    public abstract LocalizedText DisplayName { get; }

    public abstract string FileExtension { get; }

    public abstract ExporterCapabilities Capabilities { get; }

    /// <summary>Which sample would unblock this exporter, in words a user can act on.</summary>
    protected abstract string MissingSampleRecipe { get; }

    public ExportResult Write(ExportContext context, Stream output)
    {
        ArgumentNullException.ThrowIfNull(context);

        var needed = context.Instrument.Modifiers
            .Where(m => m.Binding.IsMouse)
            .Select(m => m.Binding.AsMouseButton)
            .Distinct()
            .ToList();

        var missing = needed.Except(Capabilities.Mouse).ToList();
        if (missing.Count > 0)
        {
            return ExportResult.Refused(
                "unverifiedMouseEncoding",
                $"{Id} cannot encode {string.Join(", ", missing)} - no real exported file has ever been seen "
                + $"containing those events, so any value written here would be a guess. "
                + $"This instrument needs them for {string.Join(", ", context.Instrument.Modifiers.Where(m => m.Binding.IsMouse).Select(m => m.Id))}. "
                + MissingSampleRecipe);
        }

        return WriteVerified(context, output);
    }

    protected abstract ExportResult WriteVerified(ExportContext context, Stream output);
}

/// <summary>
/// Razer Synapse 3 macro XML.
///
/// The document shape is VERIFIED against a real exported keyboard macro: element names, Type=1,
/// per-event Delay in milliseconds, and Makecode as PS/2 Set-1 (the sample's 91/30/42/15/57
/// decode as LWin(extended)/A/LShift/Tab/Space, which is a genuine oracle rather than a mirror
/// of our own writer).
///
/// What is NOT verified: the mouse-event encoding, and the polarity of &lt;State&gt;. The State
/// polarity is stored as a QUIRK so a single field report can invert it without a rebuild.
/// </summary>
public sealed class RazerSynapse3Exporter : SampleGatedExporter
{
    public override string Id => "razer3-xml";

    public override LocalizedText DisplayName => new("Razer Synapse 3 macro", "雷蛇 Synapse 3 宏");

    public override string FileExtension => ".xml";

    public override ExporterCapabilities Capabilities { get; } = new(
        MinDelayMs: 0,
        SupportsSeparateDownUp: true,
        SupportsMouse: true,

        // EMPTY on purpose. The one verified real file contains keyboard events only.
        VerifiedMouseButtons: [],

        TextEncoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        NewLine: "\r\n",
        Quirks: new Dictionary<string, int?>
        {
            // INFERRED by symmetry, never observed. If a field report says otherwise, flip these
            // two numbers in the capabilities data; no code changes.
            ["state.down"] = 0,
            ["state.up"] = 1,
        });

    protected override string MissingSampleRecipe =>
        "To unblock it: record a Synapse 3 macro containing 「按住鼠标右键 173 ms 后松开，按住鼠标中键 89 ms 后松开」, "
        + "export it, and attach the XML to a vendor-sample issue.";

    protected override ExportResult WriteVerified(ExportContext context, Stream output)
    {
        var lowered = MacroLowering.Lower(context.Timeline, Capabilities);
        var down = Capabilities.Quirks!["state.down"]!.Value;
        var up = Capabilities.Quirks["state.up"]!.Value;

        using var writer = XmlWriter.Create(output, new XmlWriterSettings
        {
            Indent = true,
            Encoding = Capabilities.Encoding,
            NewLineChars = Capabilities.NewLine,
        });

        writer.WriteStartElement("Macro");
        writer.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
        writer.WriteAttributeString("xmlns", "xsd", null, "http://www.w3.org/2001/XMLSchema");

        // Synapse truncates longer names in its own UI.
        var name = context.Meta.Title.Length > 20 ? context.Meta.Title[..20] : context.Meta.Title;
        writer.WriteElementString("Name", name);
        writer.WriteElementString("Guid", DeterministicGuid(context.Timeline.ScoreSha256).ToString("D"));

        writer.WriteStartElement("MacroEvents");
        foreach (var part in lowered.Parts)
        {
            foreach (var e in part.Events)
            {
                var key = context.Keys.Key(e.Event.Code);
                writer.WriteStartElement("MacroEvent");
                writer.WriteElementString("Type", "1");
                writer.WriteElementString("Delay", e.DelayBeforeMs.ToString(CultureInfo.InvariantCulture));
                writer.WriteStartElement("KeyEvent");
                writer.WriteElementString("Makecode", key.ScanCode1.ToString(CultureInfo.InvariantCulture));
                writer.WriteElementString("IsExtended", key.Extended ? "true" : "false");
                writer.WriteElementString("State", (e.Event.IsDown ? down : up).ToString(CultureInfo.InvariantCulture));
                writer.WriteEndElement();
                writer.WriteEndElement();
            }
        }

        writer.WriteEndElement();
        writer.WriteElementString("IsFolder", "false");
        writer.WriteElementString("FolderGuid", Guid.Empty.ToString("D"));
        writer.WriteEndElement();
        writer.Flush();

        return new ExportResult(true, lowered.Warnings, ["Synapse 3 -> Macros -> Import."], []);
    }

    /// <summary>Derived from the score hash so the same song always produces the same GUID - re-exporting must not spawn duplicates.</summary>
    private static Guid DeterministicGuid(string sha256)
    {
        Span<byte> bytes = stackalloc byte[16];
        for (var i = 0; i < 16; i++)
        {
            bytes[i] = System.Convert.ToByte(sha256.Substring((i * 2) % Math.Max(2, sha256.Length - 1), 2), 16);
        }

        return new Guid(bytes);
    }
}

/// <summary>
/// Bloody / A4Tech (双飞燕血手幽灵) .amc. Text, CRLF, UTF-8 with BOM, and it stores HID usage ids
/// literally - so for keyboard events the canonical identity is a direct cast rather than a
/// translation. The mouse encoding is inferred by analogy from a single observed LeftDown, which
/// is exactly the kind of inference this gate exists to refuse.
/// </summary>
public sealed class BloodyAmcExporter : SampleGatedExporter
{
    public override string Id => "bloody-amc";

    public override LocalizedText DisplayName => new("Bloody / A4Tech macro", "血手幽灵 / 双飞燕 宏");

    public override string FileExtension => ".amc";

    public override ExporterCapabilities Capabilities { get; } = new(
        SupportsMouse: true,
        VerifiedMouseButtons: [],
        TextEncoding: new UTF8Encoding(encoderShouldEmitUTF8Identifier: true),
        NewLine: "\r\n");

    protected override string MissingSampleRecipe =>
        "To unblock it: record a Bloody macro with right- and middle-button presses, and attach the .amc.";

    protected override ExportResult WriteVerified(ExportContext context, Stream output)
    {
        var lowered = MacroLowering.Lower(context.Timeline, Capabilities);
        var nl = Capabilities.NewLine;
        var builder = new StringBuilder();

        builder.Append("<KeyDown>").Append(nl).Append("<Syntax>").Append(nl);
        foreach (var part in lowered.Parts)
        {
            foreach (var e in part.Events)
            {
                if (e.DelayBeforeMs > 0)
                {
                    builder.Append("Delay ").Append(e.DelayBeforeMs.ToString(CultureInfo.InvariantCulture)).Append(" ms").Append(nl);
                }

                builder.Append(e.Event.IsDown ? "KeyDown " : "KeyUp ")
                    .Append(e.Event.Code.ToString(CultureInfo.InvariantCulture)).Append(" 1").Append(nl);
            }
        }

        builder.Append("</Syntax>").Append(nl).Append("</KeyDown>").Append(nl);

        // Every real sample carries an empty KeyUp block; release-all belongs exactly there.
        builder.Append("<KeyUp>").Append(nl).Append("<Syntax>").Append(nl).Append("</Syntax>").Append(nl).Append("</KeyUp>").Append(nl);

        var bytes = Capabilities.Encoding.GetBytes(builder.ToString());
        output.Write(bytes, 0, bytes.Length);

        return new ExportResult(
            true, lowered.Warnings,
            ["把 .amc 放进驱动的 GunLib 子目录，再在软件里选择它。"],
            [new ExtraFile(context.OutputStem + ".bmc", bytes, "byte-identical copy; some builds look for .bmc")]);
    }
}

/// <summary>
/// Redragon-family .MSMACRO. The single highest-leverage unknown in Branch B: this OEM codebase
/// is rebadged across several Chinese budget brands, so cracking its per-step delay encoding once
/// may unlock several vendors at the price of one investigation.
///
/// Blocked on a sample recorded with delays ENABLED. With DelayType=0 it can only emit evenly
/// spaced events, which is musically dead - so shipping it half-working would be worse than
/// refusing.
/// </summary>
public sealed class RedragonMsMacroExporter : SampleGatedExporter
{
    public override string Id => "redragon-msmacro";

    public override LocalizedText DisplayName => new("Redragon-family macro", "红龙 / 同源方案 宏");

    public override string FileExtension => ".MSMACRO";

    public override ExporterCapabilities Capabilities { get; } = new(
        SupportsMouse: true,
        VerifiedMouseButtons: [],
        Quirks: new Dictionary<string, int?>
        {
            ["map.down"] = 132,     // inferred, never observed
            ["map.up"] = 4,
            ["delayType.perStep"] = null,   // THE unknown that blocks this exporter
        });

    protected override string MissingSampleRecipe =>
        "To unblock it: record a macro with 「记录延时 / record delays」 ENABLED using the delays "
        + "137 / 251 / 61 ms, and attach the .MSMACRO plus its Config.ini. Because this software is "
        + "rebadged across several brands, one sample may unlock several vendors at once.";

    protected override ExportResult WriteVerified(ExportContext context, Stream output) =>
        ExportResult.Refused(
            "unknownDelayEncoding",
            "The per-step delay encoding for DelayType=1 has never been observed. " + MissingSampleRecipe);
}
