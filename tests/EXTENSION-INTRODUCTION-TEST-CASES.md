# 条目级 introduction 测试用例

覆盖新增的**条目级** `introduction`（不回退包级 `description`，拿不到时 agent 侧做**标注式降级**）、
详情窗按条目分 tab、齿轮按能力位归属，以及 format 的 `suffixes` 多别名声明。
只测本切片，不复测扩展加载/routing/设置本身。

## 前置

按 `AGENTS.md` 三步部署样例插件（**先关掉 TuneLab**）：

1. `dotnet build tests/plugins/<名>/<名>.csproj -t:Rebuild`
   —— 涉及 `V1.Voice`、`V1.Suite.Common`、`V1.Suite.Voice`、`V1.Suite.Format`、`V1.Format`、`V1.MultiSuffix`
2. `pwsh tests/pack-tlx.ps1`
3. `pwsh tests/install-tlx.ps1 v1-voice v1-suite v1-format v1-multisuffix`

> ⚠️ **后续变更影响本文**：format 的方向改由后缀字段决定（两个类必须写成两个条目），且一个条目
> 只声明**一个**入口类（`class` 标量）。于是 V1 Test Format 与 Suite Format 这两个"一条目两个类"的夹具
> 各变成了**两个条目 = 两个 tab**，下表已按新形态更新；§3 与 §4 的 tab 数与标签名也随之变化。
> 新形态本身的验收见 [EXTENSION-FORMAT-SETTINGS-TEST-CASES.md](EXTENSION-FORMAT-SETTINGS-TEST-CASES.md)。

四个样例各覆盖一种形态：

| 包（侧栏显示名） | 形态 | 期望 |
|---|---|---|
| **V1 Test Voice** | 单插件简写，顶层 `introduction`，`localizations` 给 zh-CN 覆盖它 | 一个 tab |
| **V1 Test Suite** | 一包**三**条目（`tlsuite` 的导入条目 + 导出条目 + voice `TLSuiteVoice`），各自 introduction；**只有 voice 条目声明了扩展设置** | 三个 tab，齿轮只在 voice 页 |
| **V1 Test Format** | 两个类 → 两个条目（`.tltest` 的导入与导出），各写 introduction、各带扩展设置 | 两个 tab，两页都有齿轮 |
| **V1 Test Multi-Suffix** | **一个** format 条目 + `"suffixes": ["mtest","mtst"]`，共用一个类与一份 introduction | 一个 tab，页内列出两个后缀 |
| （voice/instrument/effect 条目） | 身份是 engine id | 页内**不列**身份——那是内部注册键，对使用无意义 |

## 1. tab 条恒显示（含单条目包）

1. 侧栏点 **V1 Test Voice** 开详情窗。
2. **期望**：正文顶部有一行 tab，**即使只有一个条目也显示**——那个标签是条目自己的显示名 + 类别徽标，与 header 的包名是两回事（包名与条目名可以不同），属有效信息。
3. header 只剩：图标 / 名 + 版本 / 作者 / **包级** description，右侧只有「Uninstall」。**没有**那排包级类型徽标了——它原本就是各条目 kind 的并集，而每个 tab 已带自己的徽标（侧栏卡片上的徽标仍保留，那里没有 tab）。
4. tab 内容区就是 `Introduction.md` 渲染结果（含图片、表格、任务列表、代码块、引用）；**没有**"一句话摘要"那一行——作者只写全文，AI 要的摘要由它自己提炼。
5. 「Open in External Editor」在 **tab 条右端**（不在 header），点开的是 `Introduction.md`。

## 2. 语言覆盖走 localizations，不是文件名后缀

1. 切界面语言到**简体中文**，重启后再开该详情窗。
2. **期望**：正文变成 zh-CN 版（`Introduction.zh-CN.md`）——由 manifest `localizations.zh-CN.introduction` 指定，**不是**靠 `<base>.<lang>.md` 猜的。
3. 手工验证「无隐式后缀约定」：删掉 manifest 里整个 `localizations` 段、重装，中文界面下**期望**回退到基础 `Introduction.md`（即便 `Introduction.zh-CN.md` 仍在包里也不会被自动选中）。

## 3. 多条目包：一条目一个 tab

