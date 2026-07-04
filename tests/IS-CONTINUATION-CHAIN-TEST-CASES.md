# IsContinuation 相接链判据测试用例

> **宿主判据已退役（本文判据描述仅存档历史语义）**：延音判定权已完整下放引擎
> （`IVoiceSynthesisSession.IsContinuation`，宿主照单消费、零判据），`IsContinuation` 回喂标志删除，
> 链语义不再是契约资产（各引擎判定语义自有，SDK 无共享实现）。**当前无自动化回归**——曾迁出的
> `ContinuationJudgmentTests` 随 SDK 共享实现的否决一并删除（测试对象不复存在）。
> 本文用例 1–3 的链语义仍与两个宿主自带实现一致（零引擎 `EmptyVoiceSynthesisEngine` 的编辑器 `"-"`
> 约定判定、样例插件 V1.Voice 的自有判定），可作为**手测用例**对无声源 part / 样例引擎复用（把
> "标志值"期望改读为"判定为延续 → 显示透明、前元音铺过"）；两者的自动化回归候选后置（零引擎为
> internal，需宿主程序集测试基建）。现行契约见 `docs/continuation-contract-draft.md`。

只覆盖本次受影响范围：`IsContinuation` 标志从「是不是延音符记号」收窄为「**生效延续**」——延音符 **且** 经不断裂的相接链回溯到发声 note。孤儿延音符（被空隙断链）应为 `false`。与既有音素显示基线（PHONEME-DISPLAY-TEST-CASES.md）独立。

## 背景

- 宿主显示侧（`ForwardFillEnd`）**一直**按相接门控铺设：孤儿延音符不被前元音铺过、显示空白。本次**不改显示行为**。
- 改的是**喂插件的标志值**：`IVoiceSynthesisNote.IsContinuation` / `VoiceSynthesisNoteSnapshot.IsContinuation` 现在 = 宿主 melisma 实际吞并集（含相接链），使插件**直接信标志即与宿主一致**，不会把孤儿误当真延音、把前元音铺进静音（消除一类不对等 footgun）。
- 判据实现：`INote.IsEffectiveContinuation()`（`TuneLab/Data/INote.cs`）——是 `-` 且向前回溯，遇空隙断链 / 链头无发声 note → `false`。

## 用例 1：相接链 → 生效延续（true，melisma）

**构造**（钢琴窗，秒）：
- note A：`la`，0.0–0.5（发声）
- note B：`-`，0.5–1.0（与 A 相接）
- note C：`-`，1.0–1.5（与 B 相接）

**期望**：
- B、C 的 `IsContinuation` = `true`（经 C→B→A 相接链回溯到发声 note A）。
- 显示：A 的元音铺过 B、C（melisma），音素带连续到 1.5。
- 插件音频：元音延续到 1.5，与显示一致。

## 用例 2：孤儿延音符 → 非生效延续（false，静音）

**构造**：
- note A：`la`，0.0–0.5（发声）
- note B：`-`，0.5–1.0（与 A 相接）
- **空隙** 1.0–1.5
- note C：`-`，1.5–2.0（**与 B 有空隙、不相接**）

**期望**：
- B 的 `IsContinuation` = `true`（相接 A）。
- C 的 `IsContinuation` = **`false`**（B 与 C 之间有空隙、链断裂 → 孤儿）。
- 显示：A 元音铺过 B 到 1.0、**不跨空隙铺到 C**；C 空白（不再透明地被铺，也无自身音素）。
- 插件音频：信标志 → **不把元音铺进 1.0–2.0 的静音**；C 段静音。**音频与显示一致、无分叉**。

## 用例 3：链中断后再起（混合）

**构造**：A `la`(0–0.5) · B `-`(0.5–1.0,相接) · C `ka`(1.0–1.5,相接发声) · D `-`(1.5–2.0,相接 C)

**期望**：B `IsContinuation=true`（链回 A）；C 是发声 note `false`；D `IsContinuation=true`（链回 C，**不是** A）。验证链头取**最近的发声 note**、不串到更早音节。

## 验证方式

显示侧（本次未改、作回归确认）：用例 1/2 的音素带铺设按上述「期望-显示」核对，与改动前逐像素一致。
标志侧（本次新增语义，交插件方验证音频）：以参照引擎合成，确认用例 2 的 C 段静音、用例 1 元音连续——即插件读 `IsContinuation` 得到的音频与宿主显示对齐。
