# Agent 扩展路由（排障能力）测试用例

覆盖新增的 **`list_extension_routing` / `set_extension_routing`** 两件工具，以及为「插件不生效」排障补的
**两处如实标注**：

- `EngineCatalog.AppendEngineList`（voice/instrument/effect 三类引擎列表共用）多包时改报
  `A (ACTIVE) — shadowed: B`（原先只报 `multiple: A, B`，判不出谁生效）；
- `list_extensions` 每条补 `provides <kind>:<identity> — ACTIVE / SHADOWED: …` 行
  （破掉 `status=Loaded` 的误导：加载成功 ≠ 在用）。

只测本切片。**不复测**扩展冲突消解机制本身（`EXTENSION-ROUTING-TEST-CASES.md`：设置窗矩阵、默认规则、
按包分桶设置等）与其它 agent 工具。

## 前置（好消息：冲突夹具本机已装好，无需再造）

路由只在**同一身份 id 被多个包提供**时才有内容。本机 `%APPDATA%\TuneLab\Extensions\` 里已装有现成夹具：

- **Voice Conflict A / B**（`com.tunelab.test.voiceconflict.a` / `.b`）——两包声明**同一个 voice 引擎 id**
  → 冲突行 `voice:<id>`；
- **Route Conflict A / B**（`com.tunelab.test.routeconflict.a` / `.b`）——两包都声明扩展名 `.tlroute` 的
  导入+导出 → 冲突行 `format-import:tlroute` 与 `format-export:tlroute`。

故直接开测即可，不用手搓副本包。其余前置：
- 构建启动：`./run.ps1`（默认 Rebuild + 启动、自动关旧实例）。**本切片无 SDK 改动**，插件无需重新 pack/install。
- Agent 侧栏已连模型；授权档位在对话页 header 胶囊上切。
- 备份 `%APPDATA%\TuneLab\Configs\ExtensionRouting.json`（路由选择存在这个独立文件里，不在 Settings.json）。
- 对照基准：设置窗「扩展路由」页应能看到上述几行（本切片不改那页，只是 agent 侧要与它口径一致）。
- A 组（"无冲突时"）在本机**测不到**——夹具在装。要测就把上述两对夹具临时移出 Extensions 目录再启动，
  或**如实记为未覆盖**。

## A. 无冲突时（本机夹具在装，默认跳过——见前置）

问「有没有插件冲突/被顶替的情况？」

**期望**：`list_extension_routing` 明说**没有任何身份被争用、没有东西被顶替**，并把排查引向别处
（list_extensions 的加载状态/错误、或 list_sound_sources / list_effects 里能力在不在）。
**不要**编造冲突、也不要含糊其辞。

## B. 有冲突时的如实标注（本切片的核心价值）

### B1. list_extensions 点名被顶替者

先用 `list_extension_routing` 看清 `voice:<id>` 当前生效的是 A 还是 B，然后问**被顶替**那个的名字，例如
「我装的《Voice Conflict B》怎么用不了？」（若 B 是被顶替者）。

**期望**：agent 调 `list_extensions`，那一条除了 `status=Loaded` 外**带
`provides voice:<id> — SHADOWED: "Voice Conflict A" provides it instead, so THIS package's implementation is loaded but never used.`**；
agent 的结论**必须**是"它被另一个包顶替了"而不是"装好了应该能用"。生效那个包对应显示 `ACTIVE (also provided by …)`。
`.tlroute` 的两个 format 身份同理（同一包会同时列出 import 与 export 两行）。

### B2. 引擎列表点名生效者

问「装了哪些声库引擎？」→ `list_sound_sources`（无 engine 参数）。

**期望**：争用的那个 voice type 一行显示
`package=<生效包> (ACTIVE) — shadowed: <另一个>; routing conflict, see list_extension_routing`；
不再是分不出胜负的 `multiple: …`。effect/instrument 同理（同一格式化器，本机若无 effect 冲突则不必强测）。

### B3. 路由矩阵

`list_extension_routing`。**期望**：列出 `voice:<id>`、`format-import:tlroute`、`format-export:tlroute` 三行，各列两个候选（含 packageId）、标出 ACTIVE、
说明当前是**默认规则**（内建优先→否则包 id 序最小）还是**用户选定**；末尾解释 kind 取值。
与设置窗「扩展路由」页所见一致。

## C. set_extension_routing（授权档 = Auto）

### C1. 切到用户想要的包

「那就用《Voice Conflict B》那个」（即当前被顶替的那个）。

**期望**：`set_extension_routing("voice", "<id>", "com.tunelab.test.voiceconflict.b")` → 成功，回报**明确要求重启**；
`ExtensionRouting.json` 里出现该键。**重启后**再问一次：`list_extension_routing` 显示 ACTIVE 已切换、
且标注为"用户选定"；`list_sound_sources` 的 ACTIVE 也跟着变；设置窗「扩展路由」页那行的选择同步。

### C2. 清除选择回默认

「还是恢复默认吧」（packageId 省略或空串）。

**期望**：成功，并**如实告知清除后按默认规则会落到谁**；`ExtensionRouting.json` 里该键被删除；重启后确实回到默认。

### C3. 幂等

重复设成当前已选的包 / 在无选择时再清除。**期望**：分别回报 `already set to …` / `already has no explicit choice …`，
**不写盘、不弹卡**。

## D. 校验与拒绝

### D1. 非争用身份

对一个只有单一提供者的身份调 `set_extension_routing`（如某独占 voice 引擎 id、或内建 `format-import:tlp`）。

**期望**：报错「不是被争用的身份、只有被争用的才能路由」+ 提示先 `list_extension_routing`；
**不往设置里写无意义的选择**。

### D2. 不是候选的 packageId

给一个不提供该身份的包 id。**期望**：报错并列出该身份的真实候选 packageId；不写入。

### D3. kind / identity 写错

`kind: "voices"`（多了 s）或 identity 拼错。**期望**：报错 + 指出 kind 取值范围 / 让它重列，不写入。

## E. 授权闸门

- **ReadOnlyAdvice**：不改，回报 "I did NOT make \"X\" the provider of \"kind:identity\""，并指路设置窗「扩展路由」页。
- **Confirm**：卡片文案为「Agent 想让「Voice Conflict B」成为「voice:<id>」的提供包（重启后生效）。」；
  拒绝→不改；应用本次→改、档位不变；始终允许→改 + 切 Auto。

## F. 完整排障链（端到端，本切片的目的）

只问一句：**「我装的《Voice Conflict B》怎么没反应？」**（换成当前被顶替的那个包名）

**期望**：agent 按系统提示的顺序走，且**不停在第一步**——
① `list_extensions` 看状态/错误 + 是否被顶替 → ② `list_extension_routing` 确认争用详情 →
③（必要时）`list_sound_sources` 确认能力在不在 → 给出**正确结论**：不是没装好，而是被某包顶替，
并给两条出路（自己去设置窗「扩展路由」页改 / 让 agent 代改）+ **重启**提醒。

## 回归清单（快速过）

- [ ] 无冲突时明说"没有被顶替"并把排查引向别处（A）
- [ ] `list_extensions` 标 SHADOWED/ACTIVE、`list_sound_sources` 标 ACTIVE + shadowed（B）
- [ ] 选包/清除均落盘、回报要求重启、重启后确实切换（C）
- [ ] 非争用身份/非候选包/kind 写错 一律不写入且报清（D）
- [ ] 三档授权行为正确、卡片文案带"重启后生效"（E）
- [ ] 一句话排障能走完整条链并给出正确结论（F）
