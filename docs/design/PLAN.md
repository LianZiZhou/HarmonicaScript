# HarmonicaScript — MIDI → 三角洲行动口琴 转谱 / 演奏 / 宏导出

## Context

把社区流通的单轨 MIDI（FF14 吟游诗人 / 原神风物之诗琴那一类）转换成《三角洲行动》游戏内口琴「守夜人的口风琴」可演奏的乐谱，再二选一输出：**分支 A** 实时模拟键鼠输入直接演奏，**分支 B** 导出成键鼠厂商软件可导入的宏文件。

`.` 目前是**空目录、非 git 仓库**，完全从零开始。

调研（10 个 agent，含一轮事实核查）+ 设计（3 套独立架构 × 4 维评审 + 综合）得出的三个结论决定了整个计划的形状：

1. **这件乐器是完全半音阶的。** 8 个音级 + 半音修饰键让每个八度带覆盖 14 个连续半音，三个带重叠，总共 38 个半音无空洞。这意味着原神/FF14 工具的核心难题「离调音怎么办」在这里**结构性地不存在**（`#3≡4`、`#7≡1̇`）。真正的难题只剩两个，而且它们是**耦合的**：选哪个移调、以及怎么排修饰键。这就是本项目相对已有竞品的全部技术差异 —— 竞品用的都是扁平的「一个音高对应一个键」映射表。
2. **你没有 Windows 机器，且 ACE 禁用虚拟机。** SendInput 能否到达游戏、游戏的实际时序容忍度、任何厂商宏文件能否导入 —— 这三件事你**永远无法自己验证**。所以架构的组织原则是：把不可验证面压到最薄，把它整个推进 JSON 数据里（改一个字段就能修，不用重新编译分发），剩下 90% 全部在 Mac 上用金文件证明。
3. **两条分支在官方规则上都违规。** 腾讯《禁止/不建议使用的软硬件列表》与 Garena 台服公告逐字点名了「Autohotkey、python 等自动脚本」**和**「鼠标宏 / 硬件宏」。游戏用内核级 ACE。硬件宏只是**检测层面更安静**，不是政策安全港 —— 应用内提示必须这么写，不能暗示"更安全"。

---

## 一、已确定的事实

### 1.1 乐器规格（来源：竞品 `ChickenD233/harmonica-auto-player` 源码 + 你的截图 + 你本人确认修饰键为 hold）

```
键盘   Z  X  C  V  B  N  M  ,          半音偏移 0 2 4 5 7 9 11 12   (简谱 1 2 3 4 5 6 7 1̇)
鼠标左键 按住 = 降调 = 整排 −12
鼠标中键 按住 = 半音 = 整排 +1
鼠标右键 按住 = 升调 = 整排 +12
左/右互斥（永不同时发出）；中键与左右均可叠加

pitch = P0 + 12·shift + degree[key] + (sharp ? 1 : 0),  shift ∈ {−1,0,+1}, sharp ∈ {0,1}
音域   P0−12 … P0+25 = 38 个半音   (P0+24 = ','+右键；P0+25 = ','+右键+中键)
```

手工穷举验证过的结构性事实（会写成钉死的 fixture 测试）：**6 个修饰键状态；38 个偏移量零空洞；48 种发音组合；重数分布 {1:30, 2:6, 3:2}；两路偏移恰好是 {−7,+1,+5,+13,+17,+24}，三路恰好是 {0,+12}**。推论：**在固定状态下最多一个键能发出某个偏移量** —— 所以「交替用不同键来规避同键重触发」是不成立的，任何替代指法都必然要付一次修饰键切换。这条推论杀掉了一个看起来很诱人的伪优化。

### 1.2 环境（今天 2026-09-10 实测）

| 项 | 值 | 说明 |
|---|---|---|
| 本机 SDK | 8.0.414 / 9.0.305 | **两个都在 2026-11-10 停止支持**（两个月后） |
| 目标 | **.NET 10 LTS**，SDK 10.0.401，EOL 2028-11-14 | 尚未安装，是 M0 的第一件事 |
| UI | **Avalonia 11.3.21** | 见决策 D1 |
| MIDI | `Melanchall.DryWetMidi 8.0.3` | MIT，零依赖；9.0.0 仍是 prerelease |
| 不可用 | `Avalonia.Controls.TreeDataGrid` | 付费组件，硬依赖 `AvaloniaUI.Licensing` |
| 不可用 | `SharpHook` | 内置 libuiohook 是 GPL-3.0/LGPL-3.0，会污染 MIT |

---

## 二、决策清单

用户已拍板：Avalonia GUI + 核心库 + CLI ｜ Windows 完整、mac/Linux 仅演示 ｜ 中英双语 ｜ 两条分支都做 + 应用内风险提示 ｜ 无 Windows 机器 ｜ 完整编辑器 GUI ｜ 开源发布 ｜ 通用导出器优先 ｜ **完整跑 M0–M15 不设底线** ｜ **默认时序用 `df.reference`** ｜ **不做 alpha 阶段**（一次做完，最后发 v1.0）。发布渠道/身份由你自己处理，计划里不涉及。

以下是设计综合体解掉的技术分歧，每条都是"选了 X，因为 Y"：

**D1 · Avalonia 11.3.21 而不是 12.1.2。** net10 是被迫的（本机 SDK 两个月后全部 EOL），但 Avalonia 12 **没有免费 DevTools**（`Avalonia.Diagnostics` 版本列表止于 11.3.21，无 12.x），且 12 有蓄意的破坏性改动 —— 几乎所有教程和中文博客都是写给 11.x 的。11.3.21 面向 `netstandard2.0/net6.0/net8.0`，在 net10.0 上跑没有问题。迁移 12 的触发条件写进 M15：`Avalonia.Diagnostics` 出 12.x 之后再评估。

**D2 · DryWetMidi 只当文件解析器**，加 `ExcludeAssets="build"`。不用 `Playback`、不实现 `IOutputDevice`、不碰 `Multimedia` 命名空间（用 `BannedApiAnalyzers` 强制）。理由：`build/*.targets` 会无条件把三个原生 blob 拷进每个平台的输出目录，而我们只需要 SMF 读取。但**不自己手写 SMF 解析器** —— 它的容错 `ReadingSettings`、`Sanitize`、GB18030 解码、多段 tempo map 是几千小时的真实脏文件积累，重写就是重新踩一遍坑。

**D3 · IR 时间单位 = 绝对 int32 毫秒。** 每一个下游消费者无一例外都是整数毫秒（Razer `<Delay>`、Bloody `Delay n ms`、AHK `Sleep`、SendInput 调度）。微秒只活在核心流水线内部，`emit.quantise` 是**唯一**一个取整点。

**D4 · 规范键标识 = USB HID Usage ID（page 0x07）**，`keytable.json` 里带 PS/2 Set-1 + 扩展标志作为强制第二列。因为 Bloody `.amc` 和 Redragon `.MSMACRO` 字面存 HID，而 Windows SendInput 和 Razer 要 Set-1 —— 两边都要，HID 做主键。

**D5 · 移调目标函数是分层字典序，不是加权和。** 四层：`hardInfeasible`（计数，绝不归一化）→ `musicalLoss`（ppm，0.5% 容差）→ `keyDistance`（`|signedMod12(t)|, |t|`）→ `mech`（机械代价）。**这一条修掉了加权和的四个致命缺陷**：`t=−1` 且全程按住半音与 `t=0` 音高完全相同，加权和会被机械代价骗到前者，分层比较在第三层就把它挡掉了；硬不可行永远不会被一个八度折叠换掉；长曲子不会因为归一化而让不可行"变便宜"。

