# 能力位摘要测试用例

覆盖：`list_extensions` 返回前把缺的一句话摘要补齐——**短文档直接用作者原话（零调用）**，
长文档才各发一次旁路请求，按 introduction 的**内容哈希**缓存，一份文档一辈子只做一次。
对 agent 而言 summary 就是能力位自带的属性，没有对应的工具。

重点验：出处两分（原话 / 转述）、短文档不调模型、内容寻址失效、`SUMMARY:` 标记挡客套话、
补不完如实回报、不进 UI。introduction 的解析/渲染与扩展启停都已各有测试文档，不复测。

## 前置

**未改动任何 SDK 表面**，样例插件无需重建/重装。需要 agent 侧栏**已连模型**。

缓存文件：`%AppData%/TuneLab/Configs/ExtensionSummaries.json`——测前删掉，从冷启动看最清楚。
**运行中删也有效**：下一次 `list_extensions` 会发现文件没了、丢掉内存里的旧表当冷缓存重来
（缓存文件被删的意思就是"请重建"）。不必为此重启。

| 包（侧栏显示名） | 本文用它测什么 |
|---|---|
| **V1 Test Voice** | 基本回路；`localizations` 指定了 zh-CN 版 introduction → 测语言分开 |
| **V1 Test Multi-Suffix** | 一条目两后缀共用一份说明 → 共用一条摘要 |
| **V1 Test Format** | ~~**不写** introduction → 不生成、也不该报错~~ ——**已变**：它现在是拆写形态（format-import + format-export 两个条目），两个条目**各写了** introduction，故各生成一条摘要。要测「不写 introduction」这一支，改用 `V1 Missing-Assembly` 之类没写的夹具，或临时把某条目的 `introduction` 去掉 |
| **V1 Test Suite** | 一包两条目，各有各的 introduction → 两条独立摘要 |

**什么要重启、什么不用**——判据是"这东西在加载期定死，还是每次调用现算"：
| 改动 | 要重启吗 |
|---|---|
| 改 introduction **内容** | **不用**（内容哈希现算，备忘键含修改时间 → 自动失效重生成） |
| 删 / 改坏缓存文件 | **不用**（`SyncWithDisk` 每轮对表，文件没了就当冷缓存） |
| 切界面语言 | **要**（生效哪份 introduction 是加载期按当时语言解析定死的） |
| 改 manifest、装 / 卸插件、改宿主代码 | **要** |

