# 脚本入参 · 统一动作面 · 分级授权（设计）

> 目标：让脚本能在运行前向用户征集参数（响应式表单），把"临时执行 / 命名命令 / 快捷键 / agent 调用"
> 四个出口收敛到同一条执行管线，并给 agent 的写操作装一个全局分级授权闸门。
>
> 本文承接 [script-tools-design.md](script-tools-design.md)（脚本工具化 / `getScriptInfo` / `main` 双模式）
> 与 [keybinding-system.md](keybinding-system.md)（命令注册表 / 脚本命令）。三者共用 `TuneLab.Scripting` 底座。

---

## 0. 现状底座（已具备，本设计在其上加东西）

- **执行**：`ScriptRunner.Run(project, currentPart, quantization, language, selection, pianoSelection, limits, code, ct)`
  构造沙箱 Jint 引擎、注入动作面 `tl`（`ScriptApp`）、跑，**整段 = 一个可撤销单位**。
- **收口**：`ScriptContext.Finish(rollback)`——成功且有改动 → `Commit()` 成一个撤销单位；出错/取消/无改动 → 原子回退。
  当前 `ScriptRunner` 里写死 `Finish(rollback: error != null)`（成功即自动提交）。
- **写守卫**：**在首次写入时**（`ScriptContext.EnsureWritable`）检查，不在入口。只读脚本即便在用户操作中途也畅通；
  写被拦时抛 `ScriptBlockedException`，`ScriptRunResult.Blocked=true`，调用方可等 `Pushable` 恢复后整段重跑（`run_script` 已做 3s 轮询重试）。
- **双模式**：脚本定义了 `getScriptInfo` → eval 顶层后调 `main()`（工具）；否则整段脚本体即动作。
- **结果**：`ScriptRunResult { Ok, Error, Output, ResultText, Committed, Changes, Blocked }`。
- **三个运行入口各自直接拼 `ScriptRunner.Run`**：菜单/快捷键（`ScriptToolMenu.Run`，`ScriptLimits.Interactive`）、
  agent（`RunScriptTool`，`ScriptLimits.Agent`）、侧栏手动。
- **命令注册表**：`Keymap.Register/Rebind`，脚本命令 id=`script:<id或文件名>`，随脚本库同步（`ScriptToolMenu.SyncKeyCommands`）。

**范围声明**：本设计**全部是宿主内部改动**——`getInputConfig` 是脚本侧 JS 约定，描述对象 → `ObjectConfig` 的映射在宿主 TuneLab 层完成，
复用现成的 SDK `ControllerConfigs` 类型（`SliderConfig`/`ComboBoxConfig`/…，不新增 SDK 公共面）。**不触碰冻结的 SDK/Foundation ABI**。

---

## 1. 统一动作面骨架

现状是"三个入口各拼各的 `ScriptRunner.Run`"。骨架在其上收一个**单一调用抽象**，让四个出口都经它：

```
ScriptInvocation {
  Source  : Inline(code) | Library(scriptName)   // run_script 走前者；菜单/快捷键/侧栏走后者
  Inputs  : IReadOnlyDictionary<string, JsValue>? // null=尚未解析（按 §2.5 取）；agent 直接给已填值
  Trigger : UserManual | Keybinding | Agent | Sidebar
}
```

单一入口 `ScriptEngine.Invoke(invocation)` 统一四步，取代散落调用：

1. **取码**：`Library` → `ScriptLibrary.Read`；`Inline` → 直接用。
2. **解析入参**（§2）：脚本声明了 `getInputConfig` 且 `Inputs==null` 且 `Trigger` 允许交互 → 弹响应式入参窗；否则用已给/上次/默认值。
3. **执行**：`ScriptRunner` 跑，`main(inputs)` 带入参；`Trigger` 决定 `ScriptLimits`（`Agent` 紧保险丝 / 交互放宽）——现有"按触发源分流"归位到这里。
4. **收口**：按授权级别（§3）决定直接提交 / 预览待确认 / 只读回退。