**D6 · 不做移调候选预筛。** 完整扫 60–120 个候选一共约 860 万次廉价整数运算，几十毫秒。预筛不但省不了什么，还筛错了轴 —— 超范围数在相邻 `t` 上几乎是平的，而修饰键churn 是尖峰的，最优候选可能在打分前就被扔了。

**D7 · DP 的可用静默时间取悲观值** `max(0, interOnset − NoteHoldMinMs)`，**不含**调度器的挪动预算。这让 DP 的可行性判断成为**可靠下界**：它认为可行的边，一定能靠单独截短前一个音实现，这是纯局部操作、不会级联。于是**一遍过，不需要迭代，不需要收敛假设**。

**D8 · 全部代价用 int64 微单位，优化器里没有浮点。** 让暴力等价性测试变成精确整数比较而不是脆弱的浮点比较，让打平判定确定化，让金文件稳定。用 Cecil 测试强制。

**D9 · 保持长度上限（hold cap）是 `InputTimeline` 的编译期属性，不是调度器行为。** `emit.holdCaps` 把超过 4000ms 的按键保持、超过 6000ms 的修饰键保持在休止处切开（找不到就一起切开发声音符，保证模拟器的"音高在整个音符跨度内恒定"断言仍成立）。因为它是序列化产物的属性，所以它能被校验、被金文件化、被 Mac 上的模拟器验证 —— 而且对我们运行时管不着的导出宏同样成立。

**D10 · `TimelineValidator` 是逻辑状态机，不是事件记账。** toggle 降解下"按一次"是 down+up，一个把半音**留在游戏里锁定**的时间线能通过所有 hold 形状的不变量（配对、无重复按下、结尾无按住、互斥、最小保持、最小间隔、有序）。所以校验器重放降解自身的语义，断言**逻辑**状态：终态必须是 Neutral、互斥组永不同时激活、**每个 NoteDown 依赖的修饰键必须在 `T − PressLeadMs` 之前就已激活并持续到 `T+ε`**（这条 lead-time 不变量是三个方案里两个都漏掉的）。

**D11 · 不提权，不 OpenProcess 游戏进程。** 常驻提权既是检测启发式又是法律上的加重情节；读游戏完整性级别恰好是 ACE 公开宣称会拦截的跨进程行为，而信息收益不值这个代价。改为：`asInvoker` manifest，只在自检向导里、由用户显式点击才提供「以管理员身份重启」。

**D12 · 应用永不发起任何出站网络连接。** 无遥测、无更新检查、无崩溃上报。用 `Policy.Tests` 当作一等不变量强制。这既是隐私姿态，也是一个十秒钟就能自证清白的反 AV 误报手段。

**D13 · AutoHotkey v2 导出器排在 Windows SendInput 后端**之前**。** 它通过一个几千人验证过的成熟注入引擎到达游戏 —— 等于是"去掉了开发者不可验证面的分支 A"。时间线发射器一能跑，它就能做。

**D14 · MIT 许可证 + `docs/PROVENANCE.md` 从 M1 就开始维护**（不是 M15 才补写，事后补的边界文档是追认式合理化）。逐条记录：BardMusicPlayer/LightAmp（GPL-3.0）—— 只读 README 和可观察行为；MidiBard2（AGPL-3.0）—— **视为放射性，不读**；clxTools（LGPL-2.1）—— 只从公开的 pass 名称派生概念，且**我们的 pass 名全部改成项目自有术语**；`ChickenD233/harmonica-auto-player`（无许可证）—— 乐器映射和时序常量是**关于第三方程序可观察行为的事实**，注明确切来源行，未复制任何代码。PR 模板带 clean-room 勾选框。

**D15 · 不做导出器模板 DSL，不做 drop-in 插件目录。** 它恰好会给"谁都没见过的那些导出器"发放豁免所有验证手段的通行证，还给一个本来就会被 AV 启发式盯上的工具加了一条"从可写目录加载任意配置"的路径。新厂商的瓶颈是**拿到样本**，不是写代码 —— 一个面向行的 writer 只有 80–150 行。

---

## 三、解决方案布局

```

  HarmonicaScript.slnx
  global.json                { "sdk": { "version": "10.0.100", "rollForward": "latestFeature" } }
  Directory.Build.props      net10.0 · Nullable · TreatWarningsAsErrors · Deterministic
                             ContinuousIntegrationBuild · InvariantGlobalization=false
  Directory.Packages.props   中央包管理，所有 pin 在一个文件里
  .editorconfig  .gitattributes  nuget.config  LICENSE(MIT)  NOTICE.md
  docs/PROVENANCE.md         clean-room 记录（M1 起维护）
  docs/CALIBRATION.md        六个游戏内实验（给任何有 Windows 的人执行）
  .github/workflows/{ci.yml,windows.yml,release.yml}
  .github/ISSUE_TEMPLATE/{no-sound.yml,vendor-sample.yml,profile-correction.yml,calibration-result.yml}
```

### src/ — 11 个工程，每条边界都是包隔离或平台边界

| 工程 | 职责 | 禁止（由 `Policy.Tests` + Cecil 强制） |
|---|---|---|
| `HarmonicaScript.Core` | `SourceSong`/`HarmonicaScore`/`InputTimeline`；`InstrumentProfile` 模型；`EmissionTable`；pass 流水线；`TranspositionSearch`；`ModifierPlanner`(DP)；`ArticulationScheduler`；`TimelineLowering`；`HoldCapPass`；`TimelineValidator`；`InstrumentSimulator`；`ConversionReport`。**零包引用、零 I/O** | `System.IO`、`System.Net.*`、Avalonia、DryWetMidi、任何 P/Invoke |
| `HarmonicaScript.Profiles` | JSON schema + STJ 源生成上下文；内嵌默认值；**四级覆盖链**（内嵌 → AppDir/data → `%LOCALAPPDATA%` → `.hsproj` 快照）带每字段来源；手写 `IProfileMigration`；`KeyTable`；`GameProfile` | 随游戏变化的**逻辑** |
| `HarmonicaScript.Midi` | **唯一**引用 DryWetMidi 的地方；容错读取 → `SourceSong`；`.reduced.mid` 写出 | `Melanchall.DryWetMidi.Multimedia`（禁用符号） |
| `HarmonicaScript.Export` | `IMacroExporter`、`ExporterCapabilities`、共享降解链、通用 writer、厂商 writer | 从 `HarmonicaScore` 重新推导任何东西 |
| `HarmonicaScript.Playback` | `IInputBackend`、`IClock`、`IEnvironmentProbe`、`Scheduler`、`TraceWriter`/`TraceReader`（**唯一规范写入器**）、`ReleaseAllGuard`、`PreflightRules`、`TraceBackend`、`VirtualClock`、`FaultInjectingBackend` | 平台 API |
| `HarmonicaScript.Playback.Windows` | `ISendInputApi` + CsWin32 实现、`WindowsSendInputBackend`、`WindowsEnvironmentProbe`、`WindowsHotkeys`、`NotepadDeliveryTest`。目标 `net10.0` + `[SupportedOSPlatform("windows")]`，**不是 `net10.0-windows`** —— 这样整个解决方案在 Mac 上仍可编译可测 | — |
| `HarmonicaScript.Playback.MacOs` | `MacCgEventBackend`、TextEdit 回环。仅开发/演示，永不对外宣传 | — |
| `HarmonicaScript.Project` | `.hsproj` ZIP、`ScoreEdit` 日志、打开时的配置快照差异比对 | — |
| `HarmonicaScript.Audition` | `SimulatedNote[]` → MeltySynth → WAV，**离线渲染** | 任何音频设备 API |
| `HarmonicaScript.Cli` | `hsc` | — |
| `HarmonicaScript.App` | Avalonia 外壳、MVVM、i18n | 任何算法；任何 DryWetMidi 类型 |

