# Agent 探测沙箱（run_in_sandbox）+ tl 可选参修复 测试用例

覆盖 F 支柱「探测沙箱」验证桩：agent 在一个**可丢弃的无头工程**里跑 JS，用同一 `tl` 动作面造场景、
真触发离线合成、读回真实音素——够到静态读（list_sound_sources）够不着的东西。并覆盖同批顺带修的
**tl API 可选参 bug**（少传尾部可选参数不再报「No public methods…」）。

只测本切片受影响范围，不复测既有 run_script/脚本库/环境感知基线（分别在 `SCRIPT-TOOLS-TEST-CASES.md`、
`AGENT-SAVED-SCRIPT-LOOP-TEST-CASES.md`、`AGENT-ENVIRONMENT-AWARENESS-TEST-CASES.md`）。

## 前置

- 本机装有**至少一个可合成的 voice 音源**（真实引擎，非内建空引擎）。
- 打开 TuneLab（任意工程；沙箱与当前工程无关）。Agent 侧栏已连上模型。
- 授权档任意——沙箱写入不碰用户数据、**不过授权闸门**，各档表现应一致（见用例 3）。

## 1. 全链路合成探测（核心桩）

让 agent 用 `run_in_sandbox` 跑一段脚本：`sandbox.voices()` 取一个真实音源 → `tl.currentProject().addTrack()`
+ `track.addPart({startPos:0,endPos:1920})` → `part.setSoundSource({kind:"voice", type, id})` → `part.addNote({pos:0,dur:480,pitch:60,lyric:"la"})`
→ `sandbox.synthesize(part)` → `sandbox.syllable(note)`。

**期望**：
- `synthesize` 返回 `{done:true, dispatches>=1, ms>0, timedOut:false}`；
- `syllable(note).symbols` 是**非空的真实音素序**（引擎对该歌词的产物，如元/辅音符号）；
- 工具结果里能看到 print 的过程 + 最后的结论文本；
- 全程无异常、无 UI 卡顿（合成在专用后台线程驱动）。

> 这一条同时验证最吓人的三件事：非 UI 线程上 SyncContext 泵 + 驱动循环 + 真 voice 引擎 bootstrap 成立。

## 2. voices() 如实镜像（含空引擎）

让 agent 跑 `print(sandbox.voices().length); for (const v of sandbox.voices()) print(v);`。

**期望**：列出本机全部 voice 音源，**含内建空引擎那一项**（type 或 id 为空、名为「空声源/Empty」）——
沙箱不自造「只列可合成音源」的分歧契约，与普通 project 枚举一致。模型自行跳过空项即可（无需宿主过滤）。

## 3. 隔离性：不碰用户工程、不需授权

1. 记下当前工程的轨道数 / 某 part 音符数。
2. 让 agent 在**任意授权档**（尤其 Confirm / ReadOnlyAdvice）下用 `run_in_sandbox` 大改沙箱工程
   （加多轨、加音符、挂音源、合成）。
3. **期望**：
   - **不弹**授权升级卡片、**不**产生「WOULD apply…」只读建议——沙箱写入完全放行；
   - 用户当前工程**分毫未变**（轨道数 / 音符数不变、无新增撤销项、Ctrl+Z 撤不出沙箱的改动）；
   - 脚本正常跑完、返回结果。

## 4. 预算护栏（超时 / 派发次数）

1. `sandbox.synthesize(part, {timeoutMs: 1000})`（对一个较重的合成人为设极短时限）——
   **期望**：到点返回 `{timedOut:true, done:false}`，**不**抛异常、**不**死锁、UI 不冻。
2. `sandbox.synthesize(part, {maxDispatches: 1})` 对一个需多段合成的 part ——
   **期望**：派发达上限即停，`done` 可能为 false、如实报 `dispatches`。
3. 运行中点侧栏「停止」——**期望**沙箱合成被取消，工具回报 cancelled，不留悬挂线程。

## 5. tl 可选参修复：少传尾部可选参数

在 Script 侧栏或 `run_script` / `run_in_sandbox` 里跑（任选其一，验的是同一套 tl API）：

```js
const p = tl.currentProject();
const t = p.addTrack();                     // 省 name?
p.setTempo(140);                            // 省 atTick?
p.setTimeSignature(3, 4);                   // 省 atBar?
const part = t.addPart({ startPos: 0, endPos: 1920 });
part.setAutomation("Volume", 0, 480, [{tick:0,value:0}, {tick:480,value:1}]);  // 省 defaultValue?
"ok";
```

**期望**：全部正常执行、返回 `"ok"`，**不**报「No public methods with the specified arguments were found」。
（修法=给这 4 处可选参补 C# 默认值；Jint 对缺失尾参不自动补 undefined，只有形参带默认值才允许省略。）

## 5b. part.setSoundSource（B 支柱：切换音源，真实 tl 写原语）

`setSoundSource` 是**正常 tl 写**（真实编辑器 / 沙箱通用），不再是沙箱专属。在**真实工程**里经 `run_script`（或
沙箱里）验证：

1. **切换到合法音源**：`part.setSoundSource({kind:"voice", type, id})`（type/id 取自 list_sound_sources）——
   **期望**：part 音源切换、`part.soundSource()` 反映新值；合成管线重建（真实工程里编辑器自动重合成）；一个可撤销单位。
2. **未知音源报错**：`part.setSoundSource({kind:"voice", type:"nope", id:"nope"})` ——
   **期望**：报错「no voice source with type=… id=…; use list_sound_sources…」，**不**静默回退空源。
3. **kind 默认 voice / 非法 kind**：省 kind 按 voice 处理；`kind:"xxx"` → 报错「kind must be "voice" or "instrument"」。
4. **清空**：`part.setSoundSource({type:"", id:""})` ——**期望**清成空声源（无音源 part），不报错。
5. **授权闸门**：在真实工程经 agent 的 `run_script` 调用时，与其它工程写一样过分级授权（Confirm 档弹卡片、
   ReadOnlyAdvice 只报不改）——它是普通工程写、非沙箱。

## 6. 必填参缺失仍如实报错（预期行为，非缺陷）

```js
tl.currentProject().tracks()[0].addPart({ startPos: 0 });   // 缺必填 endPos
```

**期望**：报错（字段校验或 arity）——这是**预期**行为，`endPos`/`pos`/`dur`/`pitch` 等无 `?` 标记的是必填项，
漏了就该失败。文档（get_script_api / ScriptDoc）以尾缀 `?` 区分可选，无 `?` 即必填。

## 回归检查（不应被破坏）

- **普通 run_script 不受影响**：既有内联脚本在三档授权下行为不变（预览 / 确认 / 直提交、blocked wait-retry、
  出错整段回退）——沙箱是独立执行路径，不经 ScriptWriteExecutor。
- **可选参补默认值不改语义**：带全参调用（`addTrack("x")` / `setTempo(120, 480)` / `setAutomation(..., 0.5)`）
  行为与修改前一致；只是**额外允许**省略。
- **As\*OrNull 辅助改收 `JsValue?`**：其它调用点（各句柄 Set/Opt\* 路径）不受影响，null 与 undefined 同等处理。
- **合成回显读取路径**：沙箱 `syllable(note)` 读的是 `note.SynthesizedSyllable`（与钢琴窗音素显示同源），
  语义与正常工程一致。
