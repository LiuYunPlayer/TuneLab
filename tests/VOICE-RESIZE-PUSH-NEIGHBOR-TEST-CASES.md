# Voice 挂载下 note resize 推挤邻居 测试用例

## 背景

钢琴窗 note 头/尾 resize 曾为支持自由重叠删去"推挤邻居"逻辑（拉多远都不动旁边 note）。
voice/instrument 拆分后，voice 回归去重叠单声部语义，需按音源种类分流：

- **voice**（`SoundSource.Kind == Voice`）：resize 推挤邻居——邻居被整个盖住则删除、被盖到一部分则截断边界。
- **instrument**：保持自由重叠，resize 不动邻居。

改动点：`PianoScrollViewOperation.cs` 的 `NoteStartResizeOperation.Move` / `NoteEndResizeOperation.Move`。

## 前置

- 建两轨（或两 part）：一个挂 voice 音源、一个挂 instrument 音源。
- 各摆 3 个相邻 note：A(0–480) B(480–960) C(960–1440)，同音高即可。

## 用例

### 1. voice 尾部右拉截断后邻

拖 A 的尾巴右拉到 B 中间（如 720）。
**预期**：A 尾到 720；B 起点被推到 720、尾仍在 960；C 不动。松手后无重叠。

### 2. voice 尾部右拉吞掉后邻

拖 A 的尾巴一路右拉越过 B 的尾（如拉到 1200）。
**预期**：B 被删除；C 起点被推到 1200、尾仍在 1440。

### 3. voice 头部左拉截断前邻

拖 C 的头左拉到 B 中间（如 720）。
**预期**：C 头到 720；B 尾被截到 720、起点仍在 480；A 不动。

### 4. voice 头部左拉吞掉前邻

拖 C 的头一路左拉越过 B 的起点（如拉到 240）。
**预期**：B 被删除；A 尾被截到 240。

### 5. voice 拖拽中途回撤（实时重放）

按住 A 尾右拉盖到 B 中间（B 已缩短），不松手再拉回 480 以内。
**预期**：B 实时恢复原状（每帧从锚点重放，不留半截）；松手后与初始一致则无 Undo 记录。

### 6. voice 推挤后 Undo/Redo

用例 2 完成后 Ctrl+Z。
**预期**：一步撤回整次拖拽（A/B/C 全部还原，B 复活）；Redo 完整重现。

### 7. voice 波形带内 resize 同样推挤

在下方波形/音素带内拖 note 尾（非音素共享边界处的头拖）。
**预期**:与钢琴区行为一致（voice 推挤）。音素共享边界的头拖仍是原联动钳位行为，不受影响。

### 8. instrument 行为不变（回归）

在 instrument part 里重复用例 1–4 的拖法。
**预期**：邻居完全不动，自由拉出重叠（重叠区不变暗、可画和弦）。

### 9. voice 已有重叠 note 的 resize

在 voice part 里先用旧工程/粘贴制造重叠（或临时切成 instrument 摆重叠再切回 voice），拖其中一个 note 的尾巴越过多个重叠邻居。
**预期**：所有被盖住的邻居按同一规则截断/删除，不崩溃、不漏推。
