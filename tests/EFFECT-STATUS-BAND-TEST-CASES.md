# Effect 状态带（分层绘制 + 声称口 + 降级）测试用例

覆盖范围：状态带分层绘制模型（画家算法：声称完成垫底 → 音频事实 → 活动声称盖顶）、
effect 处理器声称口（`GetStatus`/`StatusChanged`，含纵向水位进度）、`Read` copy-out 输入、
降级琥珀事实层、1→1 产出契约。只测本次改动影响面；effect 链/bypass/重排/回显等既有行为见
EFFECT-TEST-CASES.md / EFFECT-READBACK-TEST-CASES.md（基线不动）。

## 视觉词汇速查

| 颜色 | 含义 | 来源 |
|---|---|---|
| 灰 | 待合成 | 声称（voice 自报） |
| 橙（可含纵向软绿水位） | 在跑；水位=该范围整体进度（不沿时间轴推进） | 声称（voice/effect 自报或宿主兜底） |
| 软绿（暗绿） | 有货但非最终：声称完成 / 已提交待下游处理 | 声称或事实 |
| 亮绿 | 链尾当前有效音频 = 听到的即最终 | **只能来自事实层** |
| 琥珀 | 降级定局：某级 effect 失败、播放 passthrough 未处理音频 | 事实层 |
| 红 | 合成失败（无声） | 声称（voice） |

## 前置

1. 构建并打包：`dotnet build tests/TestPlugins.slnx -c Debug` + `pwsh tests/pack-tlx.ps1`（已备好）。
2. 安装 `tests/tlx/v1-effect.tlx`。UI 显示名（非 Type id）：
   - **Gain**（同步瞬时、不自报状态→验证宿主兜底呈现）
   - **Reverse**（同步瞬时、倒放）
   - **Slow Gain**（慢速 ~3 秒、自报声称段+进度→验证纵向水位）
   - **Fail Demo**（恒抛异常→验证琥珀降级）
3. 需要一个能出声的 voice（如 `tests/tlx/v1-voice.tlx`）与含若干 note 的 MidiPart。

## A. 回归：无 effect 时的分层退化

- [ ] A1 不挂 effect：灰（待）→ 橙+voice 进度（合成中）→ **亮绿**（voice 段 Commit 即链尾事实）。与旧行为观感一致。
- [ ] A2 挂 effect 但全部 bypass：同 A1（effect 层无节点）。
- [ ] A3 voice 失败：红条 + pill 错误文案 + 右键复制，照旧。

## B. 分层语义：亮绿只能来自链尾事实

- [ ] B1 挂 **Slow Gain**，编辑 note 触发重合成：voice 合成中该段橙（voice 自报进度文案）；
      voice 完成后该段转**软绿**（音频已交付、待下游）并随即被 Slow Gain 的橙色声称覆盖；
      Slow Gain 完成后转**亮绿**。全程无莫名跳变——范围变化只发生在换色（阶段事实）时刻。
- [ ] B2 Slow Gain 处理期间 hover：pill 显示「43% · …」（进度来自其自报声称段）；
      橙段内出现**自底向上**的软绿水位（不是横向填充），随进度上涨。
- [ ] B3 挂 **Gain**（不自报状态）：处理瞬时完成难以观察，但无异常呈现——宿主兜底路径
      （调度事实→整段橙、无水位、pill 点名「正在处理效果器：Gain (1/2)」式文案）不报错。
- [ ] B4 重合成期间播放：听到的是旧链尾音频（陈旧但可播），带显示橙/软绿（非亮绿）——
      「亮绿=听到的即最终」不变量成立。

## C. 声称口（SlowGain 自报）

- [ ] C1 Slow Gain 处理中途再次编辑同一区域：处理取消、声称清空、重新调度后水位从 0 重涨；无报错、最终亮绿。
- [ ] C2 链 Gain + Slow Gain 两级：Gain 瞬间过、Slow Gain 期间该段橙+水位；完成后亮绿。
- [ ] C3 流光：Slow Gain 处理期间橙段有流光扫动；完成后停。

## D. 降级琥珀（事实层）

- [ ] D1 挂 **Fail Demo**：voice 完成后该段短暂软绿/橙，随即**琥珀**定局；播放**有声**（passthrough）。
- [ ] D2 hover 琥珀段：pill 显示「效果器 Fail Demo 处理失败，正在播放未处理音频」+ 换行异常消息；右键复制可用。
- [ ] D3 链 Fail Demo → Reverse：Reverse 拿 passthrough 输入正常工作（播放倒放的未处理音频），带仍琥珀。
- [ ] D4 删除/bypass Fail Demo：带回亮绿。
- [ ] D5 voice 失败 + 挂任意 effect：红（voice 声称盖顶，不被 effect 层洗白）。

## E. 编排区粗带

- [ ] E1 处理期间（voice 或 effect 在跑/待下游）：part 底缝灰条 + 在跑段流光。
- [ ] E2 Fail Demo 定局：**亮黄**条（区别于失败亮红、脏灰）。
- [ ] E3 全链完成：无条（绿=最终=干净无标记）。

## F. 契约与调度

- [ ] F1 无 `Read` 越界异常（Gain/Reverse 整段 Read；SlowGain 首跑整段、此后按账本窗口局部 Read）。
- [ ] F2 宿主标脏的区间相交过滤：把 Gain 的 gain_env 曲线改在**另一个段**的时间区间——本段不重跑
      （带不闪、回显不变）；改在本段区间内 → 正常重跑。
- [ ] F3 只调 Gain 滑条（参数 settled 变更）：所有段的 Gain 级重跑（保守调度），瞬时完成、无异常。

## G. 多语言抽查

- [ ] G1 English：pill "Processing effect: Slow Gain (1/1)" / "Effect Fail Demo failed, playing unprocessed audio"。
- [ ] G2 简体中文：对应中文文案。
