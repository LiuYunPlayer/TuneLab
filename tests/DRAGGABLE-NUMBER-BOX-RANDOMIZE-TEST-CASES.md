# 可拖拽数值框「随机」按钮验证（独立用例）

只验证本次新增面：`DraggableNumberBoxConfig.WithRandomizable()` + 宿主在数值框右侧给的骰子按钮。数值框其余行为（拖动 / 键入 / 撤销 / 多值态）见 `DRAGGABLE-NUMBER-BOX-TEST-CASES.md`，本文件不重复。

## 前置

- 已 build + pack + install `v1-voice`（包名 **V1 Test Voice**）。
- 测试夹具：`V1 Test Voice` 引擎在 **note 属性**上新增两条数值框——
  - **`Seed (Random)`**：整数框、量程 `[0, 9999]`、声明 `WithRandomizable()` ⇒ **应有**随机按钮。
  - **`Half Open (No Dice)`**：只设 `Min = 0`（上界无界）、同样声明 `WithRandomizable()` ⇒ **不应有**随机按钮（无界侧上没有均匀分布可取）。
- 对照物：同面板的 `tension` / `Steps (Int)` 滑条未声明可随机 ⇒ 无按钮；已有的 `Offset (ms)`（Phoneme 页签）亦无按钮。

## 进入被测面板

1. 打开 TuneLab，新建工程，给 part 选 **V1 Test Voice** 下任一声库（Alice / Bob / Carol）。
2. 画几个有歌词的音符，选中一个 → 右侧 note 属性栏 **Note** 页签，见 `Seed (Random)` 与 `Half Open (No Dice)` 两行。

## 用例

| # | 操作 | 期望 |
|---|---|---|
| 1 | 观察 `Seed (Random)` 行 | 数值框右侧有骰子按钮；悬停显示「随机化」提示（跟随界面语言）。 |
| 2 | 观察 `Half Open (No Dice)` 行 | **无**按钮；该行布局与改动前的数值框行一致（数值框右缘对齐其他行）。 |
| 3 | 点击 `Seed` 的骰子 | 值变为 `[0, 9999]` 内的整数；连点多次取值散布、不总落同一处。 |
| 4 | 点击骰子后 **Ctrl+Z** | 一步回退到点击前的值（一次点击 = 一个撤销步）。 |
| 5 | 点击骰子后再 **Ctrl+Y / 重做** | 回到随机得到的那个值（不重新抽）。 |
| 6 | 多选多个音符（值不一致时框显 `-`）后点骰子 | 抽出的同一个值扇出到所选各音符；一步可撤销。 |
| 7 | 悬停 / 按下骰子按钮 | 有悬停高亮与按下反馈（同滑条的随机按钮）。 |
| 8 | 反复缩放侧栏宽度 | 按钮始终贴右缘，数值框不被挤压/换行。 |
| 9 | 点骰子后存盘、重开工程 | 随机得到的值持久。 |
| 10 | 切换界面语言（设置窗）后重新悬停骰子 | 提示文案随语言切换（词条 `[DraggableNumberBox] Randomize`）。 |

## 已知非目标（本次不验证）

- 脚本层 `.withRandomizable()`（`ScriptDraggableNumberBoxConfig`）——同一条 config 通路，随脚本入参窗一并覆盖即可。
- 数组 / 列表控件里的数值框：`ArrayController` 目前不支持 `DraggableNumberBoxConfig`，无此入口。
