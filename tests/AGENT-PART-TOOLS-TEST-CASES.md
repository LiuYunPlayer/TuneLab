# Agent part 级工具测试用例

覆盖范围：内置 AI Agent 新增的 **part 级增删/属性**工具——`add_part` / `remove_part` / `set_part_properties`，以及其引出的 **part 寻址**约定（part 按起点 tick 排序、编号随增删/移动而变、新建后以回灌编号往里写音符）。仅测本次新增范围，agent 工具集基线（Layer 1/2/3、1-based、tick、可撤销单位）另见 `AGENT-TOOLSET-TEST-CASES.md`，不在此重测。

## 测试前置

1. **构建**：`dotnet build TuneLab-agent\TuneLab\TuneLab.csproj -c Debug`（启动前确保没有别的 TuneLab 实例占单实例锁）。
2. **模型 provider**：在 Agent 侧栏设置页配好一个具备稳定工具调用能力的 OpenAI 兼容模型。
3. **测试工程**：
   - 准备一条 **空 midi 轨**（无任何 part）用于 P1/P2「从零写旋律」。
   - 另准备一条含 **2~3 个 midi part** 的轨（part 起点彼此不同）用于删除/移动用例。
   - 记下钢琴窗里各 part 的起点/长度，便于核对编号变化。
4. 每个写用例做完后用 **Ctrl+Z 撤销一次**，确认整步一次性回退（验证"一次写 = 一个可撤销单位"）。

> 工具名是模型内部调用的，用户只在聊天框下达自然语言；下表"提示语"是建议输入。重点核对**工程实际变化 + part 编号 + 撤销行为**，而非模型措辞。

## 用例

| # | 提示语 | 预期 |
|---|---|---|
| P1 | （选中那条空 midi 轨）「在第 N 轨从第 1 小节开始写一段 4 小节的旋律」 | 模型先调 `add_part`（pos=该小节起点 tick、dur≈4 小节）建容器，回灌新 part 的 1-based 编号；再用该编号调 `apply_edits` 写音符。钢琴窗：空轨出现一个 part，内含音符。撤销分两步（写音符 / 建 part）各自一次回退 |
| P2 | 「在第 N 轨 tick 3840 处加一个 2 小节的空 part，叫 Verse」 | `add_part(trackNumber=N, pos=3840, dur=≈2 小节, name="Verse")`；轨内出现名为 Verse、起止 [3840, 3840+dur] 的空 part；回灌其编号；撤销移除 |
| P3 | 「把那条轨第 1 个 part 删掉」 | `remove_part(N,1)`；该 part 消失，其余 part 仍在、编号顺延；撤销恢复 |
| P4 | 「把第 N 轨第 2 个 part 改名叫 Chorus」 | `set_part_properties` 只改 name；起止不变；回灌仍报当前编号；撤销退名 |
| P5 | 「把第 N 轨第 1 个 part 往后移到 tick 7680」 | `set_part_properties(pos=7680)`；该 part 起点移到 7680，**且因 part 按起点排序、其 1-based 编号可能改变**——核对回灌的"变更后编号"与钢琴窗一致；撤销回原位 |
| P6 | 「把第 N 轨第 1 个 part 拉长到 4 小节」 | `set_part_properties(dur=≈4 小节)`；该 part 末端延长；撤销回退 |
| P7 | 「往第 N 轨那个新建的 part 里再加几个音符」（紧接 P1/P2 之后同一会话） | 模型用 P1/P2 回灌过的 part 编号继续 `apply_edits`，**不应**假设编号为 count+1、也不应再新建 part；音符落进同一个 part |

## 寻址与边界

| # | 提示语 / 操作 | 预期 |
|---|---|---|
| E1 | 「删除第 N 轨第 99 个 part」（不存在） | 越界报错，措辞 **1-based**（如 "part number 99 out of range [1,M] on this track"），模型转述而非编造 |
| E2 | 「在第 99 轨加个 part」（轨不存在） | 越界报错，措辞 1-based（"track number 99 out of range [1,K]"） |
| E3 | 让模型对某条 **audio** part 调 `remove_part` | 可正常删除（remove_part 对 midi/audio 均可）；而对 audio part 写音符（apply_edits）应仍报 "not a midi part" |
| E4 | add_part 给 dur=0 或负数 | 报错 "dur must be positive."，模型据此纠正 |

## 回归（确认未破坏既有路径）

- R-1：原有 `add_track` 仍在**末尾**追加轨、回灌 count（part 排序逻辑不影响轨）。
- R-2：`apply_edits` 对既有 part 的 note/pitch/automation op 行为不变（part 解析走同一 `ResolvePart` 重构后路径）。
- R-3：手动在轨道窗双击建 part、拖动移动 part 行为不变（数据层 `CreatePart`/`InsertPart`/`RemovePart` 未改）。
