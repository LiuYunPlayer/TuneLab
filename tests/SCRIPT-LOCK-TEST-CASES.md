# 脚本面「固定」（合成音高 / 合成参数）测试用例

> 本文只覆盖**本次改动的受影响范围**：把固定（`SynthesisLock`）暴露成脚本动作。
>
> 新增 API（`part` / `effect` 句柄）：
>
> | API | 作用 |
> |---|---|
> | `part.lockPitch(start?, end?)` → `bool` | 合成音高 → 本 part 的音高曲线 |
> | `part.lockAutomation(id, start?, end?)` → `bool` | 该轨的合成参数 → 同 id 的可编辑轨 |
> | `part.hasSynthesizedParameter(id)` → `bool` | 该轨有没有配对合成参数（有才谈得上固定） |
> | `effect.lockAutomation(id, start?, end?)` / `effect.hasSynthesizedParameter(id)` | 同上，作用域是本 effect 的轨 |
>
> 三条口径是本次要验的重点：
>
> 1. **区间两参成对**：都给 = 固定该段；都不给 = 整条 part；只给一个 = 报错（不替调用方猜边界）。
> 2. **返回 `bool` = 有没有真的固定到东西**：没有产物（通常是还没合成）是 no-op 返回 `false`，不是报错；
>    而**用法错误**（未知 id / 无配对合成参数）是报错，整段脚本随之回退。两条路刻意分开。
> 3. **与笔刷同一份实现**：扣 vibrato 偏移、简化、裁剪、秒→tick 换算全走 `SynthesisLock`，
>    故笔刷已验过的性质（幂等、颤音不翻倍、分段轨留空隙）在脚本面应当同样成立。
>
> UI 侧固定（右键菜单 / 固定笔刷）已有独立文档 `READBACK-PAIRING-LOCK-TEST-CASES.md`，本文不重复，
> 只在末尾做一次**两条路结果一致**的对照。
>
> 每个用例：**做什么 → 预期看到什么**。

## 准备

```bash
dotnet build TuneLab.sln -c Debug
```

**`v1-effect` 这次改了声明**（Gain 新增一条 `loudness` 实参轨），故它**必须重新打包安装**，否则跑的还是旧
dll、用例 11 之后全对不上。TuneLab 必须先关闭：

```bash
dotnet build tests/plugins/V1.Effect/V1.Effect.csproj -t:Rebuild -c Debug
powershell -File tests/pack-tlx.ps1
powershell -File tests/install-tlx.ps1 v1-effect
```

`v1-voice` 未改，已装过就不必动（没装过则把上面三行里的 `V1.Effect` / `v1-effect` 换成 voice 的再跑一遍）。

夹具与 UI 显示名（voice 侧同 `READBACK-PAIRING-LOCK-TEST-CASES.md`）：

| 名称 | 位置 | 是什么 | 脚本里的 id |
|---|---|---|---|
| **Alice (V1 Test)** | 音源选择器 | v1-voice 的声库 | — |
| **Energy** | 参数区标题栏 chip（粉） | 只读合成参数轨 | `energy`（配对） |
| **Energy (Actual)** | 底部 tabbar（橙） | 与合成参数同 id 的可编辑**分段**轨 | `energy` |
| **Energy** | 底部 tabbar（紫） | 偏差轨，**不配对** | `energy_offset` |
| **Gain** | 效果器选择器 | v1-effect 的效果器 | — |
| **Loudness** | 参数区标题栏 chip（蓝） | 只读合成参数轨 | `loudness`（配对） |
| **Loudness (Actual)** | 底部 tabbar（绿） | **本次新增**：与合成参数同 id 的可编辑**连续**轨 | `loudness` |
| **Gain Env** | 底部 tabbar（橙） | 连续轨，**不配对** | `gain_env` |
| **Formant** | 底部 tabbar（青） | 分段轨，**不配对** | `formant` |

**脚本自己不会触发合成**（`tl` 面没有 synthesize 入口，那是探测沙箱的专属原语）。所以凡是要"有产物"的用例，
都请先让该 part 合成完（播放一次或等自动合成跑完，参数区看得见粉色 Energy 合成参数曲线）再运行脚本。

运行方式：侧栏 **Script** 面板贴代码 → 运行（run-once 脚本，不必写 `getScriptInfo`）。`print` 的输出显示在面板里。

---

## 一、无产物：如实返回 false

### 用例 1 —— 还没合成就固定

新建工程 → 建一轨、挂 **Alice (V1 Test)** → 写 3 个音符 → **不要**播放（保持未合成）→ 运行：

```js
const p = tl.currentPart();
print("pitch=" + p.lockPitch());
print("energy=" + p.lockAutomation("energy"));
```

