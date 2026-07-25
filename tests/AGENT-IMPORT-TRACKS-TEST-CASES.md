# Agent 导入轨（project.importTracks）测试用例

覆盖 B 支柱新增的**导入**脚本原语 `project.importTracks(path)`：从文件解析出全部轨、**加法式**并进当前工程、
返回新加入的轨句柄。纯数据操作（读入文件 + 加法式写工程），走脚本面同一 commit/回退单位。

**时基语义**（本切片定案）：**保留当前工程的速度/拍号**，各轨按**原始 tick** 落位（对齐小节、不做时基重映射）。
时基对齐 / 导入文件速度等模式暂不做。**导出**不在本切片（另议）。

只测本切片。**不复测** UI 的 Import Track 对话框流程（那条含选轨 + 三种时基模式，是另一路径）。

## 前置

- 备好几个**可导入文件**：一个 `.mid`（多轨更好，验证「全部轨」）、一个 TuneLab 工程 `.tlp`（验证含音源/音符/自动化的整轨往返）。
  记下它们的**本地绝对路径**（喂 `importTracks` 用）。
- 打开 TuneLab，当前工程里已有若干轨（验证「加法式、不动既有轨」）。
- Agent 侧栏已连模型（或用 Script 面板直接跑）。

## 1. 导入 MIDI，加法式并入

记下当前工程轨数 N。跑 `const added = tl.currentProject().importTracks("<某.mid 绝对路径>"); print(added.length); for (const t of added) print(t);`。

**期望**：
- 文件里的**全部轨**被加到工程末尾；轨数变 N + added.length；**既有 N 条轨分毫未变**（加法式）；
- `added` 是新轨句柄数组、可继续用（读名字/parts/notes）；
- 音符落在其**原始 tick**（按小节对齐当前工程），当前工程**速度/拍号不变**；
- 整段脚本 = **一个可撤销单位**，Ctrl+Z 一步撤掉本次导入的所有轨。

## 2. 导入 .tlp，整轨保真

跑 `importTracks("<某.tlp 绝对路径>")`。

**期望**：导入轨含其 part/音符/**音源**/effect/自动化——音源已装则正常挂上、未装则优雅降级为空源（同 UI 导入，不崩）；
part 里的音高线/自动化曲线保真。

## 3. 导入结果可即刻操作（数据流水线）

跑：`const [t] = tl.currentProject().importTracks("<某.mid>"); for (const p of t.parts()) for (const n of p.notes()) n.pitch += 12;`。

**期望**：导入 + 对导入内容改写发生在**同一个可撤销单位**里；导入的音符被整体升八度；Ctrl+Z 一步全撤。
（体现「导入产出的是可继续操作的工程数据」。）

## 4. 错误与原子回退

- `importTracks("<不存在的路径>")` → 报清晰错误（"cannot import ...: ..."），工程**分毫未变**、无新增撤销项。
- `importTracks("<某.txt 或不支持扩展名>")` → 报「格式不支持」类错误、回退。
- `importTracks("")` → 报 "import path is required."、回退。
- 导入**之后**脚本再抛错 → 已导入的轨**一并回退**（一个原子单位）。

## 5. 空/无轨文件

导入一个**无轨**的合法工程文件（若有）——`importTracks` 返回空数组、工程不变、不报错、不产生撤销项（无改动）。

## 6. 坐标口径

对一个已知内容的单轨 .mid（如首音符在第 2 小节头）导入后，读 `added[0].parts()[0].notes()[0].pos`：
应为该音符的**绝对 tick**（part 锚点已加回，与 `part.notes()` 的其它读一致）。