**为什么值得做**：`Trigger`（谁在调）+ 授权级别成为每次调用的一等公民后，将来给 agent 开的**其它写通道**
（effect 链、设置、快捷键、切音源）能**自动继承**同一个入参机制与授权闸门，不必各造一套。这就是"骨架"的意义——后续能力挂上去零重复。

**同构闭环**：`run_script`（临时执行）与 `save_script`+快捷键（固化命名命令）只是 `ScriptInvocation` 的
不同 `Source`/`Trigger` 组合，共享同一条执行/入参/授权管线。故"帮用户写脚本+导入+绑快捷键"和"agent 顺手做一件事"在实现上是同一件事的两种落点。

---

## 2. 响应式入参 `getInputConfig`

### 2.1 声明约定（独立于 `getScriptInfo`）

脚本加一个**可选**约定函数 `getInputConfig(ctx)`，运行前（及每次改值）被调用，返回入参 schema 的**描述对象**：

`getInputConfig` 返回一个**键→config 的 map**（键=入参名，值=用 §2.2 的 config 构造 API 造出的 config）；宿主把它包成 `ObjectConfig` 渲染：

```js
function getInputConfig(ctx) {
  // ctx.values = 当前已填入参（首次开窗=上次值，无则各字段 default）
  // getInputConfig 里可读工程上下文作为动态依据：tl.currentPart().selectedNotes()、tl.voices() 等
  const count = tl.currentPart().selectedNotes().length;
  const s = {                                              // 键 = 入参名（= ObjectConfig 的 PropertyKey）
    mode: ComboBoxConfig.create(['transpose', 'setPitch']).withDefault('transpose'),
  };
  if (ctx.values.mode === 'setPitch')                      // 条件式：某项值决定别项是否出现
    s.targetPitch = SliderConfig.integer(60, 0, 127);
  else
    s.semitones   = SliderConfig.integer(12, -24, 24);
  return s;
}

function main(inputs) {                                    // 带参；无 getInputConfig 的旧脚本 main() 忽略之即向后兼容
  const notes = tl.currentPart().selectedNotes();
  if (inputs.mode === 'setPitch') for (const n of notes) n.pitch = inputs.targetPitch;
  else                            for (const n of notes) n.pitch += inputs.semitones;
}
```

注意脚本里写的 `SliderConfig.integer(...)` / `ComboBoxConfig.create([...]).withDefault(...)` 与 C# 的
`SliderConfig.Integer(...)` / `ComboBoxConfig.Create([...]).WithDefault(...)` **逐一对应**（camelCase↔PascalCase 由引擎既有的 `TypeResolver` 桥接）。

**为什么独立于 `getScriptInfo`**：`getScriptInfo` 被按 `(mtime, 语言)` 缓存、用于建菜单（静态、微秒级）；
而入参选项常要**运行时依赖工程上下文**（列当前音源/part/选中数），必须在"即将运行"时带 `tl` 现算，不能进缓存路径。分开各司其职。

**无副作用铁律**：`getInputConfig` 会被**反复调用**（开窗 + 每次改值）且带 `tl`，与 `getScriptInfo` 同样**必须无副作用**；
宿主每次重算后**防御性原子回退**任何误写（复用元数据枚举那条 `Finish(rollback: true)`；写守卫在首次写入处兜底，只读天然畅通）。

### 2.2 config 构造 API：与冻结的 SDK config 类同构

**不引入独立的"描述对象"词汇**。脚本用一套与 `TuneLab.SDK.ControllerConfigs` 那些冻结类**同构**的构造 API 直接造 config——
词汇（`Slider`/`ComboBox`/`CheckBox`/`TextBox` + `Linear`/`Integer`/`Create`/`WithDefault`/`WithFormat`…）就是这些类本身的、
从大众认知里挑定的词汇。全仓库因此只有**一套** config 词汇，SDK 类演进（新工厂/新 `With`）时脚本 API 自动跟随，不维护平行映射。

**机制决策：同构调用，而非自家声明式描述对象。** 两条理由：