`tests/`：`Core.Tests`、`Midi.Tests`、`Export.Tests`、`Playback.Tests`、`Policy.Tests`。

`data/`（exe 旁边的散文件 **+** 内嵌兜底）：`profiles/df.harmonica.v1.json`、`profiles/loopback.text.v1.json`、`games/df.game.json`、`timing/{df.reference,df.conservative,df.humanised}.json`、`keytable.json`、`exporters/*.capabilities.json`、`diagnostics/rules.json`、`weights/objective.v1.json`。

`testdata/`：`pd/*.mid`（25–30 个**公有领域**文件，提交进仓库）、`generated/`（程序化 fixture 生成器：代码 + 固定种子）、`vendor-samples/` + `PROVENANCE.md`、`snapshots/**/*.verified.*`。

### 包 pin（`Directory.Packages.props`）

`Melanchall.DryWetMidi 8.0.3`（`ExcludeAssets="build"`）· `Avalonia` / `Avalonia.Desktop` / `Avalonia.Themes.Fluent` / `Avalonia.Controls.DataGrid` / `Avalonia.Diagnostics` **11.3.21** · `CommunityToolkit.Mvvm 8.4.2` · `MeltySynth 2.4.1` · `System.CommandLine` · `Microsoft.Windows.CsWin32` · `System.Text.Encoding.CodePages`（GB18030）· `xunit.v3 4.0.0` · `Verify.XunitV3`（**首次 restore 时 pin 当前最新 stable —— 版本列表里有 33.0.0-beta.x，要确认不要猜**）· `CsCheck` · `Mono.Cecil`（仅 Policy.Tests）· `Microsoft.CodeAnalysis.BannedApiAnalyzers` · `HotAvalonia`（仅开发）。

**构建姿态**：`PublishAot=false`、`PublishTrimmed=false`、`EnableCompressionInSingleFile=false`。NativeAOT **无法**从 macOS 交叉编译到 Windows，而普通 IL self-contained **可以** —— 这正是你能在 Mac 上切 win-x64 发布包的原因。但 Core/Profiles/Midi/Export/Project 从第一次提交就守 AOT-clean 纪律（STJ 源生成、无反射）。

---

## 四、数据模型（四层）

完整 C# 草图见 `docs/design/dataModel.md`。要点：

**L1 `SourceSong`** — `SourceNote(Index, TrackIndex, Channel, Pitch, Velocity, OnsetTicks, DurationTicks, OnsetUs, DurationUs)`。`Index` 是编辑日志**永久**的锚点。因为它是净化解析产生的序数，改 `NoteMinLength` 会静默重排所有编辑 —— 所以 `SourceSong` 带 `LoaderVersion` + `SanitizeSettingsHash`，打开旧项目时任一戳记不同就按内容指纹 `(OnsetTicks, Pitch, Channel, occurrenceOrdinal)` 重锚定，并报告重锚了几个、孤立了几个。**绝不静默。**

**L2 乐器模型** — 6 状态空间是**从 `ModifierSpec[]` + 互斥组 + `MaxSimultaneousModifiers` 在加载时派生的，绝不写死类型**。`EmissionTable` 提供 `DegreeOf(state, offset)` / `StateMask(offset)` / `IsPlayable(offset)`。通用校验器只断言派生不变量（状态内单射、覆盖无内部空洞、popcount 求和、绑定可解析）；乐器专属数字（6 状态 / 38 偏移 / 48 发音 / {1:30,2:6,3:2}）活在钉死的 fixture 测试里。

关键文件 `data/profiles/df.harmonica.v1.json`：HID page 0x07 —— Z=29 X=27 C=6 V=25 B=5 N=17 M=16 `,`=54；三个 modifier 带 `exclusionGroup`（左右=1，中=0）、`pressLeadMs/releaseLeadMs`、`dutyWeightMilli`。**`octaveDown.dutyWeightMilli` 是其他两个的 3 倍，因为左键在这个游戏里是开火** —— 目标函数必须主动避免把旋律停在低音带。`provenance: { confidence: "inferred", verifiedInGame: false }`。

**`data/timing/df.reference.json`（你选定的默认）** —— 每个值都是 `{ "v": n, "source": "...", "measured": false }`：`sameKeyRetriggerMs 12`、`octaveReleaseLeadMs 16`、`octavePressLeadMs 12`、`sharpLeadMs 8`、`noteHoldMinMs 20`、`keyChangeGapMs 20`、`keyHeldMinMs 10`、`chordWindowMs 25`、`globalLeadMs 25`、`breathRest{enabled:true, afterMs:8000, restMs:90}`，外加四个**诚实标注 `"source":"invented"`** 的值：`exclusiveSwapMs 16`、`interModifierStaggerMs 4`、`maxOnsetShiftMs 15`、`maxDriftMs 120`。`minSurvivingDurationMs` 是**派生的** = `noteHoldMinMs + keyChangeGapMs`。加载时跑跨常量关系检查 `keyChangeGapMs ≥ exclusiveSwapMs ≥ octavePressLeadMs ≥ sharpLeadMs`，用户改坏了就带确切原因拒绝。

呼吸休止**默认开**（8 秒连续发声后插 90ms）—— 这是竞品针对**实际观察到的**游戏内失败模式（判定粘连）加的唯一一个防护，默认开是 fail-safe 的选择。

**L3 `HarmonicaScore`** — `ScoreNote` 同时保留 `SourceMidiNote`（永不覆写）和 `EffectiveMidiNote`，`Fingering?`（Dropped/ChordSibling 时为 null），`NoteAlteration` 标志位。**被丢弃的音和和弦兄弟音保留在列表里并打标记**，不删 —— 这是「一键换成和弦里的另一个音」得以成立的前提。`Validate()` 里的单音不变量用的是**和 DP 同一张 `reqSil` 表**（含 setup 时间），堵住"时间线通过校验却在前一个音的延音里就把修饰键按下去了"这个洞。

**L4 `InputTimeline`（唯一契约）** — `InputEvent(TimeMs:int 绝对毫秒, Phase, Device, Code:ushort, IsDown, NoteId)`，排序键 `(TimeMs, Phase, Device, Code)`。**没有 `BatchId`**（相同 `TimeMs` 即一批，存字段就是第二个真相源）、**没有 `Label`**、**没有 `Value`**（三个键，没有滚轮，没有移动，永远不会有）。分支 A 和分支 B 都只吃这个。

**`.hsproj`（ZIP）** — 真相 = `original.mid`（字节一致内嵌）+ `settings.json` + `edits.json` + 三份配置快照；`cache/{score,report,timeline}.json` 是可重生的缓存，不匹配就忽略。打开时若已安装配置的 `profileVersion` 与快照不同，**展示字段级 diff** 并给「用项目内配置打开」/「迁移到新配置」两个选项。旧项目永不静默改音高。

---

## 五、核心：pass 流水线 + 联合优化

完整伪代码见 `.../scratchpad/design/pipeline.md`。