1. 开 **V1 Test Suite** 详情窗。
2. **期望**：三个标签 `Suite Format (Import)`、`Suite Format (Export)`（徽标 Format）与 `Suite Voice`（徽标 Voice）——`.tlsuite` 的导入与导出由两个类实现，故是两个条目；选中项**只靠颜色 + 底部高亮条**区分，**不加粗**（加粗会改变文字宽度、切 tab 时整条会抖）。
3. 标签文字与类别徽标**竖直居中**对齐（不是徽标贴底）。
4. 切 tab → 正文换成对应条目的 introduction，滚动位置归零。
5. 切 tab 后 tab 条右端的「Open in External Editor」目标随之变（`Introduction.Format.md` / `Introduction.Voice.md`）。
6. header 不随 tab 变——它讲的是**包**（包级 description 仍是"一包多插件：format + voice 共享…"）。

## 4. 一个格式、多个后缀别名（suffixes）

用 **V1 Test Multi-Suffix**：manifest 是**一个** format 条目 + `"suffixes": ["mtest", "mtst"]`，
两个后缀共用一个实现类和一份 introduction（模拟 `.mid`/`.midi`）。

1. 开详情窗 → **期望只有一个 tab**（声明单位就是"格式"，压根不存在两个条目指同一份文档，也就没有"合并"这回事）。
2. **期望**该页顶部列出它认的后缀：`.mtest  .mtst`。
3. 打开设置窗「Extension Routing」→ **期望**两个后缀**各自**有 Import / Export 行、可分别选包——声明合在一起并不收窄路由粒度。
4. agent 调 `list_extensions` → **期望**该包**一条** `provides format:mtest,mtst "Multi Suffix Format"` 行（身份列全），附一次 introduction 提示。
5. `get_extension_introduction("mtest")` 与 `get_extension_introduction("mtst")` → **期望**返回同一份介绍（任一别名都能定位到该条目）。
6. 随便造个 `x.mtest` 和 `x.mtst` 空文件，分别导入 → **期望**都能打开、都得到那个两音符样例工程（同一个类服务两个后缀）。

## 5. 没写 introduction：占位 + 标注式降级

用 **V1 Test Format**（manifest 里没有 `introduction`）。

1. 开详情窗 → **期望**一个 tab，正文顶部列出它认的后缀 `.tltest`（format 条目**恒列**，一个也列——显示名看不出对应什么文件），其下是「This extension has no documentation.」占位。
2. **期望**正文里**没有**包级 description——那句在 header 里，不会被搬进条目页冒充能力说明。
3. `get_extension_introduction("tltest")` → **期望**回报明确分三层：① 该能力没有 introduction、作者没写；② 给出**包级** description 并标明"仅因该能力没有自己的介绍才给你作降级参考"；③ 提醒它讲的是整个包、可能涵盖包里别的能力，**不要**当成这个能力的描述转述给用户。
4. 找一个连包级 description 都没有的包调用 → **期望**明说两者都无、除名字外一无所知。

> 关键约束：宿主绝不静默拿包级 description 顶替能力说明。降级可以，但必须让模型知道自己拿到的是二手信息。

## 6. agent：引擎目录里的降级注记

1. 让 agent 调 `list_sound_sources`（不给 engine）。
2. 对**没有** introduction 的引擎 → **期望**其下缩进一行 `(no summary of its own; its package describes itself as "…" — package-level, may cover other capabilities too. For this engine specifically, call get_extension_introduction.)`。
3. `list_effects`（不给 engine）同格式；instrument 亦然（三处共用 `EngineCatalog.AppendEngineList`）。
4. 内建引擎（无 manifest、无包 description）**期望**无该行，不报错。
5. 同一 engine id 被两个包提供时（装 `V1.VoiceConflict.A` + `.B`）→ **期望**降级参考取**活实现那个包**的 description，而不是被顶替者的。

## 7. README 不再被当元数据

1. 手工在 **V1 Test Format** 的已安装目录里放一个 `README.md`（随便写点内容），**不改** manifest。
2. 重开详情窗 → **期望**仍是「无文档」占位，README 内容不出现。
3. `get_extension_introduction("tltest")` → **期望**仍走用例 5 的降级路径，**绝不**返回 README 内容。

## 8. agent：list_extensions 的两层粒度

1. 让 agent 调 `list_extensions`。
2. **期望**每个 V1 包一行包级信息（名/id/版本/Generation/status/kinds/作者）+ 下一行**包级 description** + 逐条目 `provides <kind>:<身份清单> "显示名"`，声明了 introduction 的附 `[introduction available — call get_extension_introduction("…")]`。
3. V1 Test Suite **期望**出现**两条** `provides` 行（`format:tlsuite` 与 `voice:TLSuiteVoice`）。
4. 装两个提供同一身份的包时，冲突注记（`ACTIVE` / `SHADOWED`）挂在对应那条 `provides` 行**之下**缩进显示；多后缀条目每个后缀各出一条注记。
5. legacy 包**期望**无 `provides` 行（无 manifest 条目），但其参与的冲突仍按包列出——**不因改版丢失排障信息**。

