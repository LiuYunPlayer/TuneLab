# 脚本工具化（Script Tools）Phase 1 测试用例

对应设计：`docs/script-tools-design.md`。本批只测「脚本工具化 + 运行收口加固」新增范围，不覆盖已通过的脚本基线（侧栏运行、对象式 API 等）。

> context 全集：`global` / `note` / `partContent` / `part` / `track` / `trackContent`（track 两个在轨道头选中模型落地后已补齐）。`enabled`（菜单灰显）**已取消**——它只是 UI 糖、不防逻辑错误，校验在脚本 main() 里。

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
| toggle-mute-track.js | track | 切换静音 | **轨道头**右键 | 选中轨道切换 mute |
| tag-track-parts.js | trackContent | 标记轨内所有 Part | 编排区**空白泳道**右键 | 该轨所有 part 名追加 " *" |
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
2. 编排区**空白处**右键 → **不应**有「标记选中 Part」（那里是 trackContent，见 §4b）。

## 4a. 轨道头右键（context=track）

1. **轨道头**右键（未选中的轨道会先被选中；可 Ctrl 多选几条再右键）→ 菜单底部有「切换静音」。点击 → 选中轨道的 mute 翻转；Ctrl+Z 撤销。
2. 重复打开不叠加（轨道头菜单只建一次→每次打开重建工具区，工具项只有一份）。

## 4b. 编排区空白泳道右键（context=trackContent）

1. 编排区某轨的**空白泳道**右键（会选中该行轨道）→ 菜单底部有「标记轨内所有 Part」。点击 → 该轨所有 part 名各追加 " *"；Ctrl+Z 撤销。
2. **不应**出现 part 工具「标记选中 Part」（那是命中 part 时才有）。

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

## 10. Agent 造工具（save_script / list_scripts / read_script / delete_script）

> 需配好一个 agent 模型 provider，在 Agent 侧栏对话。

1. **造工具**：对 agent 说「帮我做个工具，把当前 part 所有音符时值翻倍，放到钢琴空白右键菜单」。期望：agent 调 `get_script_api` 看约定 → `save_script`（context=partContent，含 getScriptInfo+main）→ 回报「Registered as menu tool "…" in the piano-roll blank right-click menu」。随后钢琴卷帘**空白右键**即见该工具，点击生效、可撤销。
2. **破损不落库**：让 agent 存一个 getScriptInfo 里有语法错误的脚本。期望：`save_script` 返回 eval 错误、**未保存**（list_scripts 看不到、菜单无），agent 据错误改写重试。
3. **列/读/删**：「列出我的脚本」→ `list_scripts`（标出工具+context / plain）；「看看 X」→ `read_script`；「删掉 X」→ `delete_script`，删后菜单同步消失。
4. **复用主张**：对 agent 提**重复性**诉求（「我以后经常要给选中音符加八度」），期望它倾向 `save_script` 造可复用工具，而非每次 run_script。
5. **回归**：save_script 只存不执行（存完工程无改动、无新撤销项）；run_script 一次性仍正常。

---

## 回归检查（不应被破坏）

- agent 的 run_script 仍可用；出错返回「All changes were rolled back…」。
- Script 侧栏普通脚本运行、Doc/Code 切换、脚本库增删改查不受影响。
- 钢琴/编排原有右键菜单项（Copy/Cut/Octave/Set Voice/Delete 等）位置不变，脚本工具在其下方分隔线后。