- **scale/format 是可组合的行为接口**（`Rounded(Linear(…))`、`Custom(闭包)`）。同构调用天然表达组合与闭包（`NormalizedScale.rounded(NormalizedScale.linear(0,100))`、`NumberFormat.custom(v=>…, s=>…)`）；描述对象要么再造嵌套迷你语言、要么退化成"简单字段用数据 + scale/format 用回调"的混合式（两种心智塞进一个 API，最糟）。
- **描述对象的"可序列化"优势在此失效**：`getInputConfig` 是动态的（跑 JS、读工程上下文、响应式重算），schema 从不是静态 JSON；且 agent 读 schema 时宿主无论如何都要把最终 `ObjectConfig` 序列化成 agent 可读文本（与作者化机制无关）。
- 注：config"封构造函数、只留工厂+With"的**动机是 C# ABI 韧性**（已编译插件不能破），此动机**对脚本不成立**（脚本是文本、每次重解析）；故同构的理由是"单一连贯心智 + 组合性"，非 ABI。

**"同构"收窄为两条，避免踩坑：**

1. **只同构人体工学表面，藏掉管道类型**：绝不把 `PropertyValue`/`PropertyKey`/`IReadOnlyOrderedMap` 暴进 JS；裸值直接写（`['a','b']`、`12`）、键即对象键。你们 config 本就为此设计过（`ComboBoxItem` 的隐式转换、集合表达式友好），顺下来即可。
2. **只镜像入参相关子集**：只暴露能当输入控件的 value config（下表 5–6 个）；`AutomationConfig`/`ExtensibleObjectConfig`/`AddableKey` 等对入参窗无意义的**不给**。即"在输入相关子集上策展式同构"，非全家族搬运。

暴露为脚本全局的 config 工厂（名字=类名，方法=各类的静态工厂 + 流式 `With`/`Append`，camelCase↔PascalCase 由引擎 `TypeResolver` 桥接）：

| 脚本全局 | 同构于 C# 类 | 工厂 | 流式修饰 |
|---|---|---|---|
| `SliderConfig` | `SliderConfig` | `.linear(default,min,max)` / `.integer(default,min,max)` / `.create(default, scale)` | `.withFormat(...)` / `.withRandomizable()` / `.withMinLabel(s)` / `.withMaxLabel(s)` |
| `DraggableNumberBoxConfig` | 同名 | `.create(default)` / `.integer(default)` | `.withMin(x)` / `.withMax(x)` / `.withRange(a,b)` / `.withStep(s)` / `.withFormat(...)` / `.withSensitivity(k)` |
| `ComboBoxConfig` | 同名 | `.create()` / `.create(options)` | `.append(item)` / `.appendSeparator(label?)` / `.withDefault(value)` |
| `CheckBoxConfig` | 同名 | `.create(default=false)` | — |
| `TextBoxConfig` | 同名 | `.create(default="")` | `.withPassword()` |
| `ObjectConfig`（分组，可选/后续） | 同名 | `.create(properties)` | — |

要点与 C# 侧完全一致：

- **ComboBox 默认值是「值」非索引**（`ComboBoxConfig` 的 `DefaultOption` 语义）；选项是值/显示分离的 `ComboBoxItem`，JS 侧裸值（`['a','b']` / `[1,2,3]`）经隐式转换成项，与 C# 的集合表达式同款。
- **量程随标度走**：`SliderConfig` 无独立 Min/Max 字段，量程由 `Scale` 定义，`.linear/.integer` 工厂即给了标度。
- `label`/两端描述文本可含 `tl.language` 分支产出本地化串（与 `getScriptInfo` 一致）。
- **沙箱**：不直接把 CLR 类型丢进引擎（`ScriptRunner` 沙箱明令不暴露 CLR）。这些全局是**薄 JS 门面**——工厂返回包装句柄、`.withX()` 返回新句柄，内部持有真实 config；`getInputConfig` 返回的 map 值即这些句柄，宿主取出内部 config 包成 `ObjectConfig`。门面随 SDK config 类一一对应，是唯一需要随 config 类增删同步的接线点。
- **字段级标签**：`ObjectConfig` 里标签随 `PropertyKey` 走（key=入参名即标签，可经门面附带本地化 `PropertyKey`），而非逐控件单独声明。
- **文件选择器**现有 config 家族无对应控件，列为后续（可先 `TextBoxConfig`+浏览按钮，或补一个 host 控件）。

