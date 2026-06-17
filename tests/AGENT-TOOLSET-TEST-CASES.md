# Agent 工具集测试用例

覆盖范围：内置 AI Agent 的工具集扩充（Layer 1 只读 / Layer 2 业务级写 / Layer 3 `apply_edits` 批量 DSL），以及贯穿的 1-based 寻址、tick 单位、可撤销单位约定。仅测本次受影响范围，不重测既有 agent 基线（侧栏/会话/流式/token 等另见各自文档）。

## 测试前置

1. **构建**：`dotnet build TuneLab-agent\TuneLab\TuneLab.csproj -c Debug`（启动前确保没有别的 TuneLab 实例占单实例锁）。
2. **模型 provider**：在 Agent 侧栏设置页配好一个 OpenAI 兼容模型（已有 API key / base url）。建议用具备稳定工具调用能力的模型。
3. **测试工程**：新建或打开一个含至少 **1 条 midi 轨、轨内至少 1 个 midi part、part 内有若干音符并绑定了 voice** 的工程；另可加 1 条空轨/第二条轨便于多轨用例。记下钢琴窗里音符的实际音高与位置，便于核对。
4. 每个写用例做完后用 **Ctrl+Z 撤销一次**，确认整步一次性回退（验证"一次写 = 一个可撤销单位"）。

> 说明：工具名是模型内部调用的，用户只在聊天框下达自然语言；下表"提示语"是建议输入，模型应自行选对工具。重点核对**工程实际变化**与**撤销行为**，而非模型措辞。

## Layer 1 · 只读

| # | 提示语 | 预期 |
|---|---|---|
| R1 | 「这个工程有哪些轨？」 | 调 `get_project_overview`；回中含 PPQ=480、tempo、拍号、各轨**第 1/2…轨**（1-based）名/part 数/音符数 |
| R2 | 「第 1 轨有几个 part？」 | 调 `get_track_detail(1)`；列出该轨各 part 的 tick 区间、voice、音符数 |
| R3 | 「列出第 1 轨第 1 个 part 的音符」 | 调 `get_part_notes(1,1)`；每音符带 1-based NoteNumber、tick 位置/时长、音高(MIDI+音名如 C4)、歌词 |
| R4 | 「第 1 轨第 1 个 part 在 0 到 1920 tick 的音符」 | `get_part_notes` 带区间过滤；只列起点落 [0,1920) 的音符，编号仍按全 part 序 |
| R5 | 「采样第 1 轨第 1 个 part 的 pitch，0 到 1920，10 个点」 | 调 `get_parameter(parameterId="pitch")`；返回 10 个等距 tick 的取值，无曲线处为 NaN |
| R6 | 「第 9 轨有什么？」（工程只有少数轨） | 越界报错，措辞为 **1-based**（如 "track number 9 out of range [1,N]"），模型转述错误而非编造 |

## Layer 2 · 业务级写（每步撤销验证）

| # | 提示语 | 预期 |
|---|---|---|
| W1 | 「把第 1 轨整体升 2 个半音」 | `shift_track_pitch(1, 2)`；钢琴窗该轨所有音符 +2 半音（钳 0..127）；撤销一次全退 |
| W2 | 「把第 1 轨改名叫 Lead，并静音」 | `set_track_properties`；轨名变 Lead、mute 亮；撤销一次同时退名与静音 |
| W3 | 「第 1 轨增益设 -3 dB，声像偏左 0.5」 | gain=-3dB、pan=-0.5；pan 超 ±1 应被钳 |
| W4 | 「新增一条叫 Harmony 的轨」 | `add_track`；末尾多一条 Harmony 轨，回灌其 1-based 编号；撤销移除 |
| W5 | 「删除最后一条轨」 | `remove_track`；该轨消失；撤销恢复 |
| W6 | 「速度设成 120」 | `set_tempo(120)`；tick 0 处 tempo=120 |
| W7 | 「第 5 小节起改成 3/4」 | `set_time_signature(3,4,atBarNumber=5)`；第 5 小节（1-based）起拍号变 3/4 |

## Layer 3 · apply_edits 批量

| # | 提示语 | 预期 |
|---|---|---|
| B1 | 「在第 1 轨第 1 个 part 写一段 do-re-mi-fa-sol 的旋律，每个音符一拍」 | 单次 `apply_edits` 含多条 `add_note`；钢琴窗一次性出现 5 个音符，tick 间隔 = 一拍(480)；**撤销一次全部消失**（整批一个单位） |
| B2 | 「把刚才那个 part 的第 2 个和第 4 个音符都升一个八度」 | 先 `get_part_notes` 取编号，再 `apply_edits` 两条 `set_note(pitch=+12)`；只这两个音符变；撤销一次同退 |
| B3 | 「删除该 part 第 1 个音符，并把第 3 个音符时长翻倍」 | 单批含 `delete_note(1)` + `set_note(3,dur=…)`；**编号按批开始快照解析**：删第 1 个不会让"第 3 个"错位成别的音符 |
| B4 | 「删除该 part 0 到 960 tick 范围内的音符」 | `delete_notes_in_range(0,960)`；该区间音符消失 |
| B5 | 「在该 part 0 到 1920 tick 画一条从 C4 滑到 C5 的音高线」 | `set_pitch_line`，points 含起止 value=60→72；钢琴窗音高线出现该滑音；撤销一次清除 |
| B6 | 「清掉刚画的音高线」 | `clear_pitch`；音高线被清 |
| B7 | 「把该 part 的某个支持的参数（如能量/动态）在中段拉高」 | `set_automation_line`；若该 voice 声明了对应自动化轨则成功落线，否则回灌"参数不可用"错误而非崩溃 |

## 健壮性 / 边界

| # | 场景 | 预期 |
|---|---|---|
| E1 | apply_edits 里夹一条非法 op（如 noteNumber 越界） | 该条记 ERROR、其余正常施加；回灌 "Applied k/n" 明细；成功部分作为一个 Commit |
| E2 | apply_edits 某条字段缺失（如 add_note 少 pitch） | 解析阶段报 "edit #i is invalid — missing required field"，整批不施加，模型可据错纠正 |
| E3 | 对非 midi part（音频 part）下达音符编辑 | 报 "part X on track Y is not a midi part" |
| E4 | 闲聊/问候（如「你好」） | 不调任何工具，自然语言简短回复 |
| E5 | 切换工程后再编辑 | 工具按新工程重建，编号/内容对应新工程，不串旧工程 |