```
load.read            容错 ReadingSettings + GB18030 + Sanitize
load.inventory       每 (track,channel) 统计 + MelodyScore
track.select         默认剔除 channel 9
time.absolute        ticks→µs（两者都留）
time.trim            前导静音
─── 归约定点组，重复到 Changed==false，上限 4 轮 ───
  reduce.chords      45ms 窗口（音乐时间，速度缩放之前），最早 onset，兄弟音保留
  reduce.monophony   前向扫描截断重叠
  reduce.dropShort   丢弃 < minSurvivingDurationMs（派生值）
  reduce.smoothMelody 比两侧邻音都高 ≥12 半音且 <90ms → 提升次高兄弟音，无则丢弃
──────────────────────────────────────────
time.speed           µs /= speed；发音下限**不**随速度缩放（它们是物理输入延迟）
fit.search           ← 复合体，见下
fit.commit           物化 HarmonicaScore
edit.apply           编辑日志重放（锚定 SourceIndex）
fit.replan           t 固定、已编辑音固定，重跑合法化+规划
sched.articulate     解决阶梯 + 漂移守卫
sched.breath         呼吸休止
emit.lower           LowerHoldSpans | LowerToggleTransitions（配置驱动，两种都金文件化）
emit.holdCaps        编译期保持上限
emit.quantise        **唯一**取整点：µs → 绝对 int 毫秒
emit.validate        硬不变量。校验失败的时间线既不能演奏也不能导出。
```

**为什么归约是定点组：** `reduce.smoothMelody` 删音会重新打开 `reduce.monophony` 已经闭合的间隙。跑一遍会留下一堆对着静音的、人为断奏的音，并给 DP 的边定错价。

### 联合移调 + 修饰键优化

**候选范围**（精确边界，不预筛）：`T = [(P0+LO) − maxSourcePitch, (P0+HI) − minSourcePitch] ∩ [−36,+36]`，约 60–120 个。`TranspositionMode`：`PreserveKey`（限 12 的倍数）/ `Balanced`（默认）/ `Free` / `Manual`。

**分层目标**（见 D5）：
```
T1 hardInfeasible : int      阶梯必须合并或丢弃的转换数。计数，绝不归一化。
T2 musicalLoss    : int ppm  (droppedDurLow + 4×droppedDurHigh + 0.35×foldedDur)/totalDur × 1e6
T3 keyDistance    : (|signedMod12(t)|, |t|)
T4 mech           : int µu   W_EVENT×事件数 + W_EXCL×互斥交换 + W_DUTY×保持秒数 + W_SOFTDEF×赤字
比较：T1 精确 → T2 带 5000ppm(0.5%) 容差 → T3 精确 → T4 → 最终 (|t|, t, 状态序列) 确定化打平
```

**内层 DP**（`ModifierPlanner`，精确、整数、`O(k²·N)`，k 从配置派生 = 6）：

```
每 (profile,timing) 预计算一次 6×6 表：
  evCount[a][b]  = popcount(a^b)
  exclSwap[a][b] = a\b 与 b\a 中存在同一非零互斥组
  setup[a][b]    = max(释放侧 ReleaseLeadMs, 按下侧 PressLeadMs)
                   |> exclSwap 则 max(·, ExclusiveSwapMs)
                   |> + InterModifierStaggerMs × (evCount − 1)
  reqSil[a][b][sameKey] = ModifiersRepitchSustainedNotes
                          ? max(sameKey ? SameKeyRetriggerMs : KeyChangeGapMs, setup[a][b])
                          : (sameKey ? SameKeyRetriggerMs : KeyChangeGapMs)

每个音 i：
  off[i]=pitch[i]+t−P0;  mask[i]=StateMask(off[i]);  span[i]=onset[i+1]−onset[i]
  availSil[i] = max(0, span[i] − NoteHoldMinMs)          ← 悲观、可靠下界（D7）
  baseSil[i]  = min over (a,b)∈mask[i−1]×mask[i] of reqSil[a][b][sameKey]

nodeCost(i,st) = Σ_{m∈st} W_DUTY × m.dutyWeightMilli × span[i] / 1000
                 ← 按**相邻起音跨度**计费，正好等于极大区间降解会产生的真实保持时长

edgeCost(i−1,a → i,b):
  req     = reqSil[a][b][sameKey(a,b)]
  deficit = max(0, req − availSil[i]) − max(0, baseSil[i] − availSil[i])   ← 只计边际赤字
  return W_EVENT×evCount + W_EXCL×exclSwap + W_SOFTDEF×deficit + (deficit>0 ? HARD_PENALTY : 0)

前向：滚动 long[k]，回溯 byte[k*N]，热循环零分配
```

减去 `baseSil` 是为了让**纯音符密度**造成的不可行（15ms 起音间隔对 20ms 保持下限）不被记到修饰键规划头上再被 `W_SOFTDEF` 放大 —— 密度不可行单独作为 `DensityDeficitCount` 上报。

**复杂度**：每音每候选 36 次边求值，`N=2000` × 120 候选 ≈ 860 万次整数运算，几十毫秒，**settings 一改就能交互式重跑**。

**超范围合法化**跑在候选循环**内部、DP 之前**（折叠会改变所处的带，因而改变状态序列 —— 在 DP 之后折叠会让规划失效）。默认 `FoldThenDrop`：`< 120ms` 直接丢（错八度的装饰音比没有更碍耳）；否则最多折叠 1 个八度；轮廓守卫拒绝那些对**两侧**邻音都造成 >14 半音跳进、且两侧都不是乐句边界（≥250ms 休止）的折叠，改为丢弃。**不提供 clamp-to-edge 作为默认** —— 它破坏音级并产生读起来像卡住的同音高原。

**发音解决阶梯**（按不可感知程度排序）：`(a) 截短前音 → (b) 后移当前音（≤MaxOnsetShiftMs，累计 ≤MaxDriftMs）→ (c) 同键则合并 → (d) 丢弃 + 一条带确切毫秒数的 Blocker 诊断`。丢弃永远给出「缺 38 毫秒」这样可点击的诊断，绝不给「失败」。

**报告三个天花板而不是一个**：`SameKeyCeiling`、`KeyChangeCeiling`、`ModifierSwapCeiling`，外加从**本曲实际转换构成**算出的 `EffectiveCeiling`，以及 `P50/P95/Peak` nps（**P95 决定评级**，避免一串三十二分音符把整首好曲子的分打崩）。`ConversionReport` 是 `PassCounter` 袋子上的**投影**，不是 40 个平行字段 —— 加一个指标 = 加一个计数器。**没有「离调音」指标**，它在全半音阶乐器上结构性恒为零，报了就是表演。

---

## 六、分支 A —— 实时输入

**接缝**：`IInputBackend { Emit(ReadOnlySpan<CompiledEvent>); ReleaseAll(reason); }` + `IClock` + `IEnvironmentProbe`（**只返回原始事实，不含策略**）+ `internal ISendInputApi`。四个后端：`WindowsSendInputBackend`（生产）、`MacCgEventBackend`（开发/回环）、`TraceBackend`（**测试本身就是那份逐字记录**）、`FaultInjectingBackend`（包装另一个，在第 N 个事件按六种模式中断）。两个时钟：`StopwatchClock` 和 `VirtualClock`（`WaitUntil` 直接把 `_now` 设成目标，三分钟的曲子微秒级跑完）。

`VirtualClock` + `TraceBackend` 让**整个**演奏引擎在 macOS 上确定性可测：倒计时、开场释放全部、焦点守卫、紧急热键、暂停/续播、跳转、中途变速、异常路径、信号路径。

