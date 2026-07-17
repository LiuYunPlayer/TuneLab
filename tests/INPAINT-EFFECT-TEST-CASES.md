# Inpainting 形态 voice × effect 链 手测用例

> 范围：V1 Test Inpaint 插件引入的「贯穿段 + 就地覆写 + 局部重合成」形态，及其与 effect 链的交互
>（段身份存活 / processor 不重建 / inpainting 粒度状态声称）。
> 基线状态带用例见 EFFECT-STATUS-BAND-TEST-CASES.md，此处不重复。
>
> 前置：已安装 **V1 Test Inpaint**（声源显示名 `Inpaint (V1 Test)`）与 **V1 Test Effect**
>（Gain / Slow Gain / Fail Demo）。日志文件在 `%AppData%\TuneLab\Logs\` 最新一份（P 组用）。
>
> 引擎行为参数：段几何按 **5 秒网格 + 1 秒余量** 取整——网格内编辑就地覆写；越网格（前/后向皆可）
> ⇒ 段 **Resize**（身份不变、交集内容保留、只补渲受影响 note 区间）。模拟推理耗时 0.8s/窗口。

## I 组：inpaint 本体

| # | 操作 | 预期 |
|---|---|---|
| I1 | 新 midi part 选 `Inpaint (V1 Test)`，写 3–4 个 note | 内容区灰(Pending)→整区橙 `inpainting` 带进度→亮绿；播放为正弦音 |
| I2 | **改中间一个 note 的音高** | **只有该 note（及与之相接的 note 链）区间**灰→橙→绿，其余区域保持亮绿不动——与 `Alice (V1 Test)`（块式引擎，整块变橙）肉眼对照 |
| I3 | 拖动一个 note 到别处 | 旧位置与新位置**都**进入重绘（旧位置音频清为静音，无残留声） |
| I4 | 删除一个 note | 其旧位置重绘为静音 |
| I5 | 某 note 歌词改 `fail` | 该次 inpaint 窗口红条 + hover 报错（`Inpaint failed: …`）可右键复制；改回歌词自动恢复 |
| I6 | 把尾部 note 拖长越过 5s 网格边界（**后向扩展**） | 段 Resize：只有该 note 区间重绘（局部橙），其余内容原样保留亮绿——增长**不再**全重绘 |
| I7 | 在首个 note 之前较远处（越网格）写一个新 note（**前向扩展**） | 段 Resize 向前生长：只有新 note 区间重绘，后方全部内容保持亮绿不动 |

## P 组：段身份 / processor 存活（挂 effect + 看日志）

| # | 操作 | 预期 |
|---|---|---|
| P1 | inpaint part 挂 **Gain**，打开日志文件；在**网格内**反复编辑 note | 每次编辑**不出现**新的 `Effect node created/disposed` 行（段身份稳定、processor 存活，仅重处理）；对照：同样操作在 `Alice (V1 Test)` 的 part 上，每次重合成都伴随 `disposed`+`created` 对（块引擎丢旧建新段） |
| P2 | inpaint part 挂 **Slow Gain**（局部重合成参考实现）：先等首次整段处理完（~3s），再改**一个** note | voice 局部 inpaint 后，Slow Gain 只重算变更窗——**处理时长明显短于首次**（∝ 脏区占比，如 20s part 改 1s note ≈ 0.3s）、状态带只有窗口区橙+水位；账本（`Input.RangeModified` 绝对轴）端到端收窄重算量的可感证据 |
| P3 | P1 场景触发 I6/I7（越网格 Resize） | 日志**仍然安静**（无 `disposed`/`created`）——段身份跨几何变更存活，Slow Gain 输出同样走 Resize，全链零重建 |

## B 组：状态带交互回归（inpaint 特有形态）

| # | 操作 | 预期 |
|---|---|---|
| B1 | Slow Gain 处理中（橙+水位）时再改别处 note | voice 的灰 Pending 只出现在 effect 橙框**之外**（z 序：排队声明垫在活动声称下） |
| B2 | 观察 I2 过程中的带 | **同一个音频段**上「部分区域橙 + 其余亮绿」共存——块式引擎无法产生此形态，这正是状态声称与段解耦的收益 |

## 已知边界（非缺陷）

- **Gain / Reverse 仍整段重算**：Gain 有意保留「布尔消费账本」的兜底示范（无局部能力引擎的合法姿势），
  Reverse 零订阅最简示范；局部重合成范式由 Slow Gain 独家承载。挂 Gain 时 P2 的时长差异不可见属预期。
- 本引擎无音素/回显/自动化声明（有意从简，聚焦段身份与局部重合成语义）。