## 9. 设置齿轮按能力位归属（不再是包级入口）

用 **V1 Test Suite**（voice 条目实现了 `IExtensionSettings`，同包 format 条目没有）。

1. 开详情窗，切到 **Suite Voice** 页 → **期望** **tab 条右端**出现「Settings」齿轮（与「Open in External Editor」并排；它们都是当前条目的操作，故不在 header、也不独占正文一行）。
2. 切到 **Suite Format (Import)** 或 **(Export)** 页 → **期望**齿轮**消失**（这两个能力位没有设置），「Open in External Editor」仍在。
3. header 右侧**只剩**「Uninstall」——卸载是包级操作，留在 header；设置是能力位级的，跟着 tab 走。
4. 在 voice 页点齿轮 → **期望**设置窗打开、切到「扩展」页并滚动到 **Suite Voice** 那一段（不是同包别的条目、也不是列表顶部）。
5. 改一个值后关窗 → 日志应出现 `[V1.Suite.Voice] ApplySettings: ...`，确认值落到了**本条目**的桶。
6. **侧栏卡片上不再有齿轮**：它原本是包级的，包内多个条目都有设置时只能跳"首个"，等于拿包级控件冒充某个能力的入口。设置入口只有两处：详情窗对应 tab 的齿轮（准确），以及设置窗「扩展」页（逐条目平铺的总览）。
7. legacy 包详情窗的图标**不被压扁**：信息列只有"名 + 版本"一排时图标仍保持 56px 下限（原来会跟着塌到二十几像素并被裁边）。

> 一个只声明设置、没写 introduction 的条目**仍然会生成一页**（正文显占位 + 齿轮）——事实上现在是**逐条目恒生成一页**，tab 一栏如实回答"这个包提供哪些能力位"，没写文档的也照样在列。

## 10. legacy 包与未知类型：降级呈现

**legacy 包**（如 `legacy-voice`、`ChoristaUtau`）没有 manifest 条目，能力由兼容层盲扫发现：

1. 开其详情窗 → **期望不显 tab 条**（一个写着包名、点了没反应的 tab 是纯噪音）。
2. **期望类别徽标回到 header**（`Voice` 等）——没有 tab 承载它时，它落在 header；卡片上的徽标一直都在，两处不该只有一处有。
3. 正文**期望**是 legacy 专属说明（大意：这是旧版扩展，不提供 manifest 元数据、宿主靠扫描发现其能力，故没有作者文档），**不是**泛泛的"该扩展暂无文档"——后者会让人以为作者偷懒没写。
4. 图标**期望**保持 56px 下限、不被压扁（legacy 信息列常常只有"名 + 版本"一排）。

**未知类型的代码插件**（本宿主不支持的 `type`）——用一个**刻意不存在**的 kind 作夹具，别拿真实/在建的插件类型当例子，那样它一转正用例就失效了：

5. 把任一 V1 样例包已安装目录的 manifest `type` 改成 `"no-such-kind"`（保留其 `assembly`/`classes`）→ **期望**侧栏显示 `Skipped`，`note:` 写明 `unsupported extension type 'no-such-kind'` 并列出受支持类别与文档指引。
6. **不得**再被静默当资源包登记成"已加载"——那样代码一行没跑却显示成功，比报错更误导。
7. 真资源包（`"type": "voicebank"` 等，**不写** assembly/classes）→ **期望**照旧正常登记为已加载。

## 11. 详情窗关闭键

1. 悬浮标题栏右上的 × → **期望**底色变**红**、叉变白（系统窗口关闭键惯例）。
2. 在 × 上按下**不放**、拖到窗口别处再松开 → **期望窗口不关闭**（Clicked 只在抬起且指针仍在键内时触发）。
3. 在 × 上正常单击 → 关闭。
4. 拖标题栏空白处（含标题文字上）→ **期望**仍能移动窗口；在 × 上按下拖动 → **期望不会**变成拖窗口。

## 12. format 条目的导入/导出共享一份说明

1. 对 `tlsuite`（同时实现导入与导出）调 `get_extension_introduction("format:tlsuite")`。
2. ⚠️ **本节前提已变**：`tlsuite` 的导入与导出是**两个类**，故现在是**两个条目**、两份 introduction（虽然指向同一个文件）、详情窗两个 tab。要测"一个条目共享一份说明"改用 **V1 Test Multi-Suffix**（一个类、`suffixes` 两个别名，见 §4）。
3. 设置窗「Extension Routing」里该格式仍显示 Import / Export 两行（各自可选包）——路由粒度与条目粒度本就是两根轴。
