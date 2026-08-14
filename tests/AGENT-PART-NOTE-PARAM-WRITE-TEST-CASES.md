# Agent part/note/phoneme 参数改写（B 支柱写通道）测试用例

覆盖 B 支柱新增的 **voice/instrument part/note/phoneme 参数改写**脚本原语（`run_script` / `run_saved_script` /
`run_in_sandbox` 共用同一 `tl` 面）：

- **第一层**：`part.getProperty/setProperty`、`note.pronunciation`、`note.getProperty/setProperty`——音源声明的
  per-part / per-note 标量参数 + 发音覆盖。
- **第二层**：`note.phonemes()`、`note.addPhoneme/removePhoneme/lockPhonemes/clearPhonemes`、`note.bodyOffset` +
  `phoneme` 句柄（`symbol/duration/stretchWeight` + `getProperty/setProperty`）——音素读 + 编辑（合成态只读、首次写自动钉死）。

只测本切片受影响范围。**不复测**既有 run_script/脚本库/环境感知（`SCRIPT-TOOLS-TEST-CASES.md`、
`AGENT-SAVED-SCRIPT-LOOP-TEST-CASES.md`、`AGENT-ENVIRONMENT-AWARENESS-TEST-CASES.md`），也不复测音素显示/布局
基线（`PHONEME-*-TEST-CASES.md`）——本切片只经脚本面**写数据**，显示/布局仍由既有 DisplayPhonemes 承担。

## 前置

- 本机装有**至少一个可合成的 voice 音源**（真实引擎，非内建空引擎），且该音源**声明了** per-part / per-note /
  per-phoneme 自定义属性（样例插件 `V1.Voice` / `V1.Suite.Voice` 声明了这些，可作夹具；用 `list_sound_sources`
  的第三层查该音源到底声明了哪些键/范围/默认）。
- 另备一个 **instrument 音源**（验证 voice-only 边界，用例 8）。
- 打开 TuneLab，新建或打开一个工程，建一个挂了上述 voice 音源的 MIDI part、里面几个带合法歌词的音符。
- Agent 侧栏已连模型；授权档任选（工程写会过分级授权闸门，与既有 apply_edits/setSoundSource 一致）。

> 提示：改音源参数 / 音素会触发**重合成**，可能要等几秒引擎跑完才看到音频/回显更新——属正常。

## 1. note.pronunciation 读写（发音覆盖）

让 agent 跑一段 `run_script`：读某音符 `n.pronunciation`（初始应为空串），设 `n.pronunciation = "<该语种一个合法发音>"`。

**期望**：
- 脚本作为**一个可撤销单位**落库；该音符发音被强制为所设值，重合成后音素/音频按新发音走（可在钢琴窗音素带核对）；
- 再设 `n.pronunciation = ""` → 回到按歌词自动派生（音素回到默认 G2P 结果）；
- 单字段赋值是唯一写法（批量入口 `note.set({...})` 已随对称性整改删除）。

## 2. part 级声明参数 getProperty/setProperty

先 `list_sound_sources`（engine+source）拿到该 voice 的 **part 级** schema（键/类型/范围/默认）。让 agent 跑：
读 `part.getProperty("<键>")`（未设应返回 `null`）→ `part.setProperty("<键>", <合法值>)` → 再读回。

**期望**：
- 写前 `null`、写后读回**所设值**（number/bool/string 三类型都试一个）；
- 值落进 `part.Properties`、随重合成生效；作为一个撤销单位、Ctrl+Z 整体撤回；
- 侧栏 **Part 属性面板**（若可见）显示同一值（同源数据，双向一致）。

## 3. note 级声明参数 getProperty/setProperty

同上，用 **note 级** schema。让 agent 对某音符 `n.getProperty/setProperty` 一个声明键往返。

**期望**：写前 `null`、写后读回所设值；侧栏 **Note 属性面板**（选中该音符时）显示同值；一个撤销单位。

## 4. 音素读（合成后）

对一个**已合成**的音符跑 `for (const p of n.phonemes()) print(p.symbol, p.leading, p.duration, p.stretchWeight)`。

**期望**：
- 列出该音符的音素（引导在前、主体在后，时间序），`leading` 正确区分核前辅音 / 核及核后；
- 符号是引擎对该歌词/发音的真实产物；`duration` 秒、`stretchWeight`（辅音≈0、元音>0）合理；
- **未合成**的音符（或空 note）`phonemes()` 返回空数组、不报错。

