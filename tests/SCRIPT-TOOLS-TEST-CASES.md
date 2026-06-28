# 脚本工具化（Script Tools）Phase 1 测试用例

对应设计：`docs/script-tools-design.md`。本批只测「脚本工具化 + 运行收口加固」新增范围，不覆盖已通过的脚本基线（侧栏运行、对象式 API 等）。

> 本批 context 为 `note` / `part` / `partContent` / `global`。`track` / `trackContent`（轨道头、编排空白）**待轨道头交互升级（加选中模型 + 拖拽调位）后再做**，不在本批。`enabled`（菜单灰显）**已取消**——它只是 UI 糖、不防逻辑错误，校验在脚本 main() 里。

## 前置

- 样例脚本已就位于 `%APPDATA%\TuneLab\Scripts\`（也在仓库 `tests/scripts/` 备份）。直接启动调试版应用即可。
- 各菜单/右键里显示的是脚本 `getScriptInfo().name`（**显示名**，非文件名）。下表「显示名」即你应看到的文字（中文界面）。

| 文件 | context | 显示名（zh-CN） | 出现位置 | 作用 |
|---|---|---|---|---|
| octave-up.js | global | 全部升八度（分组「音高」） | 顶部 脚本 菜单 | 当前 part 所有音符 +12 |
| add-track.js | global | 新增空轨道 | 顶部 脚本 菜单 | 加一条空轨道 |
| boom-rollback.js | global | Boom (rollback test)（分组「Test」） | 顶部 脚本 菜单 | 改一个音符后抛错（测回退） |
| harmony-third.js | note | 加三度和声 | 钢琴卷帘**命中音符**右键 | 选中音符上方三度加和声 |
| lyrics-lowercase.js | partContent | 歌词转小写 | 钢琴卷帘**空白**右键 | 当前 part 全部歌词转小写 |
| tag-part-name.js | part | 标记选中 Part | 编排区**命中 part**右键 | 选中的 part 名追加 " *" |
| plain-scratch.js | （无 getScriptInfo） | —— | 不出现 | 普通脚本 |

> 准备一个含至少一条 MIDI part、part 内有若干带歌词音符的工程；在钢琴窗打开该 part。

---

## 1. 顶部「脚本」菜单（global + 分组 + 本地化）

1. 顶部菜单栏应见 **脚本** 菜单（Transport 与 Extensions 之间）。
2. 展开应见：**音高 ▸ 全部升八度**、**Test ▸ Boom (rollback test)** 两个可展开分组子菜单，以及直接列出的 **新增空轨道**。
3. 点「全部升八度」→ 当前 part 全部音符升八度；Ctrl+Z 一次撤销。
4. **期望**：菜单项是显示名（非文件名）；note/part/partContent 工具**不**出现在此。

## 2. 音符右键（context=note，只在命中音符时）

1. **命中音符**右键 → 菜单底部分隔线下有「加三度和声」。点击 → 每个选中音符上方三度各加一个和声音符；Ctrl+Z 一次撤销。
2. **空白处**右键 → **不应**有「加三度和声」（应是 partContent 的「歌词转小写」，见下）。

## 3. 钢琴空白右键（context=partContent，只在空白处）

1. 钢琴卷帘**空白**右键 → 底部有「歌词转小写」。点击 → 当前 part 全部歌词转小写；Ctrl+Z 撤销。
2. **命中音符**右键 → **不应**有「歌词转小写」（那里是 note 工具）。

## 4. 编排区命中 part 右键（context=part，只在命中 part 时）

1. 编排区**命中某 part**右键（可先多选几个 part）→ 底部有「标记选中 Part」。点击 → 选中的 part 名各追加 " *"；Ctrl+Z 撤销。
2. 编排区**空白处**右键 → **不应**有「标记选中 Part」（trackContent 本批未做，空白处无脚本工具）。

## 5. 普通脚本不进菜单

- plain-scratch.js 无 getScriptInfo → 不出现在任何菜单。仅可在 Script 侧栏选中后手动 Run。

## 6. 出错原子回退（核心）

1. 记下当前 part 第一个音符的音高。
2. 脚本 → Test → 「Boom (rollback test)」。
3. **期望**：弹错误对话框（含 "boom — intentional error..." + "All changes were rolled back — the project is unchanged."）；关闭后第一个音符音高**未变**；撤销栈**无**新增项（Ctrl+Z 不会撤出半成品）。

## 7. 双模式在 Script 侧栏

1. Script 侧栏「打开」octave-up.js → Run → 调 main()，当前 part 升八度、提示 applied N edit(s)。
2. 「打开」plain-scratch.js → Run → 整段即动作（第一个音符歌词变 "hi"），无需 main()。

## 8. 本地化（tl.language）

1. 设置切 English，重启。
2. 「脚本」菜单名变 **Scripts**；各工具名变英文（Octave Up (All) / Add Third Harmony…），分组变 Pitch。

## 9. 增删脚本即时反映（FileSystemWatcher）

- 应用运行中往 `%APPDATA%\TuneLab\Scripts\` 丢一个新的带 getScriptInfo 的 .js（或删一个）→ 不重启，下次打开「脚本」菜单即见变化（顶部菜单靠目录监视器提前重建）。

---

## 回归检查（不应被破坏）

- agent 的 run_script 仍可用；出错返回「All changes were rolled back…」。
- Script 侧栏普通脚本运行、Doc/Code 切换、脚本库增删改查不受影响。
- 钢琴/编排原有右键菜单项（Copy/Cut/Octave/Set Voice/Delete 等）位置不变，脚本工具在其下方分隔线后。
