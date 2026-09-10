namespace HarmonicaScript.Playback;

public enum RungVerdict
{
    Pass,
    Warn,
    Fail,

    /// <summary>Cannot be determined from outside the game. Said plainly rather than guessed.</summary>
    Unknown,
}

/// <summary>One diagnosis step, with its verdict and the evidence behind it.</summary>
public sealed record PreflightRung(int Number, string Code, RungVerdict Verdict, string EvidenceEn, string EvidenceZh);

/// <summary>
/// "Nothing happened" has FIVE distinct causes and ONE identical symptom. This is the single
/// most likely support burden in the project, so it is designed rather than left to a README.
///
/// A PURE FUNCTION from facts to verdicts. That is the whole point: the probe does platform I/O,
/// this does judgement, and so the only piece of user-facing decision logic in the project can be
/// golden-filed against synthetic fact sets on a Mac with no game and no Windows.
/// </summary>
public static class PreflightRules
{
    public static IReadOnlyList<PreflightRung> Evaluate(EnvironmentFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        var rungs = new List<PreflightRung>();

        // Rung 0 - does the whole chain reach a real foreign process at all?
        rungs.Add(facts.NotepadDeliverySucceeded switch
        {
            true => new PreflightRung(0, "delivery", RungVerdict.Pass,
                "Injected input reached Notepad and read back correctly.",
                "模拟输入成功送达记事本并原样读回。"),
            false => new PreflightRung(0, "delivery", RungVerdict.Fail,
                $"Notepad read back '{facts.NotepadReadback}' instead of the expected nonce. "
                + "Garbled text usually means an IME is in conversion mode.",
                $"记事本读回的是「{facts.NotepadReadback}」而不是预期内容。乱码通常说明输入法处于中文输入状态。"),
            null => new PreflightRung(0, "delivery", RungVerdict.Unknown,
                "The Notepad delivery test has not been run.",
                "尚未运行记事本投递测试。"),
        });

        rungs.Add(facts.GameProcessFound
            ? facts.GameWindowFocused
                ? new PreflightRung(1, "focus", RungVerdict.Pass, "The game window is focused.", "游戏窗口处于前台。")
                : new PreflightRung(1, "focus", RungVerdict.Fail,
                    "The game is running but is NOT focused. Input would go to whatever is.",
                    "游戏在运行但不在前台。现在演奏会把按键打到别的窗口里。")
            : new PreflightRung(1, "focus", RungVerdict.Fail,
                "No game process was found.", "没有找到游戏进程。"));

        rungs.Add(facts.ExclusiveFullscreen
            ? new PreflightRung(2, "fullscreen", RungVerdict.Fail,
                "The foreground application is in exclusive fullscreen, which drops injected input. Switch to borderless windowed.",
                "前台程序处于独占全屏，会丢弃模拟输入。请切换到无边框窗口模式。")
            : new PreflightRung(2, "fullscreen", RungVerdict.Pass, "Not in exclusive fullscreen.", "不是独占全屏。"));

        rungs.Add((facts.ForegroundKeyboardLayout & 0xFFFF) == 0x0804
            ? new PreflightRung(3, "ime", RungVerdict.Warn,
                "The foreground window's keyboard layout is Chinese (Simplified). If the IME is in conversion mode it will eat the note keys.",
                "前台窗口的键盘布局是简体中文。如果输入法处于中文输入状态，音符按键会被输入法吃掉。")
            : new PreflightRung(3, "ime", RungVerdict.Pass, "Keyboard layout looks fine.", "键盘布局正常。"));

        // Rung 4 - and the correction that matters: a GREEN rung 0 does NOT clear this.
        // Notepad runs at medium integrity. If the game is elevated, the Notepad test passes and
        // the elevation problem is still there, so the rules must keep saying so.
        rungs.Add(new PreflightRung(4, "uipi", RungVerdict.Unknown,
            facts.WeAreElevated
                ? "We are running elevated, so UIPI will not block us."
                : "We are NOT elevated. If the game runs elevated, Windows silently discards our input and reports no error. "
                + "A passing Notepad test does NOT rule this out - Notepad is medium integrity.",
            facts.WeAreElevated
                ? "本程序以管理员身份运行，不会被 UIPI 拦截。"
                : "本程序不是管理员身份。如果游戏以管理员身份运行，Windows 会静默丢弃我们的输入且不报错。"
                + "记事本测试通过也不能排除这一项 —— 记事本是中完整性级别。"));

        rungs.Add(new PreflightRung(5, "binding", RungVerdict.Unknown,
            "Whether the in-game keys are still Z X C V B N M , and whether the instrument is equipped cannot be observed from outside. "
            + "Use the binding-capture wizard if the notes come out wrong.",
            "游戏内是否仍然是 Z X C V B N M ，以及是否已装备乐器，从外部无法观测。如果音不对，请用按键捕获向导重新绑定。"));

        return rungs;
    }

    /// <summary>The honest summary. Rung 6 - "all green and still nothing" - is stated as itself, never as a generic failure.</summary>
    public static string Summarise(IReadOnlyList<PreflightRung> rungs)
    {
        ArgumentNullException.ThrowIfNull(rungs);

        var failures = rungs.Where(r => r.Verdict == RungVerdict.Fail).ToList();
        if (failures.Count > 0)
        {
            return $"blocked: {string.Join(", ", failures.Select(f => f.Code))}";
        }

        var unknowns = rungs.Where(r => r.Verdict == RungVerdict.Unknown).Select(r => r.Code).ToList();
        return unknowns.Count > 0
            ? $"no blocker found; {unknowns.Count} item(s) cannot be checked from outside the game ({string.Join(", ", unknowns)}). "
              + "If nothing happens with all of these clear, the game itself may be filtering injected input - "
              + "generate a diagnostic bundle and report it."
            : "all checks pass";
    }
}
