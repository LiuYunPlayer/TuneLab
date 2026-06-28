# 脚本工具化设计（Script Tools）

> 目标：让用户**新增一个脚本文件 = 新增一个自定义工具**，自动出现在菜单/右键菜单里。
> 对标 Synthesizer V 的脚本元数据模型，但收敛进 TuneLab 现有的脚本执行底座（`TuneLab.Scripting`）。

本文是 **Phase 1** 的落地设计。键位（快捷键）系统作为后续独立 phase，仅在末尾列纲要、不展开。

---

## 0. 现状底座（已具备）

- 脚本是散装 `.js`，存于 `PathManager.ScriptsFolder`（`%APPDATA%/TuneLab/Scripts`），由 `ScriptLibrary` 维护；用户直接丢文件即可被发现。
- 运行经 `ScriptRunner.Run`：构造沙箱化 Jint 引擎，注入对象式动作面 `tl`，**整段脚本跑一次 = 一个可撤销 Commit**。
- 收口内核 `ScriptContext`：写操作立即改数据层但不 Commit，最外层 `Finish()` 统一收口。
- 入口有两个：Script 右侧栏（用户手动跑）、agent 的 `run_script`（模型跑）。共享同一份 `tl`。

Phase 1 不改动作面 `tl` 的语义，只在其上加「元数据声明 + 注册 + 运行收口加固」。

---

## 1. 元数据模型：执行式 `getScriptInfo`（对标 SV）

**采用执行式**（脚本里写 `getScriptInfo()` 函数返回元数据），不采用声明式注释头。

理由：
- 调用 `getScriptInfo` 本身是返回字面量，微秒级；枚举时只需 `eval 顶层 + 调一次 getScriptInfo`，顶层若遵守「只放函数定义、无副作用」的约定就只是登记函数，几毫秒，且全程在现有 timeout/语句数沙箱里（死循环也被钳住）。这些是**用户自己的、已授权按需运行的脚本**，安全/耗时都不是问题。可按文件 mtime 缓存避免重复 eval。
- 对标 SV，用户与 LLM 已熟悉这套形状，知识可迁移。
- 本地化等动态元数据天然支持（见 §4）。
- 「声明式清单、不执行代码就能列菜单」那条原则适用于**打包扩展**（ExtensionPackage，真正的信任/稳定边界），不适用于散装用户脚本。

### 约定

```js
// 顶层只放函数定义、无副作用（否则枚举元数据时会被执行）

function getScriptInfo() {
  return {
    name:     tl.language === 'zh-CN' ? '升八度' : 'Octave Up',  // 显示名（可本地化，见 §4）
    category: 'Pitch',        // 菜单分组（同 category 聚一组）
    author:   'someone',
    version:  '1.0.0',
    context:  'note',         // note | partContent | part | track | trackContent | global —— 决定挂哪个菜单（见 §3）
  };
}

function main() {
  // 真正的动作，用 tl 操作工程；整段包成一个 Commit
  for (const n of tl.currentPart().selectedNotes())
    n.pitch += 12;
}
```

字段对照 SV：`name`/`category`/`author`/`version` 沿用 SV 形状；`context` 是 TuneLab 扩展。

---

## 2. 双模式并存（向后兼容）

`ScriptRunner` 按脚本**是否定义 `getScriptInfo`** 分两种模式：

| | 定义了 `getScriptInfo` | 没定义 |
|---|---|---|
| 身份 | **可注册工具** | 散装脚本（现状） |
| 元数据枚举 | eval 顶层 → 调 `getScriptInfo` 收元数据 | 不参与注册 |
| 触发执行 | eval 顶层 → 调 `main()` | eval 整段（整段即动作） |
| 收口 | 一个 Commit（§5） | 一个 Commit（不变） |

- Script 侧栏里手写的临时脚本通常没有 `getScriptInfo` → 走老的「整段即动作」，无感知。
- 一旦脚本写了 `getScriptInfo`，它就**自动**进菜单，同时也能在侧栏选中后点 Run（此时调 `main()`）。

实现要点：`ScriptRunner` 增加「调命名函数」能力——eval 顶层（定义出函数），再 `engine.Invoke("main")`；现状的「eval 整段」是没有 `getScriptInfo` 时的退化分支。

---

## 3. 注册与上下文定位

### 枚举

启动时（及 Scripts 目录变化时）扫描 `*.js`，逐个：构造沙箱引擎 → eval 顶层 → 若定义了 `getScriptInfo` 则调用、收元数据。按文件 mtime 缓存。元数据枚举过程**防御性丢弃任何数据改动**（getScriptInfo 不应改工程；若误改则回退，复用 §5 的回退原语）。

### 挂载点（由 `context` 决定）

各菜单按"右键命中对象 vs 命中空白"分支，对应不同的目标心智，故 context 也分开。脚本经"该 context 天经地义的入口"取目标——命中 note/part/track 时该对象必被选中（右键即选中），故一律用选区：