#### 标度与数字格式（`scale` / `format`）——行为接口的三层递进

`INormalizedScale`（`ToValue`/`ToNormalized` 一对函数）与 `INumberFormat`（`Format`/`Parse` 一对函数）是**行为接口**、非数据。
各自有便利工厂 + `.Custom(两个 Func)` 逃生口。JS 是函数语言，故三层递进：

1. **不碰**（绝大多数脚本）：`SliderConfig.linear/.integer` 已把标度烤进工厂；format 有默认（2 位小数、`.integer` 工厂 0 位）。
2. **内置变体**：把 `NormalizedScale` / `NumberFormat` 静态工厂也做成脚本全局（返回不透明句柄），塞进 `.create(default, scale)` / `.withFormat(fmt)`：
   `NormalizedScale.linear(min,max)` / `.integer(min,max)` / `.rounded(scale)` / `.floor` / `.ceil`；`NumberFormat.decimals(n)`——逐一同构 C# 静态工厂。
3. **自定义**：`.custom(...)` 逃生口直接收 JS 函数（C# 要 `Func<double,double>`，JS 就是箭头函数）：
   ```js
   NormalizedScale.custom(p => min*Math.pow(max/min, p), v => Math.log(v/min)/Math.log(max/min))   // 对数轴
   NumberFormat.custom(v => v.toFixed(1)+' dB', s => { const n = parseFloat(s); return isNaN(n) ? null : n; })
   ```
   宿主把两个 JS 函数包成 `INormalizedScale`/`INumberFormat` 适配器回调进引擎（接口恰是 `(double→double)×2` 与 `(double→string, string→double?)`，适配器几行）。

**自定义回调的唯一接线要点**：这些回调**不在 `main()` 里触发**，而在**入参窗存续期间、UI 线程、每次拖滑块/重绘/编辑文本时**被调。
故持有 JS 闭包的引擎须**活到关窗**（不能 `getInputConfig` 返回即 dispose），入参窗会话 retain 之；作用域天然有界（对话框级、跑完即弃，
逐拖拽调解释器可接受——不同于常驻属性面板的 config 会有持续回调性能顾虑）；`Custom` format 的 `Parse` 返回 `null`=解析失败须如实拒绝，与 C# 一致。
落地顺序：第 1/2 层随本切片；第 3 层（自定义回调 + 上述引擎存续接线）作紧随其后的小增量。

### 2.3 运行时序（入参窗一次会话）——复用属性面板的重算-diff 通路

你要的"改一项就重算 schema"正是宿主属性面板已在做的事：**每次值 commit 就重算 `ObjectConfig` 并 keyed-diff 到控件树**
（字段显隐 / 换控件 / 刷新选项随值涌现）。入参窗**不另造**，把数据源从插件的 `GetPropertyConfig(context)` 换成脚本的 `getInputConfig(ctx)`：

1. **开窗**：`getInputConfig({ values: 上次值或默认 })` → 映射成 `ObjectConfig` → 用属性面板既有的响应式组件（`PropertiesPanel` 一类）渲染。
2. **改任一项** → 以当前值重算 `getInputConfig({ values })` → keyed-diff 打到控件树。
3. **「重置到默认」按钮**：以空 `values` 调一次 `getInputConfig` 取默认布局、把各字段清回 `default`，重渲。
4. **确定** → 最终 `values` 作为 `Inputs` 喂 `main(inputs)`（§2.4）。

工程上下文（选区等）在模态窗存续期间**冻结**（用户无法同时编工程），故驱动重算的只有 `ctx.values`，语义干净。

### 2.4 `main(inputs)` 签名 + 入参值契约（稀疏，脚本自补默认）

`ScriptRunner` 工具分支从 `engine.Invoke("main")` 改为 `engine.Invoke("main", inputsJsValue)`：