**预期**：两行都是 `false`。音高线与 Energy (Actual) 轨都**没有**新增锚点；撤销栈里**没有**多出一个空步骤
（按 Ctrl+Z 撤的是你写音符那一步，不是脚本这一步）。

> 这条是本次改动的核心诉求：脚本/agent 那边没人盯着屏幕，静默 no-op 会被当成"已固定"。

---

## 二、合成音高 → 音高曲线

### 用例 2 —— 整条 part 固定（省略区间）

用例 1 的工程，**先播放让它合成完**（能看到合成音高的浅色曲线）→ 运行：

```js
print(tl.currentPart().lockPitch());
```

**预期**：输出 `true`。钢琴窗里音高曲线**变成模型那条线**（与合成音高重合，抬手即视觉上二线合一）。
按一次 **Ctrl+Z** 全部退回（整段脚本 = 一个可撤销单位）。

### 用例 3 —— 只固定一段

撤回到未固定态 → 运行（第 2 小节 = tick 1920..3840，按你的 part 位置调整）：

```js
const p = tl.currentPart();
print(p.lockPitch(1920, 3840));
```

**预期**：`true`。**只有** 1920..3840 这段音高曲线出现锚点，两侧仍是空白（模型线照旧只作浅色显示）；
段边界处曲线**接在边界上**、不越界。

### 用例 4 —— 幂等：反复固定同一段不逐次抬升

在有颤音的区域（画一个 vibrato）先合成，再连跑两次：

```js
const p = tl.currentPart();
p.lockPitch(1920, 3840);
p.lockPitch(1920, 3840);   // 第二次
print("done");
```

**预期**：颤音幅度**不翻倍**（第二次固定后曲线形状与第一次一致）。这验的是写入前扣掉了 vibrato 偏移。

---

## 三、合成参数 → 同 id 可编辑轨

### 用例 5 —— 配对判据（先问后做）

合成完的 Alice part 上运行：

```js
const p = tl.currentPart();
print("energy=" + p.hasSynthesizedParameter("energy"));
print("energy_offset=" + p.hasSynthesizedParameter("energy_offset"));
print("piecewiseIds=" + JSON.stringify(p.piecewiseAutomationIds()));
```

**预期**：`energy=true`、`energy_offset=false`（偏差轨独立 id、不配对）；`piecewiseIds` 里含 `"energy"`。

### 用例 6 —— 固定合成参数

```js
const p = tl.currentPart();
print(p.lockAutomation("energy"));
```

**预期**：`true`。切到底部 tabbar 的 **Energy (Actual)**：它现在是一条**与粉色合成参数曲线重合**的曲线
（原本 NaN 的段落被填成了模型输出）。一次 Ctrl+Z 撤回。

### 用例 7 —— 分段轨的空隙语义

合成参数曲线本身在段与段之间是断开的（引擎只在已合成块产曲线）。固定后：

**预期**：那些**合成参数本来就没有值**的位置，Energy (Actual) 仍然留空（不会被补成 0 或直线连过去）。

### 用例 8 —— 局部固定 + 已画值

先手画一段 Energy (Actual)（比如第 1 小节拉高到 80）→ 合成 → 运行 `p.lockAutomation("energy", 0, 1920)`。

**预期**：`true`；该段变成模型输出（你手画的被覆盖——脚本的固定是**显式覆盖写**，与笔刷刷过去一样）。
一次 Ctrl+Z 应把手画的值原样退回来。

> 对照：颤音关联时的**空隙填充**才是"绝不覆盖已画值"，那条路是自动的、不在脚本面（见
> `READBACK-PAIRING-LOCK-TEST-CASES.md`）。

---

## 四、报错路径（用法错误 → 整段回退）

### 用例 9 —— 逐条验错误消息

分别运行，看 Script 面板的错误：

| 代码 | 预期错误关键字 |
|---|---|
| `tl.currentPart().lockAutomation("nope")` | `unknown automation` + 指向 `part.automationIds() / part.piecewiseAutomationIds()` |
| `tl.currentPart().lockAutomation("energy_offset")` | `no paired synthesized parameter` + 建议先 `hasSynthesizedParameter` |
| `tl.currentPart().lockPitch(0)` | `BOTH startTick and endTick` |
| `tl.currentPart().lockPitch(960, 960)` | `endTick must be greater` |

### 用例 10 —— 报错时前面的写也不落地

```js
const p = tl.currentPart();
p.notes()[0].pitch += 12;         // 先做一个看得见的改动
p.lockAutomation("energy_offset"); // 再踩报错
```

**预期**：面板报错；**音符没有升八度**（整段脚本原子回退），撤销栈里也没有新步骤。

