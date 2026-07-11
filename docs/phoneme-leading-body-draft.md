# 音素模型升级：Leading/Body 双列表 + BodyOffset（落地细案 · 草案）

> 状态：**设计草案，待复核后再动契约**。取代 per-note `Preutterance` 标量模型。
> 动机见 §0，冻结规格见 §11。命名已定：`LeadingPhonemes` / `BodyPhonemes` / `BodyOffset`。

## 0. 为什么要改（两个 Preutterance 表达不了的东西）

1. **分类必须抗帧抖动**：辅元音分界落在 note 头附近时，整帧量化让它 ±1 帧跳。现模型的「前置(lead)/主体」是从 `Preutterance` 几何**派生**（累积末 ≤ Preutterance → lead），分界一抖，某音素的归属就翻，连带**多选合并对齐 slot** 和**明暗**一起翻。
2. **几何在原理上分不了跨拍音素**：跨拍辅音应归 leading、跨拍元音应归 body，二者 `(start,end,head)` 完全相同、分类相反——任何几何阈值都区分不了，必须由引擎**显式声明**分类。
3. **精度**：`Preutterance = Σ(leading 时长) − BodyOffset` 若再喂旧 `NoteSplit`（firstStart 再正向累加），`Σleading` 往返两次浮点 → offset=0 也会 noteStart ± ε，正是被帧量化放大的亚帧误差（Sil 教训）。必须**原生按 offset 锚定**、单次求和。

结论：把**分类**做成结构化双列表（引擎声明、抗抖、非法态不可表示），把**几何**收进一个有符号 `BodyOffset`，两者正交。

## 1. 数据模型（契约）

每 note：
- `LeadingPhonemes`：引导音素列表（核前的前置辅音），时间序。
- `BodyPhonemes`：主体音素列表（核 + 尾辅音），时间序。
- `BodyOffset`（double，有符号）：主体起点（= 两列表结合线）相对 note 头的偏移。见 §2 符号。
- `Phonemes`：**只读全序列视图** = `LeadingPhonemes ++ BodyPhonemes`。供「要全部、按时间序」的消费者（布局/显示/序列化）用，语义 = 旧 `Phonemes`（不偷换）。**不可变**——写入方（Split/Delete/Lock 等）改为显式选 `LeadingPhonemes`/`BodyPhonemes`（编译期强制 revisit）。

删除：`Preutterance`（数据层 + SDK + 序列化）、`SynthesizedPreutterance`。分类不再有派生 `leadCount`/`IsLead`。

**不变式**：结构上 leading 恒在 body 之前，拼不出交替洞；空列表合法（纯 body 的 vowel-initial note / 纯 leading 的边角）。

## 2. 几何与摆放（offset-native，单次锚定）

**符号约定（已定：左负右正）**：
```
junction_abs = noteStart + BodyOffset          // 结合线绝对时刻 = 主体起点
```
即结合线在 noteStart **左（早）为负、右（晚）为正**。
- `BodyOffset = 0`：主体起点**精确** = noteStart（leading 全在头前收尾，body 从头起）。干净情形。
- `BodyOffset < 0`：主体起点在头**左/前** → body 首元素（元音）跨头（早于拍点起声）。
- `BodyOffset > 0`：主体起点在头**右/后** → leading 末元素（辅音）跨头、伸过拍点；若 `BodyOffset > Σleading` 则连 leading 首元素都在头之后 → note 拍点与首音素之间空一段静音（lead-in 空白，见边角，**决定：不钳**）。

**摆放（单次求和、以 junction 为唯一原点）**：
```
// body 从 junction 正向铺
BodyPhonemes[i].start = junction_abs + Σ_{k<i} BodyPhonemes[k].dur
// leading 从 junction 反向铺
LeadingPhonemes[j].end = junction_abs − Σ_{k>j} LeadingPhonemes[k].dur
```
`BodyOffset = 0` 时 `junction_abs = noteStart`（同一个数，无加减）→ leading 末 / body 首那条边界零误差落头上。**绝不**走 `Preutterance` 中转。