- 有 `getInputConfig` → `inputsJsValue` = 用户在入参窗填的值对象。
- 无 `getInputConfig`（或声明为空）→ 传空对象；旧脚本 `main()` 不接参数即忽略 → **向后兼容**。
- 普通脚本（无 `getScriptInfo`，整段即动作）路径不变。

**入参值是【稀疏】的，且 `getInputConfig` 的 `ctx.values` 与 `main` 的 `inputs` 同形**——两者都**只含用户改过的键**（未改的键读到 `undefined`）。这刻意与 voice/instrument 插件读 `PropertyObject` 的契约**同构**：

- **存储即用户意图**：只存改过的键（稀疏）；未改键不写入、不 materialize。持久化（§2.6）同样只存稀疏值，故未改字段永远跟随 config 当前默认、不被冻结。
- **默认值的两个角色分离**：config 的 `DefaultValue` 只管**显示（控件未设时显示它）与「重置到默认」**；**消费时**（`main` 用值 / `getInputConfig` 里按已填值分支）由**脚本自己 `?? 默认`**，正如插件 `props.GetValue(key, 默认)` 自带默认。宿主**不**替脚本 materialize 默认进 `inputs`——那会造成与插件的不对称，并冻结默认。
- **推荐写法**：默认写成共享常量，同时喂给 config（显示）与读值处（消费），二者不漂移：
  ```js
  const DEF = { semitones: 12 };
  function getInputConfig(ctx) { return { semitones: SliderConfig.integer(DEF.semitones, -24, 24) }; }
  function main(inputs) { const s = inputs.semitones ?? DEF.semitones; /* … */ }
  ```
- **为何脚本能这样、而插件不能被宿主喂默认**：插件的 config context 是**多选编辑态**、合成却**与选中无关**，逐 note 喂默认得逐 note 重算 config，错误；脚本是**一次性运行、context 当刻固定**，本可全量，但为与插件同构、且避免 `getInputConfig↔main` 形不一致（全量需拿上一版 schema 默认回填 `ctx.values`，引入定点迭代），统一采**都稀疏 + 脚本自补**。

### 2.5 三入口取值策略

| 入口 | 有 `getInputConfig` | 无 `getInputConfig` |
|---|---|---|
| 菜单 / 快捷键 / 侧栏 | **总是弹响应式入参窗**，初值=该脚本上次值（无则默认），带「重置到默认」按钮 | 直接跑 |
| agent | agent 读 schema **编程填值**（缺项反问用户），作为 `Inputs` 传入，**不弹窗**；受授权闸门（§3）管 | 直接跑（受授权闸门管） |

快捷键与菜单一视同仁地弹窗（用户已选定"总是弹窗"）——只有"无入参脚本"才免窗直达。触发源仅影响初值来源与 agent 的免窗编程填值。

### 2.6 入参持久化

按脚本记住上次输入，存独立 JSON（照 `RecentSoundSourceManager` 范式，**不进** `Settings`/`EditorState`——它俩分别只承"可调项"与"窗口布局"）。
键 = 脚本稳定 id（`getScriptInfo.id`，无则文件名，与快捷键锚点同一套 id）。schema 变动导致的陈旧键容错（缺字段回默认、多余字段忽略）。

### 2.7 与业界脚本入参惯例的取舍

业界常见的脚本入参是**命令式静态对话框**：脚本在 `main()` 里主动弹一个固定的控件表单、阻塞等用户填完、取回按控件名索引的答案。
本设计三处刻意不同，均由你的诉求驱动：

| 维度 | 命令式静态对话框（业界惯例） | 本设计 | 为什么 |
|---|---|---|---|
| 声明方式 | 命令式：`main()` 里主动弹窗 | 声明式：宿主运行前调 `getInputConfig()` 拿 config map | **agent 可读 schema**——不跑 `main`/无副作用就知道要哪些参数，才能自己填/反问；命令式下只有跑到弹窗那句才知道 |
| 表单生命周期 | 脚本自持 | 宿主自持 | **预填上次值 + 「重置到默认」+ 统一"总是弹窗"**都需宿主拥有表单；命令式里默认值写死脚本、宿主无从预填 |
| 条件字段 | 静态（分支要连弹多个窗） | 响应式：`getInputConfig(ctx)` 随 `ctx.values` 重算（§2.3） | 你要"改一项重算 schema"；正好复用属性面板 `GetPropertyConfig(context)` 的重算-diff 通路 |
| 集合形状 | 数组，每项自带名字 | 以**参数名为键**的 config map | 键控贴合 `values` 字典与 keyed-diff，且直接就是 `ObjectConfig` 的 `PropertyKey→config` 形状 |