---

## 五、effect 轨

> 本次给 `tests/plugins/V1.Effect` 的 Gain **补了一条实参轨** `loudness`（显示名 **Loudness (Actual)**，绿），
> 与它既有的 `loudness` 合成参数轨（蓝，只读）**同 id ⇒ 配对**。这条实参轨刻意做成**连续**轨（有基线、
> 处处有值）——voice 侧的 `energy` 配对是分段轨，两边合起来才把固定的两条分支都盖到。
> DSP **不消费**它（同 `formant`）：固定写进去的就是当次产物值，重合成后 loudness 由输出音频重新算出、
> 不受该轨影响，故固定后两条线**保持重合**，正是要看的结果。

### 用例 11 —— 配对判据（effect 作用域）

给 part 挂上 **v1-effect** 的 Gain → 运行：

```js
const e = tl.currentPart().effects()[0];
print("continuous=" + JSON.stringify(e.automationIds()));
print("piecewise=" + JSON.stringify(e.piecewiseAutomationIds()));
print("loudness=" + e.hasSynthesizedParameter("loudness"));
print("gain_env=" + e.hasSynthesizedParameter("gain_env"));
print("formant=" + e.hasSynthesizedParameter("formant"));
```

**预期**：`continuous` 含 `"gain_env"` 与 `"loudness"`、`piecewise` 含 `"formant"`；
`loudness=true`（配对），`gain_env=false`、`formant=false`（引擎没声明同 id 的合成参数）。

### 用例 12 —— 固定 effect 的合成参数（连续轨分支）

先播放让 effect 处理完（参数区标题栏能看到蓝色 **Loudness** chip 有曲线）→ 运行：

```js
const e = tl.currentPart().effects()[0];
print(e.lockAutomation("loudness"));
```

**预期**：`true`。底部 tabbar 切到绿色 **Loudness (Actual)**：它现在**与蓝色 Loudness 重合**。
一次 Ctrl+Z 撤回。

> 连续轨与分段轨的差别在这里看得见：固定区间**之外**，连续轨仍保留它的基线值（默认 1.0）而不是留空——
> 这正是"连续轨处处有值"的口径，与用例 7 的分段轨留空相对照。

### 用例 13 —— effect 局部固定

```js
const e = tl.currentPart().effects()[0];
print(e.lockAutomation("loudness", 1920, 3840));
```

**预期**：`true`；只有该段与蓝线重合，段外仍是基线 1.0。

### 用例 14 —— effect 侧的报错路径

| 代码 | 预期错误关键字 |
|---|---|
| `e.lockAutomation("gain_env")` | `no paired synthesized parameter` |
| `e.lockAutomation("nope")` | `unknown automation` + 指向 `effect.automationIds() / effect.piecewiseAutomationIds()` |

### 用例 15 —— 链中第二个 effect（按下标定址）

再加一个 Gain（链中两环）→ 对 `effects()[1]` 跑用例 12。

**预期**：同样 `true`，且固定的是**第二个** effect 自己的轨（第一个的 Loudness (Actual) 不受影响）。
这验的是 effect 合成参数按**链中下标**定址（`AutomationKey.Effect(index, id)`）没有串轨。

---

## 六、与 UI 两条路对照

### 用例 16 —— 脚本固定 vs 固定笔刷，结果应当一致

同一个 part、同一段区间：
1. 用固定笔刷（按 `4`）在音符区刷过 1920..3840，记住曲线形状 → Ctrl+Z 撤回；
2. 运行 `tl.currentPart().lockPitch(1920, 3840)`。

**预期**：两次得到的音高曲线**形状一致**（同一份实现、同样的简化容差与 vibrato 扣减）；差别只可能在两端
边界的精确 tick（笔刷按鼠标扫过的范围累积，脚本按你给的数）。参数区对 Energy (Actual) 做同样的对照。

---

## 七、agent 路径

### 用例 17 —— 让模型自己用

Agent 侧栏对模型说：**「把当前 part 的模型音高固定成可编辑曲线」**。

**预期**：模型经 `run_script` 调 `part.lockPitch()`（必要时先 `get_script_api` 查手册），
**并且看返回值**——若还没合成，它应当如实回报"没有可固定的产物、需要先合成"，而不是宣称已完成。

### 用例 18 —— 模型对配对的处理

对模型说：**「把 Energy 的模型输出固定下来」**。

**预期**：模型用 `hasSynthesizedParameter("energy")` 或直接 `lockAutomation("energy")`；若它错挑了 `energy_offset`，
报错消息应当足以让它自己纠正到正确的 id（这条验的是错误文案的可自愈性）。
