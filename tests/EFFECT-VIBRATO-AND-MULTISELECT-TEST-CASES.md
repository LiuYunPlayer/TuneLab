# Effect 颤音关联 + Effects 面板多选 + PropertyValue JSON 统一序列化 — 测试用例

> 覆盖三个同批落地的特性，只测受影响范围，不重复既有基线（effect 基线见 EFFECT-TEST-CASES.md、
> 状态带见 EFFECT-STATUS-BAND-TEST-CASES.md、音频段/账本与 Resize 见 INPAINT-EFFECT-TEST-CASES.md）。

## 前置

- 构建并安装测试插件包：`V1 Test Effect`（effect 引擎 UI 显示名：**Gain** / **Reverse** / **Slow Gain** / **Fail Demo**）
  与任一测试声库（如 `V1 Test Voice` 正弦）。
- 新建工程，建 1 个 MIDI part、写几个音符，确认能听到 voice 输出。
- 颤音相关用例使用 **Gain** effect（带连续自动化轨 **Gain Env**，量程 0–2、默认 1）。

---

## A. 颤音影响 effect 参数

### A1. 关联建立（拖拽）
1. 给 part 添加 effect **Gain**，钢琴窗切到颤音工具，在若干音符上加一个颤音。
2. 底部参数栏把 active 轨切到 Gain 的 **Gain Env**（effect 组内）。
3. 在参数区把光标悬到颤音上：应出现 “Drag to associate the vibrato” 提示（此前 effect 轨无此提示）。
4. 按住拖动上下调节：曲线在颤音覆盖区出现波动（振幅随拖动变化）；主曲线 = 含颤音的终值，
   颤音覆盖区另有一条半透明基线（不含颤音的原曲线）。
5. 松手后播放：听感随 Gain Env 波动（音量按颤音频率起伏）。

### A2. 解除关联（双击）与三态回归
1. 选中该颤音，在参数区双击：Gain Env 上该颤音的波动消失、曲线回到基线。
2. Ctrl+Z：关联与振幅恢复；Ctrl+Y：再次解除。
3. active 切回 voice 自动化轨（如 Volume）：voice 轨的颤音关联行为与改动前一致（回归）。

### A3. 合成失效（听感与状态带 + 标脏分源）
1. 建立 A1 的关联后，拖动颤音的位置/长度/频率：Gain 段应重新过链（状态带闪合成中→绿），播放听感同步变化。
   几何/频率属 voice 可听变化，note 区间同时重合成属预期。
2. **只调 effect 关联振幅**（A1 的拖拽）：**voice 不得重合成**（note 区间不出现合成中），只有 effect 段重跑；
   松手后播放听感变化、波形区波形随之更新。
3. 编辑与该 effect 段时间不相交的颤音：该段不应重跑（状态带无变化）。

### A4. 链结构变更下关联跟随实例（id 锚定，零重映射）
1. part 上加两个 effect：**Gain**（槽 0）与 **Slow Gain**（槽 1），给 Gain 的 Gain Env 关联一个颤音。
2. 在 Effects 面板把 Gain 下移（Gain↔Slow Gain 互换槽位）：Gain Env 的颤音波动**跟随 Gain 移动**（不会错挂到 Slow Gain 的轨上）。
3. Ctrl+Z 撤销移动：波动仍在 Gain 上、振幅不变。
4. 删除 Gain：关联条目成孤儿（不报错）；Ctrl+Z 恢复——同 id 重连，波动原样回来。
5. 复制该 part（编排区复制粘贴）：副本 part 里颤音对 Gain Env 的波动独立成立（副本引用副本的 effect）。

### A5. 工程往返
1. 建立 A1 关联后保存工程（.tlp），关闭重开：颤音对 Gain Env 的波动原样恢复（振幅一致）。
2. 分别用二进制与 JSON 导出路径各验一次（若入口只有一种格式则只验默认格式）。

## B. Effects 面板多选（按槽位对齐合并，链不齐不整块隐藏）

