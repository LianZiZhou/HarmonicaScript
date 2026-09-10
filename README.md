# HarmonicaScript

把单轨 MIDI（FF14 吟游诗人 / 原神风物之诗琴那类社区文件）转换成《三角洲行动》游戏内口琴
**守夜人的口风琴**可演奏的乐谱，然后二选一输出：实时模拟键鼠演奏，或导出成外设厂商软件可导入的宏文件。

> Converts a single-track MIDI file into a playable score for the in-game harmonica in
> Delta Force (三角洲行动), then either plays it via simulated input or exports it as a
> vendor macro file.

---

## ⚠️ 使用前必读 / Read this first

本工具会**模拟键盘/鼠标输入**（实时演奏模式），或**生成宏文件**供外设厂商软件播放（宏导出模式）。

腾讯《三角洲行动》官方《禁止/不建议使用的软硬件列表》**同时点名**了
「按键精灵、Autohotkey、键盘鼠标控制切换器、幽灵键鼠、大漠外挂程序、**python 等自动脚本**」
与「**鼠标宏**」，Garena 版公告并列出「**硬件宏**」。

**因此本工具的两种模式都在官方明令禁止的范围内。**
宏导出模式只是在**检测层面**更安静 —— 它**不是政策上的安全港**。
使用本工具可能导致封号，包括长期封禁与设备（机器码）封禁。

本工具**不做任何反检测处理**，也**不会**声称「防封」「过检测」「不会被发现」。作者不对任何账号损失负责。

本工具**不联网**：无遥测、无自动更新、无账号、无云端曲库。你可以用防火墙自行验证。

建议仅在**安全区/大厅**使用，**切勿在对局中使用**。

---

## 乐器

```
键盘   Z  X  C  V  B  N  M  ,       半音偏移 0 2 4 5 7 9 11 12   (简谱 1 2 3 4 5 6 7 1̇)
鼠标左键 按住 = 降调 = 整排 −12      鼠标中键 按住 = 半音 = 整排 +1
鼠标右键 按住 = 升调 = 整排 +12      左右互斥，中键可与左右叠加

pitch = P0 + 12·shift + degree[key] + sharp      shift ∈ {−1,0,+1},  sharp ∈ {0,1}
音域   P0−12 … P0+25 = 38 个半音，无空洞
```

关键结构性事实：8 个音级加半音修饰键让每个八度带覆盖 **14 个连续半音**，三个带重叠，
所以这件乐器是**完全半音阶**的（`#3 ≡ 4`、`#7 ≡ 1̇`）。原神/FF14 工具里的核心难题
「离调音怎么办」在这里**根本不存在**。真正的难题只剩两个，而且耦合：**选哪个移调**、
**怎么排修饰键** —— 这正是本项目的算法核心（分层字典序目标 + 6 状态 Viterbi DP）。

## 状态

引擎、CLI、GUI、导出器、实时输入后端均已实现，**363 个测试全绿**。

乐器映射与全部时序常量仍是**推断值，未经实机验证** —— 它们全部是带 `source` 标注的 JSON
数据，改一个字段即可修正，不需要重新编译。见 [`docs/CALIBRATION.md`](docs/CALIBRATION.md)：
任何有 Windows 和游戏的人花半小时就能关掉其中四个未知量。
工程过程与每个里程碑的实际发现见 [`docs/JOURNAL.md`](docs/JOURNAL.md)。

## 用法

```bash
hsc convert  song.mid                      # 转谱并打印演奏性报告
hsc convert  song.mid --json               # 同上，JSON 输出
hsc export   song.mid -t ahk-v2            # 导出 AutoHotkey v2 脚本
hsc export   song.mid -t reduced-mid       # 导出降调归约后的单轨 MIDI
hsc targets                                # 列出全部导出目标
hsc audition song.mid -o /tmp/a.wav        # 渲染「实际会响成什么样」
hsc simulate song.mid                      # 打印模拟器认为会发出的每个音
hsc profile  candidates                    # 打印全部 38 个偏移的发音组合
hsc profile  dump --effective              # 每个字段来自覆盖链的哪一层
hsc fixtures                               # 重新生成确定性测试语料
```

GUI：`dotnet run --project src/HarmonicaScript.App -- --open song.mid`

### 导出目标

| id | 说明 | 状态 |
|---|---|---|
| `ahk-v2` | AutoHotkey v2 脚本 | ✅ 本项目最重要的导出路径 |
| `reduced-mid` | 降调归约后的 Type-0 单轨 MIDI | ✅ 可直接喂给现有 FF14/原神 演奏器 |
| `hscore-json` / `hscore-csv` | 自有时间轴格式，可往返读取 | ✅ |
| `jianpu-hsq` / `keytape` | 简谱文本 / 按键纸带 | ✅ |
| `logitech-lua` | 罗技 G HUB / LGS 脚本 | ✅ 唯一鼠标 API 有公开文档的厂商 |
| `razer3-xml` | 雷蛇 Synapse 3 | ⛔ 缺右键/中键真实样本，**拒绝生成** |
| `bloody-amc` | 血手幽灵 / 双飞燕 | ⛔ 同上 |
| `redragon-msmacro` | 红龙及同源方案 | ⛔ 缺 `DelayType=1` 延时编码样本 |

标 ⛔ 的导出器**已注册但会拒绝生成文件**，并告诉你需要什么样本才能解锁。这不是偷懒：这件乐器
的三个修饰键就是鼠标键，猜错编码会生成一个能成功导入、然后按错键的文件 —— 在一款 FPS 里那意味着
按住开火。

## 开发

需要 .NET 10 SDK（本机 SDK 8.0.x / 9.0.x 均已于 2026-11-10 停止支持）。
如果 .NET 10 装在 `~/.dotnet`（无需管理员权限的安装方式），需要：

```bash
export PATH="$HOME/.dotnet:$PATH" && export DOTNET_ROOT="$HOME/.dotnet"
```

```bash
dotnet build HarmonicaScript.slnx
dotnet test  HarmonicaScript.slnx

# 证明 Avalonia 在本机真的能渲染窗口，渲染后立刻退出 0（CI 在 Windows 上跑的是同一个入口）
dotnet run --project src/HarmonicaScript.App -- --smoke

# 从 macOS 交叉发布 Windows 可执行文件（NativeAOT 做不到，普通 IL self-contained 可以）
dotnet publish src/HarmonicaScript.App -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=false -o artifacts/win-x64/app
```

## 发布

`.github/workflows/release.yml` 在**每次 push** 时构建全部六个目标并发到 GitHub Release，
版本号就是 commit 短 hash：

| 目标 | 内容 |
|---|---|
| `win-x64` / `win-arm64` | |
| `osx-x64` / `osx-arm64` | 每个 zip 都是**自包含**的：内含 .NET 运行时、GUI、CLI， |
| `linux-x64` / `linux-arm64` | 以及可编辑的 `data/` 配置目录，解压即用 |

六个目标全部从**同一个 ubuntu runner 交叉发布** —— 普通 IL self-contained 可以跨平台发布
（只有 NativeAOT 不行），所以六个 runner 的矩阵只会花六倍的钱换同一个结果。这条路径与开发者
在 Mac 上切发布用的是同一条，CI 因此验证的是真实机制而不是一个平行机制。

GUI 与 CLI 发布进**同一个文件夹**：它们共用整个运行时，一起打包只比单独打 GUI 多约 1 MB，
而不是多 35 MB。

发布被标记为 **prerelease** —— 这些是每次提交的 CI 构建，若标成正式版会让仓库的
"Latest release" 徽章在每次 push（包括临时分支）时跳动。tag 用 `build-<短hash>` 而不是裸的
短 hash，因为 git 会把形似 object id 的 tag 视为歧义引用，每次 checkout 都警告一次。

## 架构

```
Core        乐器模型 · 发音表 · pass 流水线 · 移调搜索 · 修饰键 DP · 发音阶梯 · 降解 · 校验器 · 模拟器
Profiles    JSON schema + 四级覆盖链（内嵌 → 程序目录 → 用户目录 → 项目快照），带每字段来源
Midi        唯一引用 DryWetMidi 的地方；容错读取 + 语料生成 + 单轨 MIDI 重导出
Export      导出器契约 + 共享降解链 + 十个导出目标
Playback    平台无关演奏引擎（虚拟时钟 / trace 后端 / 故障注入）+ 六级诊断
  .Windows  SendInput 后端（扫描码路径，seam 可注入 → 编组逻辑在 macOS 上可断言）
  .MacOs    CGEvent 后端，仅开发/演示
Project     .hsproj + ConversionService（CLI 与 GUI 共用同一条路径，不会走偏）
Audition    离线 WAV 渲染，无音频设备依赖
Cli / App   hsc 命令行 / Avalonia GUI
```

## 工程宪章

不做 DLL 注入 · 不读写游戏内存 · 不 `OpenProcess` 游戏 · 无内核组件或过滤驱动 ·
不用 `WH_KEYBOARD_LL` 钩子 · 默认不提权 · 不在游戏窗口上做覆盖层 · 不截屏 ·
**零网络 I/O** · 不混淆不加壳不反调试 · **永不加入任何与战斗沾边的功能**。

可测的部分由 `HarmonicaScript.Policy.Tests` 用 Mono.Cecil 强制，不是 README 里的散文。

## 许可证

MIT。见 [`LICENSE`](LICENSE)、[`NOTICE.md`](NOTICE.md) 与
[`docs/PROVENANCE.md`](docs/PROVENANCE.md)（clean-room 边界记录，从第一次提交起持续维护）。
