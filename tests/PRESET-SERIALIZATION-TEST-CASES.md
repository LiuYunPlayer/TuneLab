# Part preset 序列化共用件 + instrument 音源修复 — 测试用例

> 覆盖本次改动的受影响范围：① preset 与 TLP 共用叶子序列化（`DataInfoJsonUtils`），修复 preset
> 丢 `SoundSourceInfo.Kind` 导致 instrument part 的 preset 不可用；② 存储改**单文件制**（每 preset
> 一个 `.tlpartpreset` 文件、带版本号），preset 文件可转发共享。
> preset 面板的既有交互（Save/Save As/Rename/删除/None/多选扇出/运行期关联）不在此重复，
> 见既有基线；TLP 工程读写为纯等价重构，由单测 `DataInfoJsonUtilsTests` + 既有工程回环保障。
>
> 前置：安装任一 voice 测试声库（如 `V1 Test Voice`）与 instrument 测试插件（UI 显示名见
> 各自 manifest 的 DisplayName）。**存储已改单文件制**：每条 preset 一个
> `%AppData%\TuneLab\Configs\Presets\<名字>.tlpartpreset`，文件名即 preset 名。旧的整库文件
> `Configs\Presets.json`（dev 期，无兼容义务）不再被读取，可手动删除。

## A. instrument preset（本次修复的主路径）

| # | 操作 | 预期 |
|---|---|---|
| A1 | 建 MIDI part，音源选 instrument 引擎，改几个 part 属性 → Preset 面板 Save As 存为 `inst-1` | 保存成功；`inst-1.tlpartpreset` 中 `soundSource.kind` 为 `"instrument"` |
| A2 | 另建一个 voice 音源的 part → 应用 `inst-1` | part 音源变为该 instrument 引擎（钢琴窗重叠不变暗、显音名等 instrument 形态生效），属性/自动化默认值同步套入 |
| A3 | 重启应用 → 对任一 part 应用 `inst-1` | 与 A2 一致（落盘读回不丢 Kind） |
| A4 | 对 instrument part 应用一个 voice preset | 音源变回 voice 形态，无残留 instrument 行为 |

## B. voice preset 无回归

| # | 操作 | 预期 |
|---|---|---|
| B1 | voice part 存 preset → 改音源/属性/自动化默认值 → 应用该 preset | 音源、属性、自动化默认值全部还原；一次撤销整体回退 |
| B2 | 多选若干 part（含混源）应用 preset | 全部 part 统一为 preset 音源与参数，一个撤销步 |

## C. 单文件存储与文件格式

| # | 操作 | 预期 |
|---|---|---|
| C1 | 存 preset `Alpha` 后查看 `Configs\Presets\` | 出现 `Alpha.tlpartpreset`，内容 `{ "version": 1, "soundSource": …, "properties": …, "automations": … }`；automations 为 `{key:{default,values:[]}}` 形（values 恒空） |
| C2 | 不改任何 part 属性直接存 preset → 查看 `properties` | 引擎声明的属性字段**全部在场**、值 = 声明默认值（物化快照，非稀疏差量）；automations 同理全轨在场 |
| C3 | 把别处拷来的 `.tlpartpreset` 文件放进 `Presets\` → 重新点开 preset 下拉 | **免重启**出现在列表中（文件名即 preset 名），可直接应用 |
| C4 | 资源管理器里把 `Alpha.tlpartpreset` 改名 `Beta.tlpartpreset` → 重开下拉 | 列表显示 `Beta`（名字随文件名走） |
| C5 | 手工把某文件 `version` 改成 `99` → 重开下拉 → 用同名 Save As 覆盖 | 该条不出现在列表（拒读高版本，日志留 error，其余 preset 不受影响）；同名保存报错弹窗而**不**清写该文件 |
| C6 | 手工把某文件内容改成非法 JSON → 重开下拉 | 仅该条消失（日志留 error），其余 preset 正常加载 |

## D. 文件名校验（新建 / 重命名）

| # | 操作 | 预期 |
|---|---|---|
| D1 | Save As 输入 `a/b`、`a:b`、`a*b`、尾部带 `.` 或空格的名字 | 错误弹窗拦下（提示非法字符/首尾格式），不产生文件 |
| D2 | Save As 输入 `CON`、`com3` | 错误弹窗（保留系统名） |
| D3 | Rename 到非法名字 | 同 D1/D2 拦下，原文件保持不动 |
| D4 | Save As 输入中文/空格/点号混合的正常名字（如 `默认音色 v2.1`） | 正常保存，文件名与输入一致 |
