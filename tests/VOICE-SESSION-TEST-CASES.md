# Voice 会话模型功能测试用例

> 本文只覆盖 **voice SDK 会话模型迁移**的受影响范围（合成调度 / 失效重排 / 产物显示 / compat）；
> format/effect/属性面板等既有功能用例见各自文档，不在此重复。
> 每个用例：**做什么 → 预期看到什么**。

## 准备

```bash
dotnet build TuneLab.sln -c Debug
dotnet build tests/TestPlugins.slnx -c Debug
powershell -File tests/pack-tlx.ps1          # 打成 tests/tlx/*.tlx
```

- 安装：把 `tests/tlx/v1-voice.tlx` 拖进 TuneLab 窗口（或扩展侧边栏 → Install Extension）。
- 测试声库（轨道 part 右键 → Voice 菜单里的**显示名**）：
  - **Alice (V1 Test)** / **Bob (V1 Test)** —— `v1-voice.tlx`，按 note 间隙分块合成正弦（多块状态带的主力夹具）。
  - **Suite Voice ...** —— `v1-suite.tlx`，整 part 单块、静音输出（单块模式 + 属性面板夹具）。
  - legacy 用例另需 `legacy-voice.tlx`（1.x 自包含插件，经 compat 层加载）。
- 写几个 note 间留出**明显间隙**（比如两簇音符隔开一拍），是多块用例的前提。

---

## 一、加载与会话创建

### 1. 安装加载
- 装 `v1-voice.tlx` → 扩展侧边栏 **Loaded**，类别 **voice**。
- 轨道 part 的 Voice 菜单出现 **Alice (V1 Test)** 与 **Bob (V1 Test)**。

### 2. 选声库 = 重建会话
- 给 part 选 **Alice (V1 Test)** → part 标题显示 `[Alice (V1 Test)]`；钢琴窗顶部出现合成状态带。
- 切到 **Bob (V1 Test)** → 状态带全部回到待合成（灰）→ 自动重新合成（会话随声源重建）。

### 3. 空声源回退
- 不选声库（或选回空）→ 无状态带、不参与合成、播放静音，不报错。

---

## 二、调度与状态带

### 4. 分块状态带
- 用 Alice 写两簇隔开的 note → 状态带显示**两段独立区段**（不是连成一条）。
- 合成过程：灰（待合成）→ 橙（合成中，左侧渐变绿表示进度，显示 "rendering" 文案）→ 绿（完成）。

### 5. 播放线就近优先
- 写多簇 note，把播放线放在中间一簇上 → 重新合成时（比如全选移调触发全脏），**播放线之后**的块先变绿，远处的块后完成。

### 6. 多 part 并行
- 建多条轨各放一个 Alice part，全部触发重合成 → 多个 part 的状态带**并行**推进（并发上限 = CPU 核数，不再一次只跑一个）。

---

## 三、变更失效（增量重排）

### 7. 编辑 note 只重合成所在块
- 两簇 note 全部合成完（全绿）→ 拖动**第一簇**里一个 note 的音高 → 只有第一簇的状态带变灰重合成，**第二簇保持绿色不动**。

### 8. 增删 note 重分块
- 在两簇之间补一个 note 把间隙填上 → 状态带合并为一段并重合成。
- 删掉它 → 重新分成两段，未受影响的簇保留已合成结果。

### 9. pitch / automation 区间失效
- 在第一簇范围内画 pitch 曲线 → 只有第一簇重合成。
- 切到自动化轨 **Growl** 在第二簇范围画值 → 只有第二簇重合成。

### 9a. 音高双通道：vibrato 作用于未绘制区域
- **不画任何 pitch**，给第一簇 note 加 vibrato → 该簇重合成，播放能听到**颤音**
  （旧管线 vibrato 只叠在绘制曲线上、未绘制区域丢失——本用例即验证 PitchDeviation 通道的结构性修复）。
- 在 vibrato 覆盖范围内再画一段 pitch → 重合成后该段按"绘制曲线 + 颤音"发声（绝对约束 + 加性偏差叠加）。
- 拖动 vibrato 位置/调振幅 → 只有相交块重合成（deviation 通道的区间失效）。

### 10. 批量编辑只重排一次
- 框选全部 note 整体拖动（移调/平移）→ 拖动过程中**不**逐帧重合成；松手后一次性重排。
- Ctrl+Z 撤销 → 同样一次性重排回原状（undo/redo 重放批量括号）。

### 11. tempo / part 平移 = 全量重排
- 改 tempo 或拖动整个 part 位置 → 该 part 全部状态带变灰重合成（秒域派生全部失效）。

---

## 四、产物显示

### 12. 波形与音频
- 合成完成后钢琴窗底部波形区显示正弦波形；播放能听到对应音高的正弦音。
- 音量自动化（Volume 轨）画低 → 波形显示与听感随之变小（混音阶段应用，不触发重合成）。

### 13. 合成音素
- 波形区每个 note 显示音素分段（Symbol = 歌词），随合成完成出现/更新。
- 拖动 note 后重合成，音素跟随更新。

---

## 五、effect 链联动

### 14. 整 part 过链
- Alice part 加 **TLTestGain**（见 EFFECT-TEST-CASES.md）→ 合成完成后链尾输出生效（gain=0 静音 / 2 更响）。
- 编辑一个 note → 该块重合成后 effect 链**自动重跑**，无需手动操作。
- 改 effect 参数 → 只重跑链（voice 不重合成，状态带保持绿色）。

---

## 六、Legacy 1.x 插件（compat 层）

### 15. legacy voice 加载与合成
- 装 `legacy-voice.tlx` → 侧边栏类别 **voice**（经 compat 注册）。
- 选其声库写 note → 正常分块合成出声（compat 把老 task 模型包成会话）。

### 16. legacy 失效行为
- 编辑 note / 画 pitch → 对应块重合成（compat 的懒脏策略按块标脏）。
- 批量拖动 → 松手后一次性重排。

---

## 七、回归口

- `dotnet test tests/TuneLab.Tests` → 33 用例全绿（数据层/快照件不受管线迁移影响）。
- v1-suite / v1-i18n 的属性面板用例（PROPERTY-*.md / PLUGIN-I18N-TEST-CASES.md）行为不变（声明面从声源移到会话，消费路径等价）。
