# Agent effect 自动化曲线读写（B 支柱写通道）测试用例

覆盖 B 支柱新增的 **effect 参数自动化曲线**脚本原语（挂在 `effect` 句柄上）：
`effect.automationIds()`、`effect.sampleAutomation(id,s,e,n)`、`effect.setAutomation(id,s,e,pts,default?)`、
`effect.clearAutomation(id,s,e)`。与 part（voice）级自动化（`part.setAutomation` 等）**逐一平行**——同 part 相对
tick 空间、绝对值语义、覆盖写口径，只是目标从 voice 换成链中某 effect（对齐 C# `IEffect.Automations`）。

只测本切片。**不复测** part 级自动化基线（`AGENT-TOOLSET-TEST-CASES.md` / `SCRIPT-TOOLS-TEST-CASES.md`）与
effect 链增删排（`AGENT-*` / 见 part.effects 那批）。

## 前置

- 装有样例效果器 `V1.Effect`（声明了可自动化参数：`formant` 恒有 −100..100；`gain_env` 在 “Show Gain Env”
  勾选时有，默认勾选，0..2）。用 `list_effects` 查其 type id（喂 setAutomation/addEffect 用的是 type id、非显示名）。
- 打开 TuneLab，工程里有一个挂了可合成 voice 的 MIDI part。
- Agent 侧栏已连模型；授权档任选（effect 自动化写是工程编辑、过分级授权闸门、落一个可撤销单位）。

## 1. automationIds 读取

给某 part 加一个 V1.Effect（`const fx = part.addEffect("<V1.Effect type>")`），跑 `print(fx.automationIds())`。

**期望**：列出该 effect 引擎声明的可自动化参数 id——含 `formant`；`gain_env` 在 env_enabled（默认 true）时也在列。
与 `list_effects` 报的该引擎参数 schema 一致。

## 2. setAutomation 覆盖写 + sampleAutomation 读回

跑 `fx.setAutomation("formant", 0, tl.ppq*4, [{tick:0, value:-50}, {tick:tl.ppq*4, value:50}])`，
再 `print(fx.sampleAutomation("formant", 0, tl.ppq*4, 5))`。

**期望**：
- 采样值沿 −50→50 线性过渡（端点 ≈ −50 / 50，中点 ≈ 0）；
- 参数面板里该 effect 的 `formant` lane 出现这条曲线（钉选/展开该 effect 参数时可见），颜色/量程与声明一致；
- 作为**一个可撤销单位**、Ctrl+Z 整体撤回；写触发重合成（回显轨 loudness 等随之更新）。

## 3. defaultValue + 按需创建

对一条**尚不存在曲线**的轨跑 `fx.setAutomation("gain_env", 0, tl.ppq*2, [{tick:0,value:1.5}], 1.0)`。

**期望**：轨按需创建、defaultValue 设为 1.0、[0,tl.ppq\*2) 段落成给定点；区间外读回 = defaultValue。

## 4. clearAutomation

在 2/3 写好曲线后跑 `fx.clearAutomation("formant", 0, tl.ppq*2)`。

**期望**：[0, tl.ppq\*2) 段被清空（该段 sampleAutomation 回到无曲线/默认），区间外保留；一个撤销单位。

## 5. 未知轨报错

跑 `fx.sampleAutomation("nope", 0, 480, 3)`。**期望**：报清晰错误（"unknown effect automation "nope"; use one of effect.automationIds()."），整脚本原子回退。
`fx.setAutomation("nope", ...)` 对**引擎未声明**的 id → 报 "not available on this effect (not declared by its engine)."、回退。

## 6. 作用域隔离：effect 轨 ≠ voice 轨

同一 part 上，voice 声明的 automation（如 `part.automationIds()` 里的某轨）与 effect 的 automation 各自独立：
`fx.setAutomation("formant", …)` **不**影响 `part.sampleAutomation(<voice 轨>, …)`，反之亦然。链上多个 effect 各有自己的曲线互不串。

**期望**：两套曲线互不干扰；`fx.automationIds()` 只列本 effect 的、`part.automationIds()` 只列 voice 的。

## 7. 坐标与 part 相对

part 起点不在 0（`part.startPos > 0`）时，`fx.setAutomation` 的 `points.tick` 用**绝对 tick**、内部落库减 part 锚点——
与 `part.setAutomation` 同一换算。`sampleAutomation` 的 start/end 也是绝对 tick。

**期望**：在绝对 tick 处写、绝对 tick 处采到同值（往返一致），与 part 级自动化坐标口径完全一致。
