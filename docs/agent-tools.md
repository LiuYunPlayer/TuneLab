# Agent 工具集设计

TuneLab 内置 AI Agent 通过"工具"读取与编辑当前工程。本文档说明工具的分层结构、贯穿全局的寻址/单位/可撤销约定，以及如何新增工具。面向维护者，也作为编写工具描述（喂给模型）时的一致性参考。

## 总体结构

```
模型  ──tool call(JSON)──►  IAgentTool 实现        （解析 JSON、格式化回灌文本）
                              │  调用
                              ▼
                          IAgentProjectEditor      （数据层命令边界：实体解析、单位、提交）
                              │  调用
                              ▼
                          TuneLab.Data             （IProject/ITrack/IMidiPart/INote…）
```

- **IAgentTool**（`TuneLab/Agent/IAgentTool.cs`）：一个工具对模型的声明（名称/描述/参数 JSON Schema）+ 执行入口。实现保持薄：解析模型给的参数 JSON、调用 editor、把结果（或错误）格式化成文本回灌给模型。
- **IAgentProjectEditor**（`TuneLab/Agent/IAgentProjectEditor.cs`）：工具与工程数据之间的门面，把对 `TuneLab.Data` 具体 API 的依赖收口在这一层。所有写操作在这里落到命令系统。数据层重构时只需改本层实现，工具与循环不受影响。
- **AgentRunner**（`TuneLab/Agent/AgentRunner.cs`）：provider 无关的多轮工具循环，只依赖 `IAgentModelSession`。

工具在 `AgentSideBarContentProvider.SetProject` 处实例化并注册到工具清单。

## 三条铁律

1. **寻址 1-based。** 所有 `trackNumber` / `partNumber` / `noteNumber` 都是面向模型的 1-based 序号——"第 1 轨"即首轨，贴合用户认知。editor 在解析助手（`ResolveTrack` / `ResolveMidiPart` / `ResolveNote`）的边界处一次性 −1 转成 0-based 内部寻址；越界报错也用 1-based 措辞。回灌给模型的文案同样按 1-based 标号，两端自洽——绝不出现"模型看到第 1 轨却要传 0"的错位。

2. **单位用 tick，且位置全为绝对（全局）tick。** 位置与时长一律为 tick，PPQ = 480（`MusicTheory.RESOLUTION`）。模型侧所有位置都是**绝对 tick**——与 playhead / `get_project_overview` / 小节同坐标系；editor 在落数据时减去 part 起点（`part.Pos`）转成数据层的 part 相对坐标，读时加回。**模型永不做坐标换算/减 part 起点**（实测弱模型做大数减法极易错，故把换算收口到 editor）。不向模型暴露秒——跨变速时 tick↔秒非线性、量化到网格不便。拍号的小节号对模型按 1-based 展示。

3. **每次写 = 一个可撤销单位。** 撤销边界是 `IProject.Commit()`：多次 mutation 累积成命令，Commit 合并为一个 CompositeCommand。每个业务级写工具在完成后单次 Commit；批量 `apply_edits` 整批一个单位。`BeginMergeDirty/EndMergeDirty` 只合并 UI 通知，不是撤销边界。

## 工具分层

### Layer 1 · 只读上下文
不改数据、不进命令系统，只把工程明细格式化回灌作上下文。

| 工具 | 作用 |
|---|---|
| `get_project_overview` | PPQ、tempo、拍号、各轨（1-based 编号/名/静音独奏/增益声像/part 数/音符数） |
| `get_track_detail` | 某轨属性 + 各 part（1-based 编号、tick 区间、voice、音符数） |
| `get_part_notes` | 某 midi part 的音符（1-based NoteNumber、tick 位置/时长、音高 MIDI+音名、歌词；可按 tick 区间过滤） |
| `get_parameter` | 采样某 part 的参数曲线（`"pitch"` 或某自动化轨 id）在 tick 区间上的取值 |

### Layer 2 · 业务级写
贴用户意图、低参数量；每次调用一个可撤销单位。

| 工具 | 作用 |
|---|---|
| `shift_track_pitch` | 整轨音符升降若干半音 |
| `set_track_properties` | 改轨名/静音/独奏/增益(dB)/声像（只改所给字段） |
| `add_track` / `remove_track` | 增删轨 |
| `set_tempo` | 设速度（无 atTick 改 tick 0 基础速度，有则设/加标记） |
| `set_time_signature` | 设拍号（atBarNumber 为 1-based 小节号） |

### Layer 3 · 批量 DSL
`apply_edits`：把一串逐字段编辑（op-DSL）作为**一个**可撤销单位施加。适合写旋律、批量改音符、画参数曲线——比逐个业务工具往返省 token，且整批一起撤销。

"原子/逐字段操作"作为 op-DSL 的词汇存在，不各自升成顶层工具（避免撑爆工具清单）。op 词汇定义在 `TuneLab/Agent/EditOp.cs`，解析（JSON→类型化 op）在 `ApplyEditsTool`，执行在 `IAgentProjectEditor.ApplyEdits`。

支持的 op（每条带 `trackNumber`、`partNumber`，外加各自字段）：

| op | 字段 | 说明 |
|---|---|---|
| `add_note` | pos, dur, pitch, lyric? | 新增音符 |
| `set_note` | noteNumber, pitch?, pos?, dur?, lyric? | 改字段（改 pos/dur 触发摘除-重插维持有序） |
| `delete_note` | noteNumber | 删指定编号音符 |
| `delete_notes_in_range` | start, end | 删 [start,end) tick 内音符（按起点判定） |
| `set_pitch_line` | start, end, points | 清空 [start,end) 再落线；point.value = 绝对 MIDI 音高（可含小数，60=C4） |
| `clear_pitch` | start, end | 清空音高曲线 |
| `set_automation_line` | automationId, start, end, points, defaultValue? | 清空再落线；point.value = 参数绝对值；轨不存在按需创建 |
| `clear_automation` | automationId, start, end | 清空某参数轨曲线 |

`points` 形如 `[{"tick":0,"value":60},{"tick":480,"value":62}]`。

**批内编号语义（关键）：** `noteNumber` 对"批开始时"的音符顺序**快照**解析、按对象引用施改，故同批内的删/插不影响后续 op 的编号；但本批新增的音符在同批内不可按编号再寻址。需要对新增音符再操作的，分两次 `apply_edits`（中间用 `get_part_notes` 取回新编号）。

`apply_edits` 逐 op 施加并各自 try/catch，单个 op 的解析/解析失败只记错跳过、不拖垮整批；成功的部分作为一个 Commit 落地，回灌每条 op 的 OK/ERROR 明细。

## 新增一个工具

1. 若需新的数据能力，在 `IAgentProjectEditor` 加方法 + 在 `ProjectAgentEditor` 实现（沿用 1-based 解析助手、tick 单位；写方法末尾按需 `project.Commit()`）。
2. 写一个 `IAgentTool` 实现：`Name`/`Description`/`ParametersJsonSchema` + `ExecuteAsync`（用 `ToolJson` 助手解析参数、catch 异常转错误文本）。
3. 在 `AgentSideBarContentProvider.SetProject` 的工具清单里按层注册。
4. 工具描述里务必声明 1-based 与 tick 约定，与既有工具措辞一致。

若要扩 `apply_edits` 的 op：在 `EditOp.cs` 加 record、在 `ApplyEditsTool.ParseOp` 加分支、在 `ProjectAgentEditor.ApplyOne` 加施加逻辑、在工具描述里补该 op 行。