| context | 挂载点 | 目标对象 | 脚本取目标 |
|---|---|---|---|
| `global` | 顶部 **Scripts** 菜单（按 `category` 分组） | 无具体目标 | currentPart / 全工程 |
| `note` | 钢琴卷帘**命中音符**右键 | 选中的音符 | `tl.currentPart().selectedNotes()` |
| `partContent` | 钢琴卷帘**空白**右键（part 的内容区） | 当前编辑的 part | `tl.currentPart()` |
| `part` | 编排区**命中 part**右键（part 整体对象） | 选中的 parts（可多选） | `tl.selectedParts()` |
| `track` | **轨道头**右键（整轨对象） | 选中的轨道（可多选） | `tl.selectedTracks()` |
| `trackContent` | 编排区**空白泳道**右键（轨道容器区） | 该泳道的轨道 | `tl.selectedTracks()` |

注入点：`PianoScrollViewOperation`（note 进命中音符分支、partContent 进空白分支）、`TrackScrollViewOperation`（part 进命中 part 分支、trackContent 进空白泳道分支）、`TrackHead`（track，菜单只建一次 → `menu.Opening` 时 `RefreshContextTools` 重建）。

右键即选中：轨道头右键选中本轨（`ITrack : ISelectable`，与 part/note 一致）；编排空白泳道右键选中该行轨道。故 track/trackContent 都用 `tl.selectedTracks()`。命名取 content/content 对称（`partContent`/`trackContent`）。

### 不做亮灭（enabled）

不引入 `enabled` 菜单灰显——它只是 UI 糖、不防逻辑错误，且右键命中对象时必有选中、灰显意义不大。前置条件的真正校验放在脚本 `main()` 里（无目标就 print 后返回）。需要时以后再以对象专属谓词（如 `hasSelectedNotes`）加回。

---

## 4. 本地化：只读 `tl.language`

新增一个**只读** `tl.language`，返回当前文化码（宿主侧已有 `TranslationManager.CurrentLanguage`，透出即可）。

- 不照搬 SV 的 `getTranslations(langCode)` 独立协议——我们枚举时本就会 eval 顶层、`tl` 始终注入，所以 `getScriptInfo` 里直接读 `tl.language` 即可产出本地化显示名。
- 同一个 `tl.language` 也覆盖动作体内的本地化需求（如脚本弹输入框的提示文案）。
- 与既有 i18n 路线（插件侧自译、返回成品本地化串）一致。

注：`tl.language` 在「无工程打开」时也要可用（启动枚举时可能尚无工程）。

---

## 5. 运行收口加固（对所有脚本生效，非仅工具）

以下两条是对 `ScriptRunner`/`ScriptContext` 的地基加固，**用户脚本、agent 脚本、注册工具一视同仁**，建议先落地（可视作 Phase 0）。

### 5.1 入口 `Pushable()` 守卫

运行前检查 `project.Pushable()`（`mUncommitedCommands` 为空才为真）：

```
if (!project.Pushable())
    return error: "another operation is in progress; finish or cancel it first.";
```

原因：`DataDocument.Commit()` 会把当时栈里的**全部未提交命令**打成一包。若脚本在别处 UI 操作中途（如正在拖音符，期间栈里堆着未提交命令）运行，会 ①与该操作的在途数据交错、打乱其逻辑；②把它的未提交改动一起吞进脚本的撤销单位。当前 Scripting 路径**没有任何地方查 `Pushable()`，是真漏**。

因为脚本是 UI 线程同步跑，入口检查即全程安全（运行期间无其他 UI 操作能插入 push）。

### 5.2 出错原子回退（替代现状的部分提交）

现状：出错时 `Finish()` 把出错前的部分改动照样 Commit。**改为原子回退**：

```
var startHead = project.Head;     // 运行前（已通过 Pushable 守卫 → 未提交栈空）
... 执行脚本 ...
成功： project.Commit();
出错/超时/取消： project.DiscardTo(startHead);   // 当无事发生
```

理由（用户路径 + agent 路径均采用原子回退）：
- **agent 脚本**是会乱来的那个。纠错的正确姿势是回头看自己写的脚本哪错了、重写，而不是去读当前工程的脏状态打补丁（那会在半成品上越叠越错）。宿主帮它干净回退到跑脚本前的状态，给模型「每次从同一干净起点重写」的稳定心智模型。
- **用户脚本**经市场检验、极少出错；为一致性同样原子回退（工具语义：要么生效、要么没发生）。
- 因 §5.1 保证入口未提交栈为空，`startHead` 的未提交数=0，`DiscardTo(startHead)` 回退掉的恰好是脚本自己那批命令，不多不少。

### 5.3 限制按触发源分流