**命令式逃生口（可选、后续、仅用户运行路径）**：若个别脚本确需运行中途多轮追问，可后补一个命令式
`tl.showDialog(config)`（其入参仍用同一套 config 构造 API）。但它**破坏 agent 可读性与"预览重跑"授权模型**（预览重跑会二次弹窗），
故绝不进入**授权 agent 写路径**，只在用户手动运行时可用。默认不做，有真实需求再议。

---

## 3. 分级授权护栏（全局一个级别）

授权级别是一个 `Settings` 项 `AgentAuthorizationLevel ∈ { ReadOnlyAdvice, Confirm, Auto }`，**只作用于 agent 发起的写操作**；
用户手动运行（菜单/快捷键/侧栏）是用户自己的动作，**不受此闸门约束**。实现直接骑在现成的原子收口上：

| 级别 | agent 写操作（`run_script` / agent 触发的命名脚本）行为 |
|---|---|
| **只读建议** `ReadOnlyAdvice` | 脚本照跑，收口一律 `Finish(rollback: true)` 回退，**从不落地**；把"我会这样改 + 脚本源码 + 改动摘要"呈现给用户，退化为"帮你写好、你自己按运行"。 |
| **需确认** `Confirm` | **预览-回退 → 确认 → 重跑落地**：先跑一遍拿 `ScriptRunResult.Changes` 生成改动摘要、`Finish(rollback: true)` 回退，把摘要呈现给用户；用户确认后**以同一脚本重跑并 `Commit`**，取消则什么都不做。 |
| **全自动** `Auto` | 直接 `Commit`（现状行为）。破坏性操作可选仍强制一次确认（§3.2）。 |

### 3.1 为什么"需确认"用"预览-回退 + 重跑"，而非"挂起未提交栈等确认"

挂起未提交改动、跨异步确认再提交，在 agent 场景**不安全**：确认在聊天里异步进行，其间用户可编辑工程，与挂起的未提交栈交错，破坏原子模型。
而"预览-回退 + 重跑"完全贴合底座既定哲学——脚本"从干净起点重跑"本就是设计心智（见 script-tools-design.md §5.2：出错也是回退后重写，不在脏状态打补丁）。
代价是脚本跑两遍：对确定性脚本无副作用；重跑在**确认时**读当时工程状态，若与预览时已分叉，则以重跑结果为准（预览为参考，权威动作是确认后的那次）。

实现上给 `ScriptRunner.Run` 增一个提交策略参数（`Auto` / `PreviewThenDiscard` / `Commit`），或让 `ScriptEngine.Invoke` 按授权级别选择收口方式——避免在 `ScriptRunner` 内写死 `Finish(rollback: error != null)`。

### 3.2 破坏性识别

从 `ScriptContext` 记录的改动类型识别删除类（removeNote/removePart/removeTrack…）：
- 在"需确认"下用于**加重确认措辞**；
- 在"全自动"下可作为**唯一仍强制确认**的例外（可选子策略，默认开）。

### 3.3 未来写通道继承同一闸门

effect 链读写、设置读写、快捷键绑定、切音源等后续能力，其"写"一律经 `ScriptInvocation` 管线，**自动落到同一授权级别**下。
新增的只读环境查询（列插件/音源/设置清单）不算写，不受闸门约束。

---

## 4. agent 侧接线

