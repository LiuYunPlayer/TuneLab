# 插件多语言（i18n）· 测试用例

> 独立需求独立文档。本文只覆盖「插件文案按宿主语言本地化」这一改动影响的范围：
> 引擎级串（属性标题、ComboBox 选项、自动化轨名、声库名/简介）由插件按当前语言**自译**返回，
> 包元数据（name/description）由宿主按 description.json 的 `localizations` 挑选。
> **不涉及** 属性面板三态、effect 合成链、format/voice 合成正确性等既有基线，无需重跑。
>
> 设计见 [docs/plugin-i18n-design.md](../docs/plugin-i18n-design.md)。

## 夹具：V1 i18n Demo（`tests/plugins/V1.I18N`）

独立演示夹具，不动基线夹具。一个 voice 引擎 `TLI18NVoice`，2 个声库 + 一组本地化属性。各串中/英对照（**列的是 UI 显示名，不是 type id**）：

| 文案面 | en-US（基础） | zh-CN |
|---|---|---|
| 包名（侧栏卡片） | `V1 i18n Demo` | `V1 多语言演示` |
| 包简介（卡片 tooltip） | `i18n demo: …` | `多语言演示：…` |
| 声库①名 | `Demo voice` | `演示声库` |
| 声库②名（动态，名内嵌语言码） | `Cloud voice [en-US]` | `云端声库 [zh-CN]` |
| Note 属性 `depth` 标题 | `Depth` | `深度` |
| Note 属性 `quality` 标题 | `Quality` | `音质` |
| `quality` 选项 | `Low` / `High` | `低` / `高` |
| Note 属性 `raw` 标题（**故意未译**） | `Uncolored` | `Uncolored`（保持英文） |
| 自动化轨 `breath` 名 | `Breath` | `气声` |

> 前置已备好：已 `dotnet build tests/TestPlugins.slnx -c Debug` + `pack-tlx.ps1`，安装包在 **`tests/tlx/v1-i18n.tlx`**。

## 操作准备

1. 装 `tests/tlx/v1-i18n.tlx`（拖进窗口或侧边栏 → Install Extension）。
2. 新建工程 → 建一个 MidiPart，voice 选本夹具的声库（en 下名为 **Demo voice**，zh 下 **演示声库**）。
3. 画 ≥1 个音符，打开右侧 **Note Properties** 面板；底部参数栏/属性侧栏可见自动化。

---

## A · 语言 = 英文（基础值）

操作：设置语言 en-US，重启。

- [ ] 侧栏卡片包名 = `V1 i18n Demo`，简介为英文。
- [ ] 属性面板标题：`Depth`、`Quality`（选项 `Low`/`High`）、`Uncolored`。
- [ ] 自动化轨名 `Breath`。
- [ ] 声库名 `Demo voice` / `Cloud voice [en-US]`。
- [ ] 无报错、无控件空白/错位。

## B · 语言 = 中文（插件自译 + manifest 本地化）

操作：设置语言 zh-CN，重启。

- [ ] 侧栏卡片包名 = `V1 多语言演示`，简介为中文（取自 `localizations.zh-CN`）。
- [ ] 属性标题：`深度`、`音质`（选项 `低`/`高`）。
- [ ] 选中 `音质` 某项后落库的仍是原始值（0/1），非显示文本。
- [ ] 自动化轨名 `气声`（底部参数栏 + 属性侧栏自动化默认值行）。
- [ ] 声库名 `演示声库` / `云端声库 [zh-CN]`。

## C · 未译 → 原样显示（无宿主回退逻辑）

- [ ] zh-CN 下，`raw` 属性标题仍显英文 `Uncolored`（插件词典故意未收录），其相邻的 `深度`/`音质` 正常中文——同面板混排互不影响。
- [ ] 装一个**无 `localizations`** 的旧包（如 `v1-voice.tlx`）：其包名/简介在 zh-CN 下回退英文基础值，不空、不报错。

## D · 动态内容（插件按语言产出）

- [ ] 声库②名在 en-US 下含 `[en-US]`、zh-CN 下含 `[zh-CN]`——证明动态串确由插件按当前语言产出（与静态走同一条路径）。

## E · 切语言 = 重启生效

- [ ] 运行中切语言（不重启）：允许已渲染的插件文案暂不刷新（与宿主既有行为一致），不崩、不错乱。
- [ ] 重启后：插件文案（含 manifest）整体切到新语言。

## F · 回归（行为保持，重点）

> 本次改动设计为行为保持，重点确认没改坏既有功能。

- [ ] 其它已装插件（v1-effect / v1-suite 等）照常加载、属性面板/自动化照常显示（automation 名仍为 Gain Env / Power / Volume 等，同改动前）。
- [ ] 宿主自身菜单/对话框文案正常（未受影响）。
- [ ] 内置自动化 Volume / VibratoEnvelope 名称正常显示。