**SendInput 细节**：`ki.wVk = 0`，`ki.wScan = ScanCode1`，按下 `KEYEVENTF_SCANCODE (0x0008)`，**抬起 `0x000A`（SCANCODE|KEYUP）** —— 抬起时漏掉 SCANCODE 是这类工具卡键的头号原因，而且它是一行测试。八个键的 Set-1 码 `0x2C 0x2D 0x2E 0x2F 0x30 0x31 0x32 0x33`（都不是扩展键）。鼠标用 `MOUSEEVENTF_{LEFT,RIGHT,MIDDLE}{DOWN,UP}`，`dx=dy=mouseData=0`，**永不发 `MOUSEEVENTF_MOVE`**（光标和镜头不能动）。同一 `TimeMs` 的所有事件进**同一次** `SendInput` 调用 —— MSDN 保证单次调用内的事件不会被其他输入穿插，这正是"修饰键就位 + 击键"原子化的依据。签名由 CsWin32 生成，不手写。**完全不用 VK 注入**。

**调度器**：一个专用非后台 `Thread{Priority=Highest}`；绝对截止时间对 `Stopwatch`，绝不 `Sleep(delta)`；`CreateWaitableTimerEx(CREATE_WAITABLE_TIMER_HIGH_RESOLUTION)` 睡到剩 ~1.5ms 再 `Thread.SpinWait` 收尾；`timeBeginPeriod(1)`（Win10 2004 起是按进程的，必须自己调）；`AvSetMmThreadCharacteristicsW("Pro Audio")`；`ProcessPriorityClass.High`（永不 `RealTime`）；`GCLatencyMode.SustainedLowLatency` 在 `finally` 恢复；热循环零分配；倒计时前先对 `TraceBackend` 跑一遍热身让 JIT 和 P/Invoke stub 就位。

**热键**用 `RegisterHotKey` + 专用消息泵线程，**绝不用 `SetWindowsHookEx(WH_KEYBOARD_LL)`**（反作弊盯得最紧的 API）。F6 = 开始/暂停/续播/取消（与竞品一致，用户肌肉记忆可迁移），F7 = 紧急全释放。不绑裸 Esc（游戏菜单键，且 `RegisterHotKey` 会吞掉它），不绑 F12。

**卡键防护，分层且对每层诚实**（在线 FPS 里卡住的鼠标键 = 持续开火，是本项目能造成的最坏后果）：
1. 保持上限是 `InputTimeline` 的编译期属性（D9）—— 可校验、可金文件化、可在 Mac 上模拟，且对我们运行时管不着的导出宏同样成立。
2. `try/finally` + `AppDomain.UnhandledException` + `PosixSignalRegistration` + `Console.CancelKeyPress` + `ProcessExit`。
3. **会话开始时先发一批全释放** + `GetAsyncKeyState` 扫描 —— 这才是从上次硬崩溃里恢复的真正手段（MSDN 明说 `SendInput` 不会重置键盘状态）。
4. `hsc watchdog --parent <pid> --state <path>`：进程外看门狗，父进程死了就按记录的持有集发全释放。自带 trace 金文件和故障注入用例。

**「没反应」的六级诊断**。`PreflightRules` 是**纯函数**（事实 → 判定），由 `data/diagnostics/rules.json` 驱动，在 Mac 上用合成事实集金文件化：

| # | 原因 | 检测 |
|---|---|---|
| 0 | （预检）整条链路是否通 | **记事本投递测试**：启动 notepad，用八个琴键拼一个 nonce 按当前 profile 的下限打进去，`Ctrl+A/C` 读回并还原剪贴板。证明引擎能到达**真实的外部进程**、证明扫描码路径、且输入法转换态会立刻表现为乱码 |
| 1 | 游戏没焦点 | `GetForegroundWindow()` 对 `EnumWindows`+`GetWindowThreadProcessId` 找到的游戏 HWND。**每批之前都重查**（几百纳秒）。顺便杜绝"往 Discord 里打了四分钟 zxcvbnm" |
| 2 | 独占全屏 | `SHQueryUserNotificationState() == QUNS_RUNNING_D3D_FULL_SCREEN` |
| 3 | 输入法 | `GetKeyboardLayout(...)`，LOWORD==0x0804 则警告 |
| 4 | UIPI / 提权 | 只查我们自己的提权状态。**故意不 OpenProcess 游戏**（D11）。**记事本测试通过并不能排除本项** —— 记事本是中完整性，游戏若提权则测试照样绿而原因仍在，规则里必须这么写 |
| 5 | 游戏内改键 / 没装备乐器 / 模式不对 | 完全不可观测。用**按键捕获 UI** 规避：在我们自己的窗口里「请按下你的 1(do) 键」，从 `KeyEventArgs.PhysicalKey` 读扫描码。与布局无关、无钩子、不碰游戏进程 |
| 6 | 五项全绿仍然没声音 | 项目杀手情形：SendInput 到了 Windows 但游戏过滤 `LLKHF_INJECTED` 或做原始输入来源检查。**如实报告本项**，一键生成诊断包 |

**trace 日志就是金文件**。**同一个 `TraceWriter`** 既产出 CI 里的 `*.verified.trace`，也产出生产运行日志 —— 事件列记的是**计划时间**，所以金文件和真实运行天然可 diff；实际延迟只出现在汇总行。`hsc diag` 在本地打包 `hs-diag-<ts>.zip` 并**打开文件夹，绝不上传**；`hsc replay hs-diag-….zip --backend trace` 用用户的确切设置和配置快照在你的 Mac 上重跑并 diff。trace 对上 ⇒ 引擎是对的、环境是错的。**这就是完整的远程调试故事，不需要网络、不需要 Windows 机器。**

---

## 七、分支 B —— 宏导出

两条分支都逐字消费 `InputTimeline`。导出器绝不从 `HarmonicaScore` 重新推导任何东西。

**降解只在核心里做一次**：`Deltaise(带残差) → ClampDelays → Fold(若不支持分离 down/up) → MergeWaits → TrimLastWait → CheckBudget → SplitIntoParts`。增量从**绝对** `TimeMs` 算，残差携带并在下一个 >200ms 的休止处吸收，上报 `CumulativeDriftMs`。于是 writer 只剩 80–150 行纯序列化。

**第一层 · 通用（先做）**

| Id | 说明 |
|---|---|
| `ahk-v2` | **本项目最重要的导出。** 绝对 `T0` + `QueryPerformanceCounter` 调度，`timeBeginPeriod(1)` 与 `OnExit timeEndPeriod(1)` 配对，`Send "{Blind}{sc02C down}"` —— **`{Blind}` 是强制的**，因为我们会跨多个音持续按住鼠标修饰键，不加 `{Blind}` 的 `Send` 会热心地"恢复"它。`{RButton down}` 是整个分支 B 里**唯一**经过验证的鼠标语法，而这件乐器三个键都要用 |
| `hscore-json`/`csv` | 自有格式，带往返读取器 |
| `reduced-mid` | Type-0、已移调、单音。**光是这一个就让 HarmonicaScript 对整个现有 FF14/原神 工具生态有用**，别人不需要采用我们的演奏引擎 |
| `keytape` | 社区认可的字符纸带 + 方括号修饰键前缀，约 40 行 |
| `jianpu-hsq` | 简谱文本导出（`1 2 3 #4 5̇` + 小节线）。**v1 不做导入器** |

**第二层 · 已验证厂商，严格样本门控**：`bloody-amc`（UTF-8 带 BOM、CRLF、`KeyDown <hid> 1`/`Delay <n> ms`，HID 是直接强转）、`razer3-xml`（已对真实 Synapse 3 **键盘**宏验证：`<MacroEvent><Type>1</Type><Delay>N</Delay><KeyEvent><Makecode>…</Makecode><IsExtended/><State/></KeyEvent></MacroEvent>`，UTF-8 带 BOM，名字 ≤20 字符，`Makecode` = PS/2 Set-1）、`logitech-lua`（走脚本编辑器 Script → Import，用数值 Set-1 扫描码不用键名字符串，`OnEvent("PROFILE_DEACTIVATED")` 释放全部）、`redragon-msmacro`。

