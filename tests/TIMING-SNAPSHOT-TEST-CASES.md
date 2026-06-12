# 合成快照基础件（Timing 换算 + 自动化无变形开窗）· 测试用例

> 独立需求独立文档。本文只覆盖三项改动的影响范围：
> ① SDK.Base 文件分域整理（config 家族迁入 `TuneLab.SDK.Base.ControllerConfigs`、ILog 迁入 `…Environment`，纯 namespace 移动）；
> ② tick↔秒换算唯一份纯函数收敛为 `TempoConvert`，`TempoManager` 改调、新增 `ITiming`/`TempoSnapshot`（后续冻结面收口：实现家族 `TempoConvert`/`TempoSnapshot`/`TempoMark` 现居宿主 `TuneLab.Data.Timing`，SDK 契约面只留 `ITiming` 接口）；
> ③ 宿主侧自动化不可变窗口快照（`AutomationSnapshot`/`PiecewiseAutomationSnapshot` + `AnchorWindow` 开窗）。
> **不涉及** 任何既有 UI/合成行为变化（①② 是等价重构，③ 是尚无调用方的新增件）——既有功能基线无需重跑。

核心正确性主张（单测钉死的两条防漂移锚）：

- **换算零漂移**：live `TempoManager` 与冻结 `TempoSnapshot` 共用 `TempoConvert` 唯一份实现，对同一 tempo 表逐点**全等**（不带容差）。
- **开窗零变形**：快照取"闭区间内锚点 + 每侧开区间之外至多两个锚点"（单调 Hermite 的斜率影响半径 = 2），窗口内取值与全曲线逐点**全等**；只外扩一个点则边缘段变形（有反向论证用例）。

## A · 单元测试（自动，必须全绿）

```powershell
dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj
```

- [ ] `TempoConvertTests`：单/多 tempo 已知值（120BPM 时 480 tick = 0.5s 等）、负 tick 线性外推、live vs 快照逐点全等、标量=批量、往返换算。
- [ ] `AutomationSnapshotTests`：开窗索引规则（边界在段内取 2 / 压锚点取 1+自身 / 越界取到头 / 空表）；五种窗型下快照 vs 全曲线逐点全等；DefaultValue 偏移；0/1/2 锚点退化路径；**只外扩一个点不充分**的反向论证。
- [ ] `PiecewiseAutomationSnapshotTests`：窗口切组 / 压组边缘锚点 / 纯空隙窗 / 全覆盖 / 越界五种窗型，快照 vs 活曲线逐点全等（NaN 位置同为 NaN）；空隙 NaN、组边锚点有值的语义抽查。

## B · 手动冒烟（namespace 迁移与 TempoManager 改调的回归面）

准备：重新构建并打包测试插件（namespace 变更后旧 tlx 失效）：

```powershell
dotnet build tests/TestPlugins.slnx -c Debug
powershell -File tests/pack-tlx.ps1
```

- [ ] 应用正常启动，打开既有工程无报错。
- [ ] 改一处 tempo（如 120→60），播放：音符发声时刻与时间轴显示一致（GetTime/GetTick 改调共享函数后的行为回归）。
- [ ] 钢琴窗画 automation 曲线、画 pitch，渲染与拖动正常（AnchorPoint/插值路径未动，应无感）。
- [ ] 装 `tests/tlx/v1-suite.tlx`，建 part 选 **Suite shared-infra voice**（`TLSuiteVoice`），属性面板各控件正常显示（ControllerConfigs namespace 迁移后插件与宿主侧 config 互通无碍）。
- [ ] 装 legacy 插件（任一旧版 voice），加载与属性面板正常（兼容层 config 转换路径已随 namespace 同步更新）。
