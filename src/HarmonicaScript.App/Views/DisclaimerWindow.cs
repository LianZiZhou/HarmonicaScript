using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace HarmonicaScript.App.Views;

/// <summary>
/// The blocking first-run disclaimer.
///
/// Its wording is load-bearing and was checked against primary sources: Tencent's official
/// prohibited-software list names BOTH 「Autohotkey、python 等自动脚本」AND「鼠标宏」, and the
/// Garena announcement adds「硬件宏」. So BOTH of this tool's modes fall inside the prohibition,
/// and macro export is quieter in DETECTION terms only - it is not a policy safe harbour.
///
/// Saying anything softer would be a false claim to a user about their own account. The words
/// 防封 / 过检测 / undetectable / 100% safe / 更安全 appear nowhere in this product, and a Policy
/// test enforces that permanently rather than trusting anyone to remember.
/// </summary>
public sealed class DisclaimerWindow : Window
{
    private const string ConsentFileName = "accepted-disclaimer.v1";

    public DisclaimerWindow()
    {
        Title = "HarmonicaScript";
        Width = 700;
        Height = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;

        // Only an explicit choice dismisses it.
        Closing += (_, e) =>
        {
            if (!Accepted)
            {
                e.Cancel = true;
            }
        };

        var accept = new Button
        {
            Content = "我已阅读并接受 / I have read and accept",
            HorizontalAlignment = HorizontalAlignment.Right,
        };

        accept.Click += (_, _) =>
        {
            Accepted = true;
            Remember();
            Close();
        };

        var quit = new Button { Content = "退出 / Quit", HorizontalAlignment = HorizontalAlignment.Right };
        quit.Click += (_, _) => Environment.Exit(0);

        Content = new DockPanel
        {
            Margin = new Thickness(22),
            Children =
            {
                Header(),
                Buttons(accept, quit),
                Body(),
            },
        };
    }

    public bool Accepted { get; private set; }

    /// <summary>True once the user has accepted on this machine.</summary>
    public static bool AlreadyAccepted => File.Exists(ConsentPath);

    private static string ConsentPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HarmonicaScript",
        ConsentFileName);

    private static Control Header()
    {
        var header = new TextBlock
        {
            Text = "⚠  使用前请阅读 / Read before use",
            FontSize = 20,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 14),
        };

        DockPanel.SetDock(header, Dock.Top);
        return header;
    }

    private static Control Buttons(Button accept, Button quit)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
            Children = { quit, accept },
        };

        DockPanel.SetDock(row, Dock.Bottom);
        return row;
    }

    private static Control Body() => new ScrollViewer
    {
        Content = new SelectableTextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 22,
            Text = Text,
        },
    };

    private static void Remember()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConsentPath)!);

            // Local file only. This program makes no network connection of any kind, and a
            // Policy test asserts that across every assembly.
            File.WriteAllText(ConsentPath, DateTime.UtcNow.ToString("O"));
        }
        catch (IOException)
        {
            // Not being able to remember the choice is not a reason to block the user.
        }
    }

    private const string Text = """
        本工具会模拟键盘 / 鼠标输入（实时演奏模式），或生成宏文件供外设厂商软件播放（宏导出模式）。

        腾讯《三角洲行动》官方《禁止 / 不建议使用的软硬件列表》同时点名「按键精灵、Autohotkey、
        键盘鼠标控制切换器、幽灵键鼠、大漠外挂程序、python 等自动脚本」与「鼠标宏」，Garena 版
        公告并列出「硬件宏」。

        因此本工具的两种模式都在官方明令禁止的范围内。宏导出模式只是在检测层面更安静 ——
        它不是政策上的安全港。使用本工具可能导致封号，包括长期封禁与设备（机器码）封禁。

        本工具不做任何反检测处理，也不会声称「防封」「过检测」「不会被发现」。作者不对任何
        账号损失负责。

        本工具不联网：无遥测、无自动更新、无账号、无云端曲库。你可以用防火墙自行验证。

        另外请注意：本程序的乐器映射与全部时序参数都尚未在运行中的游戏里验证过 —— 它们来自
        第三方工具的源码与游戏截图的推断。所有数值都是带来源标注的 JSON 数据，改一个字段即可
        修正，不需要重新编译。详见 docs/CALIBRATION.md。

        建议仅在安全区 / 大厅使用，切勿在对局中使用。

        ──────────────────────────────────────────────────────────────

        This tool simulates keyboard and mouse input, or generates macro files for peripheral
        vendor software to play back.

        Tencent's official prohibited-software list for Delta Force names BOTH automation scripts
        (AutoHotkey, python) AND mouse macros; the Garena announcement also lists hardware macros.

        BOTH of this tool's modes therefore fall inside that prohibition. Macro export is quieter
        in DETECTION terms only - it is not a policy safe harbour. Using this tool may get your
        account banned, including long-term and hardware-ID bans.

        This tool does nothing to evade detection and makes no claim of being undetectable.
        The author is not responsible for any account loss.

        This tool makes no network connection of any kind: no telemetry, no auto-update, no
        account, no cloud song library. You are welcome to verify that with a firewall.

        Also note: the instrument mapping and every timing constant have NOT been verified against
        the running game. See docs/CALIBRATION.md.

        Use it in a safe zone or lobby. Do not use it during a live match.
        """;
}