## 5. 音素编辑：合成态首次写自动钉死

对一个**合成态**（`n.hasLockedPhonemes === false`）音符，跑：取 `const ps = n.phonemes()`，改 `ps[0].symbol = "<另一合法符号>"`。

**期望**：
- 写入前该音符 `hasLockedPhonemes` 为 `false`，写入后变 `true`（自动 `LockPhonemes` 物化——与侧栏面板首次编辑
  音素一致）；
- `ps[0]` 句柄写后仍解析到正确音素（钉死后按 `(leading, localIndex)` 映射到新 IPhoneme），符号已改；
- 整段脚本 = **一个撤销单位**：Ctrl+Z 一步既撤销符号改动、又撤销钉死（回到合成态）；
- 同理试 `ps[i].duration = 0.12`、`ps[i].stretchWeight = 2`。

## 6. 音素增删 + bodyOffset

对某音符跑：`n.addLeadingPhoneme({symbol:"<x>", duration:0.05})`（前置辅音）、
`n.addBodyPhoneme({symbol:"<y>", stretchWeight:1})`（主体元音）；再 `n.removePhoneme(<某句柄>)`；再读 / 写 `n.bodyOffset`。
（引导 / 主体是两个独立列表，故是两个方法——原先那个 `leading:` 布尔参数已删。）

**期望**：
- add 后 `n.phonemes()` 多出对应项、归到正确列表（leading/body）；返回的句柄可继续读写；
- remove 后该项消失；**其后音素句柄下标前移**——文档已说明结构变更后应重取 `n.phonemes()`，用旧句柄指到错位/越界
  时报清晰错误（"no longer present (structure changed)"），不静默改错音素；
- `n.bodyOffset` 可读写（写自动钉死）；改 bodyOffset 后音素带引导/主体结合线位置随之变。

## 7. phoneme 级声明参数（若音源声明了音素 slot 属性）

`list_sound_sources` 若显示该 voice 声明了 phoneme slot 属性：对某音素 `p.setProperty("<键>", <值>)` 再 `p.getProperty` 读回。

**期望**：
- setProperty 自动钉死该 note；读回所设值；侧栏 **Phoneme 面板**对应 slot 显示同值；
- 合成态（未钉死）音素 `getProperty` 返回 `null`（音素属性只在钉死后作为可编辑数据存在）——与文档一致。

## 8. voice-only 边界（instrument）

把某 part 切到 **instrument** 音源（`part.setSoundSource({kind:"instrument", ...})`）。对其音符跑 `n.phonemes()`、
`n.addBodyPhoneme(...)`、读 `n.pronunciation`。再对该 part / note 试 instrument **声明的** part/note 参数 getProperty/setProperty。

**期望**：
- instrument 无音素概念：`n.phonemes()` 返回空、音素编辑对其无意义（addPhoneme 技术上会建钉死数据但引擎不消费——
  可接受；重点是不崩、不污染）；`pronunciation` 对 instrument 无意义（字段有值但引擎不用）；
- part/note 的 `getProperty/setProperty` 对 **instrument 声明的**参数照常工作（自定义属性体系 voice/instrument 通用）。

## 9. 错误与原子回退

- `part.setProperty("k", {})` / `n.setProperty("k", [1,2])`（非 number/bool/string）→ 报清晰错误
  （"... value must be a number, boolean, or string."），**整段脚本回退**、工程分毫未变。
- 对 **audio part**（非 midi）调 `getProperty/phonemes` 等 → 报 "not a MIDI part" 类错误、回退。
- 空 key（`setProperty("", 1)`）→ 报 "key is required." 、回退。
- 脚本中途抛错 → 之前的属性/音素写入**全部回滚**（一个原子单位），符合既有 run_script 语义。

## 10. 沙箱一致性（可选，若已装真音源）

在 `run_in_sandbox` 里造 1-track/1-part、挂真 voice、`addNote` 合法歌词、`sandbox.synthesize(part)` 后，用
**同一套** `n.phonemes()` / `p.symbol = ...` / `n.getProperty` 原语改音素与参数。

**期望**：沙箱里这些写原语与真实工程行为一致（沙箱写不过授权闸门、不碰用户工程）；`sandbox.syllable(note)` 与
`n.phonemes()` 对同一音符报同样的符号序。