**这三个全都卡在同一件事上：需要一份含右键/中键事件的真实样本。** 已验证的 Razer 文件只证明了键盘事件和左键；Bloody 的 `RightDown`/`MiddleDown` 是从单次观察到的 `LeftDown` 类推的。所以 `keytable.json` 里右键/中键的 `razer3Button` 是 `null`，导出器**带着具名的缺失能力拒绝执行，而不是猜**。两个"猜错了会生成能成功导入但倒着播放的文件"的推断（Razer `<State>` 0=按下/1=抬起；MSMACRO `Map_N` 132=按下/4=抬起）存成 capabilities JSON 里的**类型化 `Quirks`** —— 一份现场报告翻转一个数字，不用重新编译。

**永不做**：`corsair-cueprofile` 延后并写明重入条件（cereal 的 `polymorphic_id`/`ptr_wrapper` 记账加上漂移的 `cereal_class_version`，写错会毁掉用户**整个**配置列表 —— 这是唯一一个失败会伤害用户而不只是失败的导出器）。VIA/Vial/QMK/ZMK **永远不做整曲导出器**（每音 14–18 字节对 500–900 字节的宏缓冲 = 30–50 个音，且 Vial 会**静默截断**）。SteelSeries 根本没有宏导入。ASUS/MSI/酷冷/ROCCAT/Wooting/Glorious/Pulsar 和整个国产集群（狼蛛/VGN/黑爵/达尔优/雷柏/Attack Shark/KZZI/机械师/MIIIW/雷神）**零个可验证格式**，网上流传的两份"规格"追溯到 AI 生成的 SEO 农场。**永不写投机导出器。**

**放弃了「每个 writer 配一个 reader，往返相等替代厂商软件」这条**。两位评审独立指出它在恰恰最要紧的那些未知上是同义反复：reader 和 writer 共享同一个心智模型，所以 `<State>` 极性反转、延时在事件前还是事件后、未转义的花括号 —— 全都能完美往返然后发布出去，而团队对不可验证面的信心毫无证据地上升了。替代方案是三条真办法：(1) **从真实样本做解码 fixture**（Synapse 3 样本里的 `91/30/42/15/57` → `LWin(ext)/A/LShift/Tab/Space` 是真正的神谕，不是镜子）；(2) **金文件打在降解后的事件列表上**（每个 fixture 都打，回归都住在那儿，小且可评审）**加六个精选 fixture 的导出器字节**（短曲/半音阶/长修饰键区间/踩事件预算/三个鼠标键全用/病态）；(3) **在 CI 上真跑 AHK** —— `windows-2025` 作业执行生成的 `.ahk`（把 `Send` 打桩到日志）并与 `InputTimeline` diff。

**样本汇集管线（零网络）**：`hsc contrib package --vendor <id>` 打出 zip（清单 + 字节原样 + 二进制格式的 4KiB hexdump + `recipe.md` + 脱敏报告）并打开文件夹，**由用户自己发送**。录制配方用**互质的延时和不同的键**让每个字段单独可辨认：「按下 Z，等 137 毫秒，松开 Z，等 251 毫秒，按下 X，等 61 毫秒，松开 X。再录一个：按住鼠标**右键** 173 毫秒后松开，按住鼠标**中键** 89 毫秒后松开。录制时请打开『记录延时』。」`hsc contrib inspect <zip>` 把样本喂给所有已有 reader，对未知格式打印针对已知延时（137/251/61）的字段对齐视图 —— 延时编码通常一眼可见。

---

## 八、GUI

Avalonia **11.3.21** + `CommunityToolkit.Mvvm 8.4.2`，全局 `x:CompileBindings="True"`，MIT 的 `Avalonia.Controls.DataGrid`，`Avalonia.Diagnostics` 看可视树，`HotAvalonia` 做 XAML 热重载。**不引用 `Avalonia.Headless`**（它是唯一会把整个测试套件耦合到 Avalonia 版本、并引入 xunit v2/v3 pin 冲突的依赖），v1 不写自动化 UI 测试，改用手工截图清单。

**i18n 一次做对**：中性 `Resources.resx`（英文）+ `Resources.zh-Hans.resx`（**不是 zh-CN** —— 回退链 `zh-CN → zh-Hans → zh → neutral` 用一个文件服务 CN/SG/MY）。约 40 行的 `Loc : ObservableObject` 单例带字符串索引器，`SetCulture()` 触发 `OnPropertyChanged("Item[]")` —— Avalonia 官方文档明说 `{x:Static}` 绑定**不会**在文化切换时刷新。**核心永不本地化**：它返回 `DiagnosticCode` 枚举 + 数值参数，由 GUI 映射到 resx。一个 10 行测试断言每个中性 key 在 zh-Hans 里都存在、每个 `DiagnosticCode` 成员都有条目 —— 防的是经典的"发布了个翻译了一半的版本"。

**S1 · 外壳 + 导入 + 报告**（M11）。顶栏：中/EN · 帮助 · **「没有声音？」** · **「生成诊断包」** · 一个在 `verifiedInGame == false` 期间常驻的琥珀色徽章「本程序未经实机验证」。
- *文件与音轨*：拖放区；音轨 DataGrid（☑/名称/音符数/时长/音域/平均复音/★推荐），打击乐置灰不可选。
- *转换设置*：移调 spinner + 自动 · 移调模式 · 速度 · 超范围处理 · 时序 profile 选择器 —— **每个时序值都带 `Provenance` 徽章**，`measured: false` 的加虚线下划线 + 悬浮提示「此数值未经实测」。
- *演奏性报告*：评级 + **水平堆叠惩罚条**（丢音 −12 / 八度折叠 −7 / 速度 −3），**绝不是一个光秃秃的分数** —— 「78/100」什么都没告诉用户，拆解才告诉他该改什么。点击某段筛选音符表格。下面是移调候选单选列表，展示每个候选的四层数值，点击就重跑（几十毫秒）。
- *导出*：能力驱动，不支持的目标带原因置灰，`ExtraInstructions` 通用地呈现。

**S2 · 音符编辑器**（M12）。DataGrid 列 `# · mm:ss.mmm · 小节:拍 · 度数 · # · 八度 · 按键 · 修饰键 · 原音 · Δ半音 · 标记`，默认筛选「只看有问题的音」。行内编辑度数/升降号/八度、删除/静音、`PromoteChordSibling`（下拉列出保留的兄弟音）。下方是**只读**钢琴卷帘条：灰色源音符 vs 彩色发出音符，带可演奏音域色带。**故意不做拖拽编辑** —— 乐谱是单音的，所以它就是个列表；列表编辑器 + 只读卷帘用零头的代价承载了同样的信息。

**S3 · 编辑日志 + `.hsproj`**（M12）。有序 `List<ScoreEdit>`，扁平记录 + 单一枚举判别式（**不用 `[JsonDerivedType]`** —— 多态 + 源生成 + AOT 纪律是活的痛点），锚定 `SourceIndex` **和**内容指纹，**绝不**锚定生成的 `ScoreNote.Id`（移调一变它就漂）。这个列表**本身就是撤销栈**，白送，而且可 git diff。