- **读 schema**：扩展 `get_script_api`（或新增只读工具）让 agent 能取某命名脚本的 `getInputConfig` schema，从而自己填值或反问缺项。
- **run_script**：仍是逃生口（内联代码）。授权级别改变其收口：`ReadOnlyAdvice` 恒回退+呈现；`Confirm` 预览+重跑；`Auto` 直提交。
- **调命名脚本**：agent 以 `Source=Library + Inputs=已填值 + Trigger=Agent` 走同一 `ScriptEngine.Invoke`；等价于"替用户按下那个工具"，同样过授权闸门。
- `RunScriptTool` 现有的 3s 写守卫轮询重试保留（正交于授权：授权决定要不要提交，写守卫决定此刻能不能写）。

---

## 5. 落地清单

1. **`ScriptInvocation` + `ScriptEngine.Invoke`**：统一四步管线；三入口（`ScriptToolMenu.Run`、`RunScriptTool`、侧栏）改为构造 invocation 后经它。
2. **`getInputConfig` 支持**：`ScriptRunner`/`ScriptTools` 增"取 `getInputConfig` schema（带 `ctx.values`，无副作用回退）"能力；`main` 改带参调用。
3. **config 构造 API 门面**（宿主侧，§2.2）：为各冻结 config 类建薄 JS 门面（同名全局 + 工厂 + 流式 `With`/`Append`），`getInputConfig` 返回的 config map → `ObjectConfig`。含 `NormalizedScale`/`NumberFormat` 内置工厂门面（第 1/2 层）。
   - 3b. **自定义 `scale`/`format` 回调**（第 3 层，紧随其后）：`.custom(jsFunc, jsFunc)` → 接口适配器；入参窗会话**retain 引擎至关窗**、回调走 UI 线程（§2.2 标度与格式小节）。
4. **响应式入参窗**：复用属性面板的重算-diff 组件，接 `getInputConfig` 数据源 + 「重置到默认」按钮。
5. **入参持久化**：独立 JSON（§2.6），键=脚本稳定 id。
6. **分级授权**：`Settings.AgentAuthorizationLevel` + 设置窗 UI；`ScriptRunner.Run` 增提交策略；`Confirm` 的预览+重跑；破坏性识别。
7. **agent 读 schema**：`get_script_api` 扩展或新只读工具。
8. **文档**：用户向"给脚本加入参"说明（`getInputConfig` 约定、config 构造 API 表、条件式示例、`ctx.values`/`tl` 上下文）；`ScriptApiReference` 同步补 `getInputConfig`/`main(inputs)`；`script-tools-design.md` 挂指针。
9. **独立测试文档**（§7）。

---

## 6. 不在本设计范围（记纲，后续继承本骨架）

- effect 链 / 设置 / 快捷键 / 切音源等**新写通道**与**新只读环境查询**（音源目录、插件 readme、设置清单）——它们挂到已建好的骨架上，是后续切片。
- 设置元数据注册表（"告诉用户在哪调/自动调设置"的地基）——独立切片。
- MCP server 作为第四类消费者复用同一动作面。
- 后台执行大任务 + 子树 token 锁（见 script-tools-design.md §7.2）。

---

## 7. 独立测试文档纲要（不污染既有基线）

新建独立测试文档，只覆盖本设计受影响范围：

- `getInputConfig` 双态：有/无 `getInputConfig` 的运行分支；`main(inputs)` 带参与旧脚本 `main()` 无参兼容。
- 响应式重算：改值触发 `getInputConfig` 重算 → 字段显隐/选项刷新；`ctx.values` 传入正确；`getInputConfig` 读 `selectedNotes` 等上下文。
- 无副作用：`getInputConfig` 误写工程被原子回退，不落撤销栈。
- 「重置到默认」：清回各字段 `default`。
- 入参持久化：上次值回填；schema 变动的陈旧键容错。
- 分级授权三态：`ReadOnlyAdvice` 恒回退+不落地；`Confirm` 预览摘要正确 + 确认后落地 / 取消不动；`Auto` 直提交；破坏性强制确认（若启）。
- 授权只管 agent：用户手动运行不受级别影响。
- 快捷键"总是弹窗"：有入参脚本经快捷键触发也弹窗；无入参脚本直达。