**⚠️ 先确认改的是哪一份**：manifest 的 `localizations` 会让不同界面语言指向不同 introduction 文件。
中文环境下 `V1 Test Voice` 生效的是 `Introduction.zh-CN.md`，改 `Introduction.md` 会"什么都没发生"、
容易误判成 bug。安装目录按 manifest 的 `name` 建（`Extensions\V1 Test Voice\`），不是包 id。

两条路径这批夹具**天然都覆盖到**（预算 1000 字符）：`V1 Test Suite` 两份与 `V1 Test Voice` 的 zh-CN 版
在预算内走原话直采，`V1 Test Multi-Suffix`（1040）与英文界面下的 `V1 Test Voice`（1141）走模型。
要把某一份从直采推到模型，往它里面**灌到 1000 字符以上**即可。

## 1. 基本回路

1. 删掉缓存文件、重启，让 agent 调 `list_extensions`。
2. **期望**：返回里每个有文档的能力位下方带一行摘要，且**出处分两种**：
   - 短文档 → `(author's own words) …`
   - 长文档 → `(TuneLab's paraphrase of the author's introduction, not their wording) …`
3. 缓存文件出现，形如 `{"<32位十六进制>": {"Summary": "…", "Verbatim": true, "Label": "<包目录>/Introduction.md"}}`。
4. 再调一次 `list_extensions` → **期望明显更快**（全部命中缓存，零请求）。
5. 重启后再调 → 仍然快、内容一致（跨进程持久）。
6. 全程 agent **没有**调用 `get_extension_introduction`，也没有任何与摘要相关的工具——它感知不到生成过程。

## 1b. 短文档不该调模型

1. 挑一个 introduction 很短的夹具，删缓存后调 `list_extensions`。
2. **期望**：它那条标 `(author's own words)`，内容就是 introduction **原文一字未改**（markdown 标记、
   表格、代码块全在，按行缩进呈现），且**这一条没有产生任何模型请求**（看日志/网络）。
3. 把该文件灌长到 **1000 字符以上**、再调一次 → **期望**这次改走模型，标记变成 `(TuneLab's condensation …)`，
   且内容应保留承重事实（参数名与默认值、前置条件、限制），而不是一句含糊的概括。

## 2. 出处标注，别让模型当成作者原话

1. 让 agent 用中文介绍某个声库。
2. **期望**：对标了 `(TuneLab's paraphrase …)` 的那条，它不会说"作者称……"；对标了
   `(author's own words)` 的，转述成作者的说法是**允许的**——那本来就是原话。
   这条正是"出处两分"的价值：一律标成转述会让本可引用的原话也被打折。
3. 追问细节时**期望**它去调 `get_extension_introduction`（摘要只是索引，不是全文的替代品）。

## 3. 内容寻址：插件更新即自动失效

1. 手工编辑 `%APPDATA%\TuneLab\Extensions\<V1 Test Voice 目录>\Introduction.md`，改动任意文字。
2. **不必重启**（内容哈希每次现算、备忘键含修改时间），直接再调一次 `list_extensions`。
3. **期望**：**重新生成**，新摘要反映新文案；**绝不**沿用旧摘要。
4. 缓存文件里旧键已被清掉（写入时顺带清理"当前已装扩展里不存在的 introduction"）。

## 4. 语言变体各自一条

1. 界面语言切**英文**，删缓存后调一次 `list_extensions` → 得到英文 introduction 的摘要。
2. 切**简体中文**，**必须重启**（生效的是哪份 introduction 是在扩展加载期按当时语言解析定死的，
   切语言不会重新加载扩展），再调 `list_extensions`。
3. **期望**：宿主为 `Introduction.zh-CN.md` **另生成一条**（内容不同即不同键），且新摘要是中文的
   （prompt 要求"用文档的语言作答"）。
4. 缓存文件里两条并存，切语言各取各的。

## 5. 多后缀条目共用一条

1. 删缓存后调一次 `list_extensions`。
2. **期望**：**V1 Test Multi-Suffix** 那**一条** `provides format:mtest,mtst` 行带一句摘要——两个后缀
   共用同一份说明，本就该共用一条（内容寻址天然做到，不需要额外逻辑），且**只发了一次**请求。

## 6. 没有 introduction 的能力位：不生成、不报错

1. 在 `list_extensions` 的输出里找一个**没写 introduction** 的能力位（V1 Test Format 已改为拆写形态、两个条目都写了说明，不再适用；可用 V1 Missing-Assembly，或临时去掉某条目的 `introduction`）。
2. **期望**：那一行**没有摘要行、也没有 `[full text: …]` 提示**，更没有报错；它照常显示身份、显示名与状态。
   绝不能拿包级 description 冒充这个能力的摘要——那会造出一条无源的"事实"（包级自述可能涵盖包里别的能力）。

## 7. 请求失败 / 撞限流 / 超预算 —— 如实回报

1. 把 base url 改错（或临时断网），删缓存后调 `list_extensions`（需有长文档条目，否则全走原话直采）。
2. **期望**：**不报错不崩**，列表完整；对应条目写 `(not summarized yet — see the note at the end)`，
   末尾一句 `Note: N capability(ies) could not be summarized this time … tell the user they can ask again in a moment`。
3. **期望** agent 据此**转告用户可稍后重试**，而不是说"这些插件没什么可说的"。
4. 恢复后再调 → **期望**补上（失败不做负缓存，会重试），已做好的不重复花钱。
5. 长文档较多时观察请求：**期望串行**、不会一次打出一串——这正是为避免频率限制而定的姿态。

## 8. `SUMMARY:` 标记：客套话进不了缓存

1. （需要能观察/构造模型输出）让总结请求返回 `我来帮你总结：这个插件可以……`（**没有** `SUMMARY:` 标记）。
2. **期望**：**整条丢弃**、该条目写"尚未总结"，日志一条 info——不能把客套话当摘要写进缓存。
3. 让它返回 `好的，我来总结一下。
SUMMARY: 一个测试用正弦波声库` → **期望**只取标记之后那句，
   前面的客套话不进缓存。
4. 让它返回一整篇、或以 `{` 开头的 JSON → 同样丢弃。
5. **不该**出现被截断的半句话——那种半句 agent 之后每次读到都会困惑，而用户与开发者都不知情。
6. 三个数各司其职，别混：预算 `MaxSummaryChars`=1000（告诉模型的目标 + 作者原文装不装得下的判据）、
   兜底拒收 `RejectOverChars`=1500（只挡离谱输出；与预算取同一个数会让"写到 1020"的文档永远补不上）、
   喂入上限 `MaxIntroductionChars`=20000（与 `get_extension_introduction` 同口径）。

## 9. 不进 UI

1. 打开该扩展的详情窗。
2. **期望**：页面上**只有**作者写的 introduction 全文，没有那句自动摘要（宿主不替作者背书他没写过的话）。
3. 侧栏卡片、设置窗同样不出现。

## 10. 喂给模型的正文不该比 agent 看到的少

1. 找一份**很长**的 introduction（>2 万字符，可临时往夹具里灌），删缓存后调 `list_extensions`。
2. **期望**：喂给总结请求的正文与 `get_extension_introduction` 回给 agent 的**同一口径**——
   同样截到 `MaxIntroductionChars` 并带上 `… (introduction truncated; N more characters)` 标记，
   不是另设一个更小的限。（判据：两处若各设一个数，早晚漂移成"摘要是从半份文档提炼的"而没人察觉。）
3. 正常长度的文档**完整喂入**，不做任何额外截断。

## 11. 缓存文件坏了不该出事（**运行中也要认**）

1. TuneLab **保持运行**，先调一次 `list_extensions` 让摘要都生成好。
2. 手工把 `ExtensionSummaries.json` 改成非法 JSON（比如删掉最后一个 `}`），**不重启**，再调一次。
3. **期望**：日志一条 warning，列表照常返回，缓存被当作**冷的**重新生成，文件被**重写成合法内容**。
4. 反例（真实踩到过的回归）：内存里那份完好 → 认为一条都不缺 → 既不重新生成也不落盘 → 坏文件原地不动。
   根因是当年只判了 `File.Exists`，漏了"文件在、但内容是垃圾"这一支。
5. 改坏后**重启**再调，结果应当一致（加载期解析失败同样退化成冷缓存）。
6. 顺带验"外部改动以磁盘为准"的第三种情形：把文件手工改成**另一份合法内容**（如改掉某条 Summary 的
   文字），不重启再调 → **期望**采纳磁盘上的版本，而不是拿内存里的把它盖回去。

## 12. 运行中删缓存文件

1. TuneLab **保持运行**，先调一次 `list_extensions` 让摘要都生成好。
2. 手工删掉 `ExtensionSummaries.json`，**不重启**，再调一次 `list_extensions`。
3. **期望**：文件被**重新生成**、内容重新填回（短文档仍是原话直采、长文档重新调模型）。
4. 反例（本条要防的回归）：内存里已有旧表 → 认为一条都不缺 → 既不重新生成也不落盘，
   文件就此消失不见。这是自测时真实踩到的。
