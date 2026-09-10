## Licence and the clean-room boundary

**MIT.** The one prior-art codebase we may borrow from is MIT (`sabihoshi/GenshinLyreMidiPlayer`, attributed in `NOTICE.md`), every dependency is MIT, and choosing copyleft for a clean-room project invites exactly the question "copyleft of what?" when the surrounding prior art is GPL-3.0/AGPL-3.0/LGPL-2.1. MIT also maximises the chance that someone who *has* a Windows box and the game forks it and fixes the input layer — which is precisely the help this project needs.

`docs/PROVENANCE.md` is maintained **from M1**, not written at M16, because a boundary document produced after all the code exists is retroactive rationalisation. It records, per derived idea: the public artefact it came from (README, published docs, observable behaviour, a file format), the depth of reading (README/docs level versus source level), and an explicit assertion that no source expression was transcribed. Specifically logged: BardMusicPlayer / LightAmp (GPL-3.0) — README and observable behaviour only; MidiBard2 (AGPL-3.0 with a per-file "must prominently credit akira0245" clause) — **treated as radioactive, not read**; clxTools (LGPL-2.1) — pass *concepts* derived from published pass names, with **our pass names renamed to project-native terms** so we do not manufacture the written record of a structural resemblance our own risk register calls a losing argument; `ChickenD233/harmonica-auto-player` (unlicensed) — the instrument mapping and the timing constants are **facts about a third-party program's observable behaviour**, cited as such with the exact source lines, and no code was copied. The PR template carries a clean-room checkbox. Algorithms and numeric constants derived from public documentation are facts; source expression never is.

## CI

`ci.yml` (ubuntu-24.04 + macos-26): restore, build with `TreatWarningsAsErrors`, all five test suites, the `GOLDEN-CHANGE:` trailer check, `Policy.Tests`. `windows.yml` (windows-2025, free for public repos): launch the macOS-cross-published exe; SendInput into a real Win32 window and assert the characters arrive; execute the generated `.ahk` with `Send` stubbed and diff against the timeline; `RegisterHotKey` pump thread; scheduler p99 deadline error. `release.yml`: tag-triggered, cross-publishes `win-x64` from ubuntu, emits both artefacts, computes SHA-256.

## Distribution

**Primary artefact: a plain, uncompressed, self-contained, NOT-single-file portable zip** (~40–50 MB). Compression is the biggest AV false-positive trigger and runtime self-extraction is the second, so the single-file variant is a clearly-labelled secondary link with `EnableCompressionInSingleFile=false`. No installer, no updater, no packer, no obfuscator, no anti-debug, no process-name randomisation — every one of those is simultaneously a detection heuristic and, under PRC Art. 285(3) analysis, a step toward the 繞過安全措施 element.

GitHub Releases is the hash-of-record, but the README's **primary link is a 蓝奏云 mirror** (no login, no client, direct download, comfortably under the 100 MB free-file limit) with a 123云盘 fallback, because GitHub is slow or unreachable for much of the audience. Discovery happens on Bilibili, which is where these users actually are — the instrument itself went viral there. SHA-256 published prominently so mirrors are independently verifiable. Gitee as a source mirror only, never as a release channel (review gates and repo locks).

## Antivirus

A .NET binary that calls `SendInput` in a loop is a textbook Defender / 360 / 火绒 HackTool heuristic. Countermeasures, in order: uncompressed zip first; no packers or obfuscation ever; **zero network I/O**, so a firewall check corroborates benignness in ten seconds and `Policy.Tests` enforces it as a first-party invariant; public source; SHA-256 + VirusTotal links with every release; submit to Microsoft's false-positive portal on first detection. **Plan for unsigned.** Azure Artifact Signing is restricted to US/CA/EU/UK business entities so the developer is very likely ineligible, and since 2024 even an EV certificate no longer bypasses SmartScreen on first download — so document the SmartScreen bypass in Chinese rather than budgeting for a certificate. macOS notarisation is skipped ($99/yr for a rounding-error audience); the `xattr` workaround is documented.

## The in-app disclaimer (blocking, first run, Chinese primary)

> ## ⚠️ 使用前请阅读
>
> 本工具会**模拟键盘 / 鼠标输入**（实时演奏模式），或**生成宏文件**供外设厂商软件播放（宏导出模式）。
>
> 腾讯《三角洲行动》官方《禁止 / 不建议使用的软硬件列表》**同时点名**「按键精灵、Autohotkey、键盘鼠标控制切换器、幽灵键鼠、大漠外挂程序、**python 等自动脚本**」与「**鼠标宏**」，Garena 版公告并列出「**硬件宏**」。
>
> **因此本工具的两种模式都在官方明令禁止的范围内。** 宏导出模式只是在**检测层面**更安静——它不是政策上的安全港。使用本工具可能导致封号，包括长期封禁与设备（机器码）封禁。
>
> 本工具**不做任何反检测处理**，也**不会**声称「防封」「过检测」「不会被发现」。作者不对任何账号损失负责。
>
> 本工具**不联网**：无遥测、无自动更新、无账号、无云端曲库。你可以用防火墙自行验证。
>
> 建议仅在**安全区 / 大厅**使用，**切勿在对局中使用**。
>
> 继续使用即表示你已理解并接受上述风险。
>
> `[ 我已阅读并接受 ]`   `[ 退出 ]`

A persistent one-line banner stays on the playback screen. The words **防封 / 过检测 / undetectable / 100% safe / 更安全 appear nowhere** in the product, README, release notes or commit history — that is a permanent commitment, not a default. Branch B's only permitted claim is architectural: 「导出的宏文件由厂商软件播放，本程序不与游戏进程发生任何交互」.

## The engineering charter, stated in the README and enforced where testable

No DLL injection. No memory reads or writes. No `OpenProcess` on the game. No kernel component or filter driver. No `WH_KEYBOARD_LL` hook. No elevation by default. No overlay on the game window. No screen capture. No network I/O. No obfuscation. No combat-adjacent feature, ever — a single one reclassifies the entire tool. Described as 「MIDI 转简谱 / 宏 工具」, never as 外挂 / 辅助 / 脚本. Free, non-commercial, no 卡密, no donation-gated features, no ads.

## Copyright posture

Bring-your-own-file, local-only, zero upload, zero bundled song library, zero sharing feature, zero cloud. Public-domain demo content only, with `testdata/README.md` documenting the rule so a contributor does not helpfully add 起风了.mid. **Permanently resist a community score repository** — it is the single feature that converts a neutral converter into a distribution platform *and* supplies the popularity that gets a per-tool signature written.

## The competitive posture

An incumbent already ships at v1.0.5 with a real in-game user. Do not race him on Branch A — reach parity and stop. Compete on the four things his position makes structurally hard: an unlicensed single-purpose repo cannot easily become bilingual, MIT-licensed, data-driven and *explainable*, and it cannot exploit the 18 redundant renderings with a flat one-key-per-pitch map. Link to his tool in the README and be openly complementary; in a ten-star niche a cooperative second entrant gets adoption that a hostile one does not, and his unanswered issue 「在线对局中使用真的会封号吗？」 is a question we can answer honestly and thereby earn attention.