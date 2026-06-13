# Voice Note 可重叠（和弦）— 手测用例

只覆盖本需求受影响范围：可重叠 note 序列经 voice 插件消费的两条路径——

- **V1 voice 插件**：原样消费可重叠 note（和弦发声）。
- **Legacy voice 插件**：经 compat「后盖前」钳位退化成单声部（老引擎硬假定单声部）。

> 注意：钢琴窗拉伸/移动会主动裁掉重叠邻居，MIDI 导入也去重叠——**当前唯一能注入重叠 note 的入口是 `.tlp` JSON 工程**（原样读入、不去重叠）。故本测试用夹具 `fixtures/voice-note-overlap.tlp`。

## 前置准备（已就绪）

1. **构建/运行宿主**：跑 App 即自动 build 并拷入最新 `TuneLab.Hosting.Compat.Legacy.dll`（含钳位改动）。
2. **安装两个测试插件**（拖进窗口或扩展侧边栏 Install Extension）：
   - `tests/tlx/legacy-voice.tlx` → 老接口声库，UI 显示名 **「Carol (Legacy Test)」**
   - `tests/tlx/v1-voice.tlx` → V1 会话声库，UI 显示名 **「Alice (V1 Test)」**
3. **打开夹具**：导入/打开 `tests/fixtures/voice-note-overlap.tlp`（两条轨已分别挂好上面两个声库，note 完全相同便于 A/B）。

工程速览（120 bpm，480 tick = 0.5 s）：

| note | pos (tick) | dur (tick) | pitch | 用途 |
|---|---|---|---|---|
| 1 | 0 | 720 | C4(60) | 尾部重叠：被 note2 盖 |
| 2 | 480 | 720 | E4(64) | 起点落在 note1 内 |
| 3 | 1920 | 720 | C4(60) | 同起点和弦：最长 |
| 4 | 1920 | 480 | E4(64) | 同起点和弦：中 |
| 5 | 1920 | 240 | G4(67) | 同起点和弦：最短 |
| 6 | 2880 | 480 | D4(62) | 和弦后的独立 note |

## 用例 A — 尾部重叠（后盖前）

note1 C4 [0–0.75s) 与 note2 E4 [0.5–1.25s) 在 [0.5,0.75s) 重叠。

- **Legacy 轨**：note1 尾巴被钳到 note2 起点 → C4 仅 [0–0.5s)，随后 E4 [0.5–1.25s)。**单声部、无重叠区**。
- **V1 轨**：两 note 原样 → [0.5–0.75s) 同时听到 C4+E4（叠音）。

## 用例 B — 同起点真和弦

note3/4/5 同起点 1920（2.0s），数据层序按 EndPos 降 = C4→E4→G4。

- **Legacy 轨**：C4、E4 的 Next 是同起点的更短音 → 钳后归零、退出发声；只剩排最后、EndTime 最靠前的 **G4 [2.0–2.25s)** 存活。和弦塌成单音 G4，随后 D4 [3.0–3.5s)。
- **V1 轨**：C4+E4+G4 自 2.0s 同时起 = **C 大三和弦**，错位释放（G4→2.25s、E4→2.5s、C4→2.75s），随后 D4。

## 判定

| | Legacy（Carol） | V1（Alice） |
|---|---|---|
| 用例 A 重叠区 | 只有先 C4 后 E4，**无叠音** | [0.5–0.75s) 听到 **C4+E4 叠音** |
| 用例 B 和弦 | 塌成单音 **G4** | 清晰 **C 大三和弦** |

听感：Legacy 轨整体单薄、逐音相接；V1 轨在第 1 拍出现叠音、第 2 拍出现明确和弦。两轨 note 数据完全一致，差异全部来自 compat 单声部兜底。

> 单测侧已在 `legacy/compat/TuneLab.Hosting.Compat.Legacy.Tests/NoteOverlapClampTests.cs` 钉死钳位算术；本手测验证端到端听感与产物归属。
