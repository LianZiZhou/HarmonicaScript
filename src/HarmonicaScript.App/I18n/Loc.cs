using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HarmonicaScript.App.I18n;

/// <summary>
/// Runtime-switchable localisation.
///
/// Bound as <c>{Binding [Play_Start], Source={x:Static i18n:Loc.Instance}}</c>. It has to be an
/// indexer on an observable object rather than <c>{x:Static}</c>, because Avalonia's own docs are
/// explicit that static bindings do NOT refresh when the culture changes - and a language toggle
/// that needs a restart is a language toggle nobody uses.
///
/// The strings live in code rather than resx on purpose: satellite assemblies add publish and
/// trimming complexity for a two-language app, and a plain dictionary gives the key-parity test
/// (see LocalisationTests) something concrete to assert. The requirement that matters - never
/// ship a half-translated release - is met either way.
/// </summary>
public sealed partial class Loc : ObservableObject
{
    private static readonly Dictionary<string, string> En = new()
    {
        ["App_Title"] = "HarmonicaScript",
        ["App_Unverified"] = "NOT verified in game",
        ["App_UnverifiedTip"] = "This instrument profile was derived from a third-party tool and screenshots. Nobody has confirmed it against the running game. See docs/CALIBRATION.md.",
        ["Nav_Import"] = "File and tracks",
        ["Nav_Settings"] = "Conversion",
        ["Nav_Report"] = "Playability",
        ["Nav_Editor"] = "Notes",
        ["Nav_Rehearse"] = "Rehearse",
        ["Nav_Export"] = "Export",
        ["Nav_Play"] = "Play",
        ["Nav_Diagnose"] = "Self-test",
        ["Import_Drop"] = "Drop a MIDI file here, or click to browse",
        ["Import_Open"] = "Open MIDI...",
        ["Track_Name"] = "Track",
        ["Track_Notes"] = "Notes",
        ["Track_Duration"] = "Duration",
        ["Track_Range"] = "Range",
        ["Track_Polyphony"] = "Polyphony",
        ["Track_Score"] = "Melody score",
        ["Track_Percussion"] = "percussion",
        ["Settings_Transpose"] = "Transpose",
        ["Settings_TransposeMode"] = "Transposition mode",
        ["Settings_Speed"] = "Speed",
        ["Settings_Reduction"] = "When notes overlap, keep",
        ["Settings_OutOfRange"] = "Out of range",
        ["Settings_Timing"] = "Timing profile",
        ["Settings_TimingUnmeasured"] = "this value has never been measured in the game",
        ["Report_Grade"] = "Grade",
        ["Report_Kept"] = "notes kept",
        ["Report_Dropped"] = "dropped",
        ["Report_Folded"] = "octave-folded",
        ["Report_Merged"] = "merged",
        ["Report_Shifted"] = "shifted",
        ["Report_Candidates"] = "Transposition candidates",
        ["Report_Ceilings"] = "Speed ceilings (notes per second)",
        ["Report_Effective"] = "Effective ceiling",
        ["Report_OnlyProblems"] = "Only show notes with problems",
        ["Editor_Time"] = "Time",
        ["Editor_Degree"] = "Degree",
        ["Editor_Sharp"] = "#",
        ["Editor_Octave"] = "Octave",
        ["Editor_Key"] = "Key",
        ["Editor_Modifiers"] = "Modifiers",
        ["Editor_Source"] = "Written",
        ["Editor_Delta"] = "Delta",
        ["Editor_Flags"] = "Flags",
        ["Editor_Mute"] = "Mute",
        ["Export_Target"] = "Target",
        ["Export_Write"] = "Export...",
        ["Export_Refused"] = "This target refuses to write a file",
        ["Play_Start"] = "Start (F5)",
        ["Play_Stop"] = "Stop (F7)",
        ["Play_NoSound"] = "No sound?",
        ["Play_Diagnostics"] = "Generate diagnostic bundle",
        ["Play_Banner"] = "Simulated input is prohibited by the game's official rules. Use at your own risk.",
        ["Common_Language"] = "中文",
        ["Common_Close"] = "Close",
        ["Common_Accept"] = "I have read and accept",
        ["Common_Quit"] = "Quit",
        ["Disclaimer_Title"] = "Read before use",
    };

