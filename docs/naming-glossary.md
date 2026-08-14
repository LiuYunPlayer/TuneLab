# 术语表 —— 一个概念一个词

本仓的命名基准。**新增 API / 新写文档前先查这张表**：同一个概念在 SDK、宿主数据层、UI、agent 工具面、
脚本动作面、中英文档里必须用同一个词；发现同义词并存就是缺陷，按这里的裁决收敛。

## 为什么需要它

同义词的代价不是"读着别扭"，而是三条实打实的：

1. **模型会猜错**。脚本动作面与工具面是 LLM 的操作台：它在 `list_sound_sources` 里读到一个词、在脚本 API
   里遇到另一个词，就会猜出既不存在的第三个名字（`lockPhonemes` 与 `pinPitch` 就是这么来的）。
2. **搜不到 context**。改一处功能要 grep 两三个词才能捞全现场，漏掉的那个就是下次的 bug。
3. **用词跑偏会带着理解跑偏**。按用途起的名（"回显"）在用途扩展时会失准，而按事实起的名（"合成参数"）不会。

## 裁决规则

- **SDK 公共面是锚**。`TuneLab.SDK` / `TuneLab.Foundation` 的公共签名已冻结（见 `sdk-api-evolution.md`），
  改不动 ⇒ 其余各面一律向它收敛，反向不可能。
- **按事实命名，不按用途命名**。用途会增加（同一份产物既显示、又能被固定），事实不会变。
- **英文符号面严格唯一**；中文允许一个**上位词**存在，但不得用它指代某个具体类型（见下表"回显"一行）。
- 发现新的同义词对：先判哪个是事实/哪个是用途，再全仓收敛，然后**在这里加一行**。

## 术语

| 概念 | 唯一英文符号 | 中文 | 已废弃 / 不要再用 |
|---|---|---|---|
| 把只读的引擎产物固化成归用户的可编辑数据 | `Lock*`（`LockPhonemes` / `SynthesisLock` / `lockPitch` / `lockAutomation`） | 固定 | `pin`（脚本面 `pinPhonemes` 已改名；数据层残留 `HasPinnedPhonemes` 属历史，勿扩散） |
| 引擎在合成时发布的只读参数曲线（及其声明） | `SynthesizedParameter` / `GetSynthesizedParameterConfigs` / `hasSynthesizedParameter` | 合成参数（轨） | `readback`（符号面已清空） |
| 引擎发布的只读音高曲线 | `SynthesizedPitch` | 合成音高 | — |
| 引擎发布的只读音素 | `SynthesizedPhoneme` / `SynthesizedSyllable` | 合成音素 | — |
| **上位概念**：引擎产物被显示出来这件事（涵盖音素 / 音高 / 参数三种） | —（无符号，勿造） | 回显 | 不得用"回显"指代 `SynthesizedParameter` 这一具体族 |
| 合成机器类（会话 / 上下文 / 引擎 / 快照） | `VoiceSynthesis*` / `InstrumentSynthesis*` 中缀 | — | 见 `sdk-api-evolution.md` 的命名约定 |

### 两条边界的说明

**`lock` vs `pin`**：数据层的动作一直叫 `LockPhonemes`，UI 叫"固定笔刷"，只有状态属性 `HasPinnedPhonemes`
和早期脚本面用了 `pin`。脚本面是模型的操作台，同一范式在那里出现两个动词最伤，故统一到 `lock`
（`note.lockPhonemes` / `note.hasLockedPhonemes`）。数据层内部那个 `HasPinnedPhonemes` 未动——它不在动作面上，
改它要扫 UI 与 legacy compat 一大片；但**新代码不要再产出 pin**。

**`synthesized parameter` vs `readback`**：`readback`（回显）说的是"把它显示回来给用户看"——那是用途之一，
而"固定"功能一来就多了第二种用途；更糟的是它字面暗示"读回用户写进去的东西"，可这条曲线恰恰是引擎自己算的
（用户覆盖段只是其中一部分）。`SynthesizedParameter` 命名的是事实，且是 SDK 冻结名。故符号面、模型可见文案
（`list_sound_sources` / `list_effects` 的 `Read-only synthesized parameter tracks`）、脚本 API 全部用它。

中文保留"回显"仅因它是好用的**上位词**：音素留白→回显、音高回显、参数回显都说得通。真正指那族只读参数轨时
写"合成参数轨"。