**S4 · 排练视图**（M12）。八个简谱按钮 + 三个鼠标指示灯，由**与往返测试所断言的同一个 `InstrumentSimulator`** 驱动 —— 所以排练视图是一个**已验证不变量的渲染**，而不是一个会静默漂移的第二实现。而且如果分支 A 最终被证明不可行，它本身就是一个完整的、零注入的产品。

**S5 · 演奏**（M13）。六级预检面板（判定 + 证据）；3-2-1 倒计时（用户自己 alt-tab 进游戏，我们**永不调 `SetForegroundWindow`**）；实时 trace 尾；大的停止（F7）按钮；本区域常驻一行风险横幅。

**S6 · 自检与诊断**（M13）。记事本投递测试及其读回 diff；六级决策树；生成诊断包；按键捕获 UI；配置文件导入/粘贴/导出（接受前先做字段级校验 diff）；**实机验证向导 —— 是问卷而不是自动探测器**，因为自动探测预设了分支 A 已经能用，那是循环论证。

**首次运行**：阻塞式双语免责声明模态（中文为主），必须显式点击才能关闭。

---

## 九、里程碑

约 66 个"理想工作日"；三套方案都被评审判定乐观 1.5–2 倍，**现实预算 90–110 个开发日**。

| # | 内容 | 量 | DONE 判据 |
|---|---|---|---|
| **M0** | 工具链、骨架、两个构建路径尖刺 | 2d | 装 .NET 10 SDK（**第零号动作**）；`global.json`、`.slnx`、CPM、`Directory.Build.props`、git init、MIT LICENSE、`docs/PROVENANCE.md` 开始维护；11 个工程 + 5 个测试工程建好。`dotnet build && dotnet test` 在 ubuntu-24.04 / macos-26 / windows-2025 三个 runner 上全绿。**并且两个各 30 分钟、但在赌两周的尖刺都通过**：(a) Avalonia 11.3.21 在 net10.0 + macOS arm64 上真的开出窗口（XAML 编译器任务 + 原生资源解析，不只是 lib 解析）；(b) 从 Mac `dotnet publish -r win-x64 --self-contained` 出来的 exe 在 windows-2025 上**能启动**，且输出目录里没有 DryWetMidi 原生 blob |
| **M1** | 乐器/时序/键表/游戏/诊断规则 schema、四级覆盖链、发音表 | 3d | `hsc profile candidates` 打出全部 38 个偏移的 (度数,状态) 发音，同时匹配金文件**和**对闭式公式的穷举断言；`hsc profile dump --effective` 显示每个字段来源；钉死的 fixture 测试断言 6 状态 / 38 偏移零空洞 / 48 发音 / {1:30,2:6,3:2} / 两路恰好 {−7,1,5,13,17,24} / 三路恰好 {0,12} / 状态唯一确定按键；跨常量关系检查能拒绝一个故意写反的配置 |
| **M2** | MIDI 摄入、语料、fixture 生成器 | 4d | 提交 25–30 个**公有领域** .mid；程序化生成器（代码+固定种子）覆盖半音阶跑动、带缝穿越（偏移 0/12）、4 秒长音、踩重触发下限的密集十六分、强制左右互斥交换、超 38 半音音域、40–50ms 的琶音和弦、多段 tempo、畸形文件。一个故意损坏的 fixture（`KeySignature scale=255`、截断 chunk、孤立 NoteOn、GBK 音轨名）能**加载而不是抛异常**；`testdata/README.md` 写明版权规则 |
| **M3** | 归约流水线（有界定点 + 计数器） | 3d | 四种归约策略下严格单音不变量在所有 fixture 上成立；定点在 4 轮内收敛；CsCheck 属性断言发出的音高内容 ⊆ 源、且被丢弃/兄弟音保留在列表中 |
| **M4** | 修饰键规划 DP | 5d | **暴力等价性属性测试** —— CI 跑 2000 个 N≤6 随机实例（夜间加 100 个 N≤8），int64 **精确相等**，生成器强烈偏向多路偏移 {0,12} 和 {−7,1,5,13,17,24} 以及强制互斥交换的相邻对（均匀生成器出的几乎全是平凡实例，因为 38 个偏移里 30 个只有一个可行状态）；单调性测试（调高 `W_EVENT` 绝不使最优解的事件数上升）；自洽测试（DP 报告的代价 == 从产出方案重算的代价）；**可靠性测试**（DP 标为可行的每条边都能靠单独截短前音实现）。N=2000 全候选求解 < 50ms |
| **M5** | 移调搜索 + 分层目标 | 3d | **退化回归套件**通过：12 首各调全自然音合成曲全部解到 `|signedMod12(t)|` 最小；一首全升号的曲子**不**被移调 −1；被音域逼到 `t=+3` 的曲子不会被任何机械节省挪到 `t=+4`（加权和输掉的正是这一例）；有三次不可行互斥交换的长曲输给一个折叠 0.3% 时长的候选（归一化目标输掉的正是这一例）。跑两次逐字节一致；Cecil 断言优化器里没有浮点 |
| **M6** | 发音阶梯、降解、保持上限、校验器 | 4d | 两套时序 profile × 两种降解模式下所有 fixture 零违规；一个会让修饰键保持锁定的 toggle 降解时间线被**拒绝**；对抗性 fuzz（1ms 起音间隔、交替极端八度、零长音符、300nps 爆发）仍产出合法时间线且结尾无任何按住/锁定 |
| **M7** | `InstrumentSimulator` 往返 + 试听 | 3d | **项目的中心正确性闸门**：对每个 fixture，(a) `Simulate(timeline)` 精确产出可听乐谱的 `EffectiveMidiNote` 序列且起音误差在 `maxOnsetShiftMs` 内，(b) **发声音高在每个音符的 [down,up) 跨度内恒定**，(c) 非循环断言 `EffectiveMidiNote == SourceMidiNote + t`（对未标记折叠/丢弃/兄弟的音）。一个故意做坏的规划器（修饰键早一个音就位）**必须让它失败**，这个反向测试一起提交。`hsc audition song.mid -o /tmp/song.wav` 在 macOS 上产出能听的文件 |
| **M8** | CLI + 通用导出器（含 AHK v2） | 4d | 所有核心能力在不加载任何 GUI 程序集的情况下可无头触达；四个自描述格式 `Parse(Write(t)) == t`；`.reduced.mid` 用 DryWetMidi 重开后单音音高/起音序列完全一致；5 分钟曲子 `CumulativeDriftMs < 1ms`；**`windows-2025` 作业真跑生成的 `.ahk`（`Send` 打桩到日志）并与 `InputTimeline` diff 干净** |
| **M9** | Windows CI 硬化 | 2d | 在 `windows-2025` 上：SendInput 打进一个真实 Win32 窗口并断言字符逐个到达（端到端验证 Set-1 表、扩展标志、`0x000A` 抬起路径、单次调用批处理）；跑绝对截止时间调度器并记录 p99 截止误差；跑 `RegisterHotKey` 泵线程；跑 P/Invoke 编组断言。**四项每次 push 都绿，把原本被硬件卡住的约 70% 变成每次提交都跑的检查。全程不涉及三角洲行动。** |
| **M10** | 权重定标 + 金文件评审门 | 3d | 目标权重移入 `data/weights/objective.v1.json` 各带 `Provenance` 徽章；**机械类权重标为 Invented 并绑定到它们所猜测的那套 `TimingProfile`**（MeltySynth 渲染没法告诉你游戏拿一个不够的间隙会怎么办）；`hsc goldens review` 输出跨全部 fixture 的聚合 delta 表；CI 拒绝任何缺 `GOLDEN-CHANGE:` trailer 的 `*.verified.*` 变更；固定 10 首的耳测集**认真听过一遍** |
| **M11** | Avalonia 外壳 v1（S1） | 8d | **单块最大、也最可能超支**（对 Avalonia 新手而言）。macOS 上全程用 GUI 完成一次 MIDI → 报告 → 导出；resx key 对齐与 `DiagnosticCode` 对齐测试通过；切换 中/EN 不重启即改变每一个可见字符串 |
| **M12** | 音符编辑器 / 日志 / `.hsproj` / 排练视图（S2–S4） | 8d | 旧版本存的项目在新转换器下打开、重跑流水线、**保住编辑**，并报告重锚了几个 / 孤立了几个；排练视图在无头测试中对金时间线逐帧准确。**若分支 A 最终不可行，到这里已经是一个完整产品** |
| **M13** | 分支 A：Windows 后端、预检、自检、诊断（S5–S6） | 6d | macOS 上用 `TraceBackend`+`VirtualClock` 跑通所有演奏不变量，含**故障注入 theory**（每 fixture × 每第 17 个事件 × 六种中断模式）断言 trace 永远以 `RELEASEALL` 且持有集为空结尾；`PreflightRules` 对合成事实集金文件化；macOS 回环把整首 fixture 打进 TextEdit 且读回文本精确匹配；`windows-2025` 的 SendInput 作业保持绿。**故意排在最后 —— 不可验证的那部分永远不阻塞可验证的 90%** |
| **M14** | 厂商导出器（严格样本门控） | 5d | 顺序 `bloody-amc`(+同字节 `.bmc`) → `razer3-xml` → `logitech-lua` → `redragon-msmacro`。**每个的门槛**：一份含右键/中键事件的样本（msmacro 还要 `DelayType=1`）。样本到之前，导出器**已注册但带具名缺失能力拒绝执行**。每个交付时：真实样本能被解析成手写的期望时间线；`Write(Parse(sample))` 只在规范化空白上有差异；UI 明说导入性未经硬件验证。Corsair 和所有未验证厂商不做 |
| **M15** | v1.0 | 3d | 文档收尾；`verifiedInGame` 只在足够多一致的用户报告出现后才置位；不可验证清单收缩到确实没人能关掉的项；对 Avalonia 12 迁移做一次评估决定；到这时才考虑第二个乐器配置 |