    private static readonly Dictionary<string, string> ZhHans = new()
    {
        ["App_Title"] = "HarmonicaScript",
        ["App_Unverified"] = "未经实机验证",
        ["App_UnverifiedTip"] = "本乐器配置来自第三方工具与截图推断，没有人在运行中的游戏里确认过。见 docs/CALIBRATION.md。",
        ["Nav_Import"] = "文件与音轨",
        ["Nav_Settings"] = "转换设置",
        ["Nav_Report"] = "演奏性报告",
        ["Nav_Editor"] = "音符",
        ["Nav_Rehearse"] = "排练",
        ["Nav_Export"] = "导出",
        ["Nav_Play"] = "演奏",
        ["Nav_Diagnose"] = "自检",
        ["Import_Drop"] = "把 MIDI 文件拖到这里，或点击浏览",
        ["Import_Open"] = "打开 MIDI...",
        ["Track_Name"] = "音轨",
        ["Track_Notes"] = "音符数",
        ["Track_Duration"] = "时长",
        ["Track_Range"] = "音域",
        ["Track_Polyphony"] = "平均复音",
        ["Track_Score"] = "旋律度",
        ["Track_Percussion"] = "打击乐",
        ["Settings_Transpose"] = "移调",
        ["Settings_TransposeMode"] = "移调模式",
        ["Settings_Speed"] = "速度",
        ["Settings_Reduction"] = "重叠时保留",
        ["Settings_OutOfRange"] = "超出音域时",
        ["Settings_Timing"] = "时序配置",
        ["Settings_TimingUnmeasured"] = "此数值未经实测",
        ["Report_Grade"] = "评级",
        ["Report_Kept"] = "保留音符",
        ["Report_Dropped"] = "丢音",
        ["Report_Folded"] = "八度折叠",
        ["Report_Merged"] = "合并",
        ["Report_Shifted"] = "位移",
        ["Report_Candidates"] = "移调候选",
        ["Report_Ceilings"] = "速度上限（音符/秒）",
        ["Report_Effective"] = "实际需要清过的上限",
        ["Report_OnlyProblems"] = "只看有问题的音",
        ["Editor_Time"] = "时间",
        ["Editor_Degree"] = "度数",
        ["Editor_Sharp"] = "#",
        ["Editor_Octave"] = "八度",
        ["Editor_Key"] = "按键",
        ["Editor_Modifiers"] = "修饰键",
        ["Editor_Source"] = "原音",
        ["Editor_Delta"] = "偏移",
        ["Editor_Flags"] = "标记",
        ["Editor_Mute"] = "静音",
        ["Export_Target"] = "导出目标",
        ["Export_Write"] = "导出...",
        ["Export_Refused"] = "该目标拒绝生成文件",
        ["Play_Start"] = "开始 (F5)",
        ["Play_Stop"] = "停止 (F7)",
        ["Play_NoSound"] = "没有声音？",
        ["Play_Diagnostics"] = "生成诊断包",
        ["Play_Banner"] = "模拟输入在游戏官方规则中属于禁止范围，风险自负。",
        ["Common_Language"] = "EN",
        ["Common_Close"] = "关闭",
        ["Common_Accept"] = "我已阅读并接受",
        ["Common_Quit"] = "退出",
        ["Disclaimer_Title"] = "使用前请阅读",
    };

    private Dictionary<string, string> _active = DetectCulture();

    public static Loc Instance { get; } = new();

    /// <summary>Every key in the neutral (English) table. The parity test iterates this.</summary>
    public static IReadOnlyCollection<string> Keys => En.Keys;

    public static IReadOnlyDictionary<string, string> EnglishTable => En;

    public static IReadOnlyDictionary<string, string> ChineseTable => ZhHans;

    public bool IsChinese => ReferenceEquals(_active, ZhHans);

    /// <summary>Missing keys surface as [Key] rather than empty space, so a gap is obvious in a screenshot.</summary>
    public string this[string key] => _active.TryGetValue(key, out var value) ? value : $"[{key}]";

    public void Toggle() => SetCulture(IsChinese ? "en" : "zh-Hans");

    public void SetCulture(string culture)
    {
        _active = culture.StartsWith("zh", System.StringComparison.OrdinalIgnoreCase) ? ZhHans : En;

        // Refreshes every indexer binding at once. Without this the toggle silently does nothing.
        OnPropertyChanged("Item[]");
        OnPropertyChanged(nameof(IsChinese));
    }

    /// <summary>
    /// Follows the system locale. zh-CN, zh-SG and zh-MY all fall back to zh-Hans through .NET's
    /// own chain, which is why the table is keyed on the script rather than the region.
    /// </summary>
    private static Dictionary<string, string> DetectCulture() =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("zh", System.StringComparison.OrdinalIgnoreCase)
            ? ZhHans
            : En;
}