**头切分（供跨 note 压缩，几何、与列表分类无关）**：压缩域边界仍是 noteStart（= `FillStart`）。头落在哪个音素内 = 由上面 junction-anchored 位置**直接**与 noteStart 比较得出（`noteStart − junction_abs = −BodyOffset`，精确）。该音素被头一分为二：头前半进前 note 压缩域、后半进本域（现有跨拍逻辑不变，只是切点来源换成 offset）。**注意**：头切分的跨拍音素**未必**= 结合线那个音素（二者相距 `BodyOffset`）——这正是分类与几何解耦的体现。

**边角（lead-in 空白）**：`BodyOffset` 大到内容整体退到 noteStart 之后 → 拍点前空一段。**不防**（顶多一段静音起手；相接判据本就处理空隙 = 不借用）。软 clamp 留作可选，默认不加。

## 3. `PhonemeLayout` 改写

- `PhonemeLayoutNote` 载荷：`{ FillStart(=noteStart), FillEnd, LeadingPhonemes, BodyPhonemes, BodyOffset }` 替 `{ FillStart, FillEnd, Preutterance, Phonemes }`。
- `NoteSplit.Compute` 重写为 **junction-anchored**：由 `junction = FillStart + BodyOffset` 摆放全序列（§2 公式），按 `FillStart` 就地切拍前/跨拍/拍后。`Preutterance` 钳位 `[0,Σ]` 那套换成对 junction/offset 的等义处理。
- `Distribute`（等比/二级压缩）**完全不动**——它只吃「拍后料 + 借入前置料 + 可用空间」，与切点来源无关。
- Resolve 的返回形状不变（交错 `[Start,End]`）。

## 4. SDK 契约变更（engine 面）

- 输出：`SynthesizedSyllable { LeadingPhonemes, BodyPhonemes, BodyOffset }` 替 `{ Phonemes, Preutterance }`（map 值型，按归属 note 键）。
- `PhonemeInfo`（per-phoneme：Symbol/Duration/StretchWeight/Properties）**不变**。
- `IVoiceSynthesisNote` / `VoiceSynthesisNoteSnapshot` / `IVoiceSynthesisPartPropertyContext`：`Preutterance` 相关读口换 `BodyOffset` + 两列表视图。
- 文档 `voice-sdk-design.md` §6 同步（含 §2 摆放公式、§11 冻结项）。

## 5. 数据层

- `INote`：`IDataObjectList<IPhoneme> LeadingPhonemes`、`BodyPhonemes`；`IDataProperty<double> BodyOffset`（替 `Preutterance`）；`SynthesizedSyllable? SynthesizedSyllable`（替 `SynthesizedPhonemes[]` + `SynthesizedPreutterance`；壳内含两列表 + BodyOffset，Q3 定）。
- `Phonemes` ⇒ 只读拼接视图（计算属性 / 轻量包装）。
- `LockPhonemes`：把合成产物的两列表 + BodyOffset 固化为钉死数据（替「Phonemes + Preutterance」）。

## 6. 序列化

- `NoteInfo`：`LeadingPhonemes[]`、`BodyPhonemes[]`、`BodyOffset` 替 `Phonemes[] + Preutterance`。
- TLP/TLPX：当前 2.0.0-dev 的 preutterance 格式（version 2）**直接顶替**（2.0.0 未发布、不为 dev 格式留兼容，见 [[feedback_no_compat_in_2_0_0_dev]]）。是否再 bump version 见开放问题 Q4。
- `TuneLabProjectCbor` 同步。

## 7. Legacy compat

- 适配器 `OnPieceComplete`：从引擎回报的音素位置 + **老模型的 lead 归属**（legacy 本就有前置辅音概念）分类进 `LeadingPhonemes`/`BodyPhonemes`，`BodyOffset = body 首起点 − noteStart`（显示保真、逐字直显不吸头，同现状）。legacy 恒 `w=0`。
- `FormatConverter`：legacy 1.x（startTime/endTime）双向——读时按老 lead 标记 + 位置分两列表 + 算 BodyOffset；写时反算。**跳过 preutterance 中间态**（直达 lists）。

## 8. UI 两处（改读结构化分类，不再派生）