现状 `ScriptRunner` 硬性 5s + 500 万语句上限，是为「agent 写的代码当失控保险丝」设计的，不适合用户的大批量工具。

- **用户显式运行（侧栏/工具）**：放宽或取消时间上限，换成**可响应的 Cancel 按钮**（`CancellationToken` 已一路铺好，侧栏现传 `None`，接上即可）；语句上限可保留但调大，仅防真死循环。
- **agent 跑的脚本**：维持现紧上限当失控保险丝。

> 真正支持「长任务不冻 UI」需把运行挪后台 + 进度/取消，但数据层改动必须 marshal 回 UI 线程（撤销/通知/merge 非线程安全），属后续工程；届时 §5.1 的入口守卫不够（后台运行期间用户可发起 UI 操作 push），需升级为运行期排他锁（见 §7）。

---

## 6. 元数据 vs 设置覆盖表：分层

- **元数据声明默认**（自动注册、零配置）——覆盖绝大多数情况，作者最清楚脚本该挂哪、要不要选区。
- **设置里的覆盖表**（从已发现脚本生成，非手填）——让用户改位置 / 改键 / 开关。

Phase 1 **只做元数据自动注册（纯菜单）**；覆盖表等到做快捷键时（冲突消解成刚需）再上，那时一并提供。

---

## 7. 后续 phase 纲要（不在 Phase 1 范围，仅记纲）

### 7.1 统一键位（快捷键）系统

- **不能有两套键位系统**：脚本快捷键要能声明/绑定并解决冲突，系统必须知道每个键被谁占用；而内置键位现在是硬编码且散落（部分在 `Editor.OnKeyDown` 手动 `e.Match`、部分在菜单 `SetShortcut`/`SetInputGesture`，Undo 等甚至两处重复、可能漂移），系统看不见 → 冲突无法检测。
- **结论**：建**单一键位/命令注册表**。内置命令注册默认绑定、脚本注册建议绑定、用户覆盖存一处、冲突消解横跨两者。菜单那批 `SetShortcut` 项本就半声明式，是最易迁的第一批。
- **区分**：命令式动作（New/Open/Save/Undo/Redo/播放切换/移调/删除…）进注册表、可重映射；模态/交互绑定（鼠标拖拽 operation、数字键 1–6 切工具、空格播放…）是交互语法，**不进**通用命令模型，原样留着。
- UI 暴露面可渐进（先露脚本绑定 + 内置冲突提示，内置重映射 UI 后补），但内置命令架构上必须驻留注册表，否则冲突检测失效。
- 工作量明显大于 Phase 1，是独立 phase。

### 7.2 后台执行大任务 + 子树 token 锁

- 长任务挪后台后，§5.1 的「入口 `Pushable()`」不足——运行期间用户可发起 UI 操作 push。需要**运行期排他**：组件向 document 申请提交控制权 token（仅未提交栈空时发真 token），push/commit 带 token 校验来源，commit 后失效。
- 更精巧的一般化：`DataDocument` 是一棵树，可在**某节点**申请 token，则其所有父/子不可再申请（类似 merge 时 changeflag 沿树传播），但**兄弟子树可独立申请** → 并列的两个 part 能被两处同时改，适配多人协作 / 「后台改 part A + 手动编 part B」。
- 结构性代价：今天 `DataDocument` 是全局单一 `mUncommitedCommands` + 单一 `Head`，整树共用一个提交栈；子树独立加锁/提交要把「未提交命令暂存」和 `Head` 下沉到按子树，撤销根模型从「文档级」改「子树级」，牵动 Commit/Head/Undo 整条线。是大工程，多人协作时再启。

---

## 8. Phase 1 落地清单

1. `tl.language` 只读属性（无工程时亦可用）。
2. `ScriptRunner` 双模式：识别 `getScriptInfo`；有则 eval 顶层后调 `main()`，无则现状 eval 整段。
3. 元数据枚举器：扫 `ScriptLibrary` → 调 `getScriptInfo` 收元数据（mtime 缓存，改动防御性回退）。
4. 注册：顶部 Scripts 菜单（按 category 分组）+ note/part/partContent 进各自菜单分支（无 enabled 灰显）。加 `tl.selectedParts()`。
5. 收口加固（§5，先行）：入口 `Pushable()` 守卫 + 出错 `DiscardTo(startHead)` 原子回退 + 限制按触发源分流（用户工具放宽 + Cancel、agent 维持紧上限）。
6. 文档：用户向「如何写一个脚本工具」说明（含 `getScriptInfo`/`main` 约定、字段表、`tl.language` 本地化示例）；喂 LLM 的 `ScriptApiReference` 同步补 `tl.language`。
7. 独立测试文档：覆盖双模式、note/part/partContent 注册分支、出错原子回退、增删脚本即时反映、限制分流。

**不在 Phase 1**：快捷键（§7.1）、设置覆盖表（§6）、后台执行 + 子树 token（§7.2）。