---

## 十、验证策略（Mac only）

**五个测试套件**：`Core.Tests`（DP 暴力等价、退化回归、CsCheck 属性、金文件）、`Midi.Tests`（畸形容错、往返）、`Export.Tests`（解码 fixture、降解事件列表金文件、六个精选字节金文件）、`Playback.Tests`（`VirtualClock`+`TraceBackend` 的全引擎确定性测试、故障注入、`PreflightRules` 金文件）、`Policy.Tests`（Mono.Cecil + `BannedApiAnalyzers` 强制架构禁令 —— "Midi 不许用 Multimedia"、"Core 不许有 I/O"、"优化器里没有浮点"、"没有 `System.Net`" 都是**测试**而不是表格里的散文）。

**三个不可绕过的闸门**：
1. **`InstrumentSimulator` 往返**（M7）—— 独立于 `EmissionTable`、直接从 `DegreeSemitones` + `ModifierSpec` 算音高，把音高建模成时间上的阶跃函数。它是"我们发出的按键序列真的会奏出我们打算奏的音"这件事在没有游戏的情况下唯一的证据。配一个必须失败的反向测试。
2. **DP 暴力等价**（M4）—— int64 精确相等，生成器偏向非平凡实例。
3. **`windows-2025` 免费 runner**（M8/M9）—— 真跑 SendInput、真跑生成的 AHK、真跑调度器。**这一条把不可验证面从"整个 Windows 路径"压缩到字面意义上的一个问题：三角洲行动会不会接受注入的输入。**

**手工端到端（在你的 Mac 上）**：
```bash
dotnet run --project src/HarmonicaScript.Cli -- convert testdata/pd/ode-to-joy.mid --report
dotnet run --project src/HarmonicaScript.Cli -- audition testdata/pd/ode-to-joy.mid -o /tmp/a.wav && open /tmp/a.wav
dotnet run --project src/HarmonicaScript.Cli -- export testdata/pd/ode-to-joy.mid --target ahk-v2 -o /tmp/a.ahk
dotnet run --project src/HarmonicaScript.Cli -- play testdata/pd/ode-to-joy.mid --backend trace   # macOS 回环打进 TextEdit
```

**永远无法在这里验证的（如实列进 README）**：① SendInput 是否到达三角洲行动；② 游戏是否提权（决定 UIPI）；③ 时序常量的真实下限（`df.reference` 是竞品的实战调参值，不是测量值）；④ 修饰键切换的真实建立时间；⑤ 任何厂商宏文件是否真能导入并按毫秒精度回放。`docs/CALIBRATION.md` 把这些写成六个脚本化的游戏内实验，每个只需一个是/否回答 —— 任何有 Windows 和游戏的人（包括未来的你）花半小时就能把其中四个关掉。

---

## 十一、风险与合规

**许可证 MIT**（见 D14 的 clean-room 边界流程）。

**工程宪章**（写进 README，可测的部分由 `Policy.Tests` 强制）：不做 DLL 注入；不读写内存；不 `OpenProcess` 游戏；无内核组件/过滤驱动；不用 `WH_KEYBOARD_LL`；默认不提权；不在游戏窗口上做覆盖层；不截屏；**零网络 I/O**；不混淆、不加壳、不反调试、不随机进程名；**永不加入任何与战斗沾边的功能**（一个就足以让整个工具被重新归类）。自我描述永远是「MIDI 转简谱/宏 工具」，绝不是外挂/辅助/脚本。

**人性化抖动默认关**，开启时从存在 settings 和 `.hsproj` 里的种子生成（不然会毁掉每一个金文件），命名为「人性化演奏」，**永不**在任何地方被描述成规避检测的手段。

**应用内阻塞式免责声明**（首次运行，中文为主）要点：明确写出腾讯官方列表**同时**点名了自动脚本**和**鼠标宏/硬件宏；因此**两种模式都在官方明令禁止的范围内**；宏导出只是在**检测层面**更安静，**不是政策安全港**；本工具不做任何反检测处理，也不会声称"防封/过检测"；本工具不联网，可用防火墙自行验证；建议仅在安全区/大厅使用，切勿在对局中使用。**「防封 / 过检测 / undetectable / 100% safe / 更安全」这些词永不出现在产品、README、发布说明或提交历史里** —— 这是永久承诺，不是默认值。分支 B 唯一被允许的说法是架构性的：「导出的宏文件由厂商软件播放，本程序不与游戏进程发生任何交互」。

**版权姿态**：自带文件、纯本地处理、零上传、零内置曲库、零分享功能、零云端。演示素材只用公有领域内容。**永久抵制社区曲谱仓库** —— 它是唯一一个既把中立转换器变成分发平台、又提供了"招来针对性特征"的流行度的功能。

---

## 十二、参考资料

设计综合体的各章节都在本目录下：`dataModel.md`（数据模型）、`pipeline.md`（流水线与联合优化）、
`branchA.md`（实时输入）、`branchB.md`（宏导出）、`gui.md`、`verification.md`、`decisions.md`、
`milestones.md`、`cut.md`、`releaseAndRisk.md`、`solutionLayout.md`、`thesis.md`。

实际实现过程中与本计划的偏差、发现的 bug 与结论，记录在 [`../JOURNAL.md`](../JOURNAL.md)。