### B1. 同型槽位合并展示
1. 建两个 part（同或不同声库均可），各自添加相同链：Gain。编排区多选两个 part。
2. 侧栏 Part 面板：Effects 面板**可见**，显示一个 Gain 槽位。
3. 两 part 的 Gain 参数值相同 → 参数控件显示该值；改其中一个 part 的 Gain gain 值后再多选 → 控件呈 Multiple（"-"/占位形态）。
4. 多选下编辑参数：两 part 一起变（单次 Ctrl+Z 一起回退）。
5. bypass 勾选框：两 part 状态不同 → dash 三态；点击 → 两 part 统一，且一个撤销步。

### B2. 链不齐的槽位呈现（Multiple / empty 占位）
1. **同长不同型**：把一个 part 的 Gain 换成 Reverse（用 B5 的替换），多选：该槽标题显示 **(Multiple)**，
   无参数区，仍有 bypass（三态）与删除、替换按钮。
2. **链长不同**：一个 part 链 [Gain]、另一个 [Gain, Slow Gain]，多选：显示两个槽位——槽 0 = Gain 正常合并；
   槽 1 只有一个 part 有实例（另一 part 为 empty 占位），类型全等 → 仍显示 Slow Gain 全功能，
   参数/bypass 只作用于有实例的 part。
3. 槽 1 点删除：只删有实例的 part；点替换选某类型：有实例的 part 原位换、链短的 part **补位**（两边都出现该槽位）。
4. 链等长时显示 ↑/↓ 移位按钮；不等长时移位按钮隐藏（删除/替换仍可用）。

### B3. 结构操作扇出（Add 补空对齐）
1. 等长多选下点「Add Effect」加 Slow Gain：两个 part 同槽位追加；一个撤销步同时回退两边。
2. **不等长多选 Add**：链 [Gain] 与 [Gain, Reverse] 多选，Add Slow Gain：短链 part 中间自动补一个 **(empty)**
   数据级占位（passthrough、不出声不报错），新效果在两个 part 落进同一槽位、补完后两链等长（移位按钮出现）；
   一个撤销步回退全部（含补位）。(empty) 槽可点标题替换成真 effect。
3. 多选下移动/删除槽位：两边链同步变化，撤销一致；有颤音关联时结合 A4 验证重映射同样扇出。
4. 含 (empty) 占位的工程保存/重开：占位保留原位、合成不受影响（passthrough）。

### B4. 自动化默认值行多选
1. 两 part 都有 Gain 的槽位，Gain 块下的 Gain Env 默认值行：两 part 默认值相同显示该值、不同显示 "-"。
2. 拖动该行滑条：扇出到两 part（听感/曲线基线同步变），松手一个撤销步。

### B5. 替换 effect（保数据换 Type，单选即可测）
1. 单选 part，链 [Gain]：调过 gain 参数、画过 gain_env 曲线后，点槽位类型标签 → 弹引擎菜单 → 选 Reverse：
   原位替换为 Reverse，参数区随新引擎重建。
2. **不经 undo** 再替换回 Gain：gain 参数值、gain_env 曲线、颤音关联**全量恢复**（替换保数据，
   旧引擎的键/轨在新引擎下按孤儿隐藏保留——与 voice 换引擎不清 automation 同判例）。
3. Ctrl+Z 两步依次回退两次替换，状态一致。
4. 选择与当前相同的类型：无操作（不产生撤销步）。

## C. PropertyValue JSON 统一序列化（PropertyArray 臂）

### C1. preset 数组往返
1. 用带数组/列表属性的测试声库（`[v1-suite] Conditional` 的 phonemes/pair 等）设置 part 属性里的数组值。
2. 侧栏 Preset「Save As」存为预设；换一个 part Apply：数组属性完整应用（此前会被静默丢掉）。
3. 重启应用后 Apply 同一预设：数组属性仍完整（Presets.json 往返）。

### C2. TLP JSON 数组往返
1. 同 C1 的数组属性写入工程，经 JSON 格式导出再导入：数组属性完整（此前 JSON 路径丢数组）。
2. 嵌套形态各验一例：数组套对象、对象套数组、空数组（present-[] 不得丢键）。

### C3. 扩展设置回归
1. 任一带设置页的扩展（含密钥字段的更佳）：改值、保存、重启读取——行为与改动前一致
   （存储迁 Newtonsoft + 共用转换，文件仍为原生 JSON 形；密钥字段 Windows 上仍为 DPAPI 密文）。
2. 含 ListConfig/ArrayConfig 设置的扩展存取数组值往返完整。