- **波形带明暗**：leading = 暗、body = 亮，直接按**列表成员**（`DisplayPhoneme` 带 `IsLeading` 由所属列表给，不再 `end≤head` 派生）。
- **侧栏合并对齐**（`NotePropertySideBarContentProvider`）：`leadCount = LeadingPhonemes.Count`（存的、稳定），对齐索引 `a = 全序列位置 − leadCount`（核 = body 首 = a0）。`PhonemeLeadCount` 那套几何派生删除。明暗同上。

## 9. 与上轮 roll / 侧栏接线（Preutterance → BodyOffset）

上轮（commit 0985a7b）的这些用点全部从 `Preutterance` 迁到 `BodyOffset`，且**分类显式后逻辑简化**：
- **侧栏改音素时长的生长方向**：不再靠 `f=拍前占比` 派生方向——**音素在哪个列表就定方向**：改 `LeadingPhonemes` 里的 → 向左长（调 BodyOffset 保 body 不动）；改 `BodyPhonemes` 里的 → 向右长（BodyOffset 不变）。跨拍那半由 BodyOffset 承担。
- **拖分界线 `DragPinnedBoundary`**：全刚性 roll / 有核 bisect 框架不变；「拍前/拍后」判据换成「属 Leading / 属 Body」；roll 的 Preutterance 耦合（上轮修的「只随被拖首音素向左长而 +d、前邻不动」）换算到 BodyOffset。
- `LockPhonemesForBoundaryDrag`、`BoundaryDomainAllRigid`、`BuildLayoutNote` 的 overridePreutter/第二时长覆盖 → 对应改到 BodyOffset / 两列表。

## 10. 测试

- `PhonemeLayoutTests`：现 10 例按新载荷改写（行为逐字复刻，尤其 `AllRigid_RollConservesTotal_EndpointsFixed`、`Sil_OffHeadBoundary_NotSnapped`）；**新增** `BodyOffsetZero_JunctionExactlyOnHead`（offset=0 → body 首 start 精确 == noteStart，零误差）+ `Classification_StableUnderOffsetJitter`（列表固定，BodyOffset ±ε 不改分类/对齐）。
- 手测：新建 `PHONEME-LEADING-BODY-TEST-CASES.md`（分类抗抖、跨拍归属、offset=0 精确、lead-in 空白、合并对齐不错位）。上轮的 `PHONEME-EDIT-DIRECTION-TEST-CASES.md` 相应更新措辞。

## 11. 冻结规格（钉死，供云端镜像）

- **BodyOffset 符号约定**（§2，待你定正负向）。
- **摆放公式**：单次、以 `junction = noteStart + BodyOffset` 为原点，body 正向 / leading 反向累加；`BodyOffset=0` 恒等精确 == noteStart。
- **头切分算术**：`noteStart − junction = −BodyOffset` 精确取用，严格比较无容差（同 `PhonemeLayout.Connected` 论证）。
- **跨语言 fp 一致**：摆放/切分只做加减、无 `Σ` 往返，规避亚帧漂移。

## 12. 决定（已拍板）

- **Q1 符号方向 = 左负右正**：`BodyOffset = junction − noteStart`，结合线在头左为负、右为正（§2 已按此定稿）。
- **Q2 lead-in 空白 = 不钳**：允许 `BodyOffset > Σleading` 造成拍点与首音素间的静音，不加 clamp（相接判据按空隙处理）。
- **Q3 合成产物 = `SynthesizedSyllable` 壳**：数据层也以 `SynthesizedSyllable`（内含 `LeadingPhonemes` + `BodyPhonemes` + `BodyOffset`）整体存，替 `SynthesizedPhonemes[]` + `SynthesizedPreutterance`。§5 按此。
- **Q4 序列化 = 直接顶替**：preutterance dev 格式无兼容，直接换 lists+offset（version 是否 bump 属实现细节，倾向不 bump，因 dev 格式非对外契约）。
- **Q5 `Phonemes` = 纯只读计算视图**：`LeadingPhonemes ++ BodyPhonemes` 每次拼接、不做 `IDataObjectList` 转发壳；现订阅 `Phonemes.Modified` 的消费者改订 `LeadingPhonemes` / `BodyPhonemes` 两个具体列表（都要 revisit，顺带迁）。
