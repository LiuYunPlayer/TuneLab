# 标签组（TagConfig）· 测试用例

> 范围：新控件 TagConfig——从一组固定候选里挑若干个值，chip 横排、自动换行，末尾 `+` 弹候选菜单。
> 语义：**有序、不重复**（新加的追加到末尾、可拖动调序、已选的不进 `+` 菜单）。
> `+` 菜单本身（分组 / 分隔线 / 搜索）已由 `ADDABLE-GROUP-AND-MENU-SEARCH-TEST-CASES.md` 覆盖，这里只测 tag 特有的部分。

## 前置

- 安装重建后的 `v1-suite.tlx`。打开夹具 `tests/fixtures/tag-config.tlp`：一条轨、一个片段，声库 **`[v1-suite] Conditional`**。
  - 片段属性 `Timbres` 预置 Power、Mellow。
  - 四个音符的 `Moods`：①未设置（显示默认 Calm）②Warm、Bold ③Bold、Level 2 ④Warm、`gone`、Warm（含候选外的值与重复值）。
- 片段属性看右侧 **片段** 面板，音符属性看 **音符** 面板。

## A · 片段面板 `Timbres`：基本增删与顺序

- [ ] 显示两个 chip：**Power、Mellow**（按存值顺序），末尾一个 `+`。参数面板多出两条曲线 **Power Amount、Mellow Amount**，顺序同 chip。
- [ ] 点 `+` → 菜单为 Bright ▸ Resonant / Soft ▸ Breathy / 分隔线 / Raw：**已选的 Power、Mellow 不在菜单里**。
- [ ] 选 Raw → Raw 追加在**末尾**（Power、Mellow、Raw），参数面板多出 Raw Amount 排在最后。
- [ ] 把 Bright、Soft 下剩下的都加上 → 两个分组从菜单消失；五个全选后 `+` 消失。
- [ ] 点某个 chip 上的 × → 只删它，`+` 重新出现、菜单里又有它。
- [ ] Ctrl+Z / Ctrl+Y：每次增 / 删 / 调序都是**一步**撤销。

## B · 选中、Delete、右键

- [ ] 点 chip → 选中（底色提亮 + 描边）；再点同一个 → 取消选中。
- [ ] Ctrl 点击增减选；Shift 点击从锚点连选。
- [ ] 选中两个后按 **Delete** → 两个一起删；钢琴窗里的音符不受影响。
- [ ] 右键一个**未选中**的 chip → 只删它；右键一个**已选中**的 → 删整组选区。
- [ ] 点侧栏空白处后按 Delete → 什么都不发生。

## C · 拖动调序

- [ ] 拖一个 chip 越过邻居中线 → 出现一条竖线标出插入处，被拖的 chip 压暗；松手后落到竖线处。参数面板的曲线顺序随之变。
- [ ] 拖到行首 / 行尾 / 换行后的下一行都能落准；面板窄到 chip 换行时依然可用。
- [ ] 选中两个后拖其中一个 → 两个一起挪，保持彼此原有次序。
- [ ] 拖动中途被打断（按住左键拖着时按 Alt+Tab 切到别的窗口）→ 不提交，竖线消失、chip 恢复原样、顺序不变。
- [ ] 拖到 TuneLab 窗口外再松手 → 照常落在最后一次显示竖线的位置（拖出窗口不算打断）。

## D · 默认值与存值规整（音符面板 `Moods`）

- [ ] 选音符①：显示 **Calm**（默认值，未写入数据）。加一个 Warm → 变成 Calm、Warm（默认值随首次编辑一起物化）。
- [ ] `+` 菜单叶子共 12 个 → 顶部有搜索框，输入 `lev` 只剩 Level 1/2/3（数值型的值显示为 Level N）。
- [ ] 选音符④：显示 **Warm、gone**（重复的 Warm 只显示一次）；`gone` 为**琥珀色**，悬停说明它不在候选里、由插件决定怎么处理。
- [ ] 在音符④上加任意一个 tag → 存值被规整：重复的 Warm 去掉，`gone` **保留**（宿主不替用户删）。删掉 `gone` 后它不会再出现。

## E · 多选（音符面板 `Moods`）

- [ ] 同时选中音符②③：显示 **Warm、Bold、Level 2**——Bold 两个都有（正常）；Warm、Level 2 只有一个有，**半透明**，悬停提示「从 + 里选它即补给全部」。
- [ ] `+` 菜单里 **Bold 不在**（两个都有），**Warm、Level 2 在**；选 Warm → 两个音符都有 Warm，chip 变为不透明。
- [ ] 删除 Bold（× 或选中后 Delete）→ 两个音符都去掉 Bold，一次撤销全部还原。
- [ ] 两个音符的 tag 序列不同 → 拖不动（不起竖线）；把两边调成完全相同的序列后可以拖，且两边同步挪。

## F · 命令面

- [ ] 让 agent 列出该声库的属性（`list_sound_sources` 一类）：`Timbres` / `Moods` 描述为 "tags: an ordered list of distinct values, each one of [...]"，默认值显示为 `["calm"]`。
