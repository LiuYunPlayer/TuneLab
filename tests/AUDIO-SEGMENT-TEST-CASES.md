# 音频段握柄测试用例（voice→effect 段化 · 提交①）

> 本文只覆盖**音频产物从 `ReadAudio` 扁平拉取改为 `IAudioSegment` 段握柄交付**的受影响范围。
> 提交① 是**行为保持的重构**：voice 经握柄交付音频，宿主把各段拼成单条 buffer 喂**现有整 part effect 链**——
> 听感 / effect 效果 / 状态带应与改动前**完全一致**。按段增量重渲染（per-segment effect）是提交②，不在此测。
> voice 会话调度 / 状态带 / 失效重排的完整用例见 [VOICE-SESSION-TEST-CASES.md](VOICE-SESSION-TEST-CASES.md)，
> effect 链/参数/自动化用例见 [EFFECT-TEST-CASES.md](EFFECT-TEST-CASES.md)，本文不重复，只做段化后的回归冒烟。
> 每个用例：**做什么 → 预期看到什么**。

## 准备

```bash
dotnet build TuneLab.sln -c Debug
dotnet build tests/TestPlugins.slnx -c Debug
powershell -File tests/pack-tlx.ps1          # 打成 tests/tlx/*.tlx
```

逐个拖进 TuneLab 窗口安装（或扩展侧边栏 → Install Extension）：
- `v1-voice.tlx` —— 声库 **Alice (V1 Test)** / **Bob (V1 Test)**：按 note 间隙**分块**合成正弦（多段握柄主力夹具）。
- `v1-suite.tlx` —— 声库 **Suite Voice (V1 Test)**：整 part **单块**、静音输出（单段握柄）。
- `v1-effect-real.tlx` —— 真 effect 包，含 **TLTestGain**（参数 `gain` 0~2、默认 1）与 **TLTestReverse**（倒放）：验证段拼接后 effect 链仍生效。（注意：`v1-effect.tlx` 是「预期被跳过加载」的 skip 变体，不是真包，别装错。）
- `legacy-voice.tlx` —— 1.x 自包含声库，经 compat 层加载（compat 单段路径）。

> 选声库 / 加 effect 的入口都用菜单/面板里的**显示名**（上面加粗的），不是插件 Type id。

---

## 一、voice 音频经段握柄交付（行为保持）

### 1. 多块声库出声
- 用 **Alice (V1 Test)** 写两簇隔开的 note（中间留明显间隙）→ 状态带分两段、依次变绿。
- 播放 → **每簇都能听到正弦**，音高随 note；波形与改动前一致（每簇一段波形，间隙处静音）。
- 期望：与段化前听感无差异（多段握柄各自交付、宿主按时间拼接无错位、无重叠串扰）。

### 2. 单块声库
- 用 **Suite Voice (V1 Test)** 写一串 note → 单段状态带、合成完成变绿。
- 期望：静音输出（该夹具本就产静音），无报错、无残留旧音频；波形为整 part 一段（值为 0）。

### 3. legacy 声库（compat 单段）
- 用 **legacy 声库**写 note → 正常合成出声。
- 期望：compat 经单段握柄交付，听感与改动前一致。

---

## 二、编辑后重合成（段销毁/重建）

### 4. 改音高
- Alice 合成完后改某簇里一个 note 的音高 → 该簇变灰重合成、回绿。
- 播放 → 改动的音高生效，**其它簇音频不变**（其握柄未动）。

### 5. 增删 note / 改间隙触发重分块
- 在两簇之间补一个 note 把间隙填上，使两簇并成一块 → 重分块后状态带变一段、重合成。
- 删掉它恢复间隙 → 又回两段。
- 期望：分块变化时旧段握柄释放、新段重建，无音频残留/错位、无崩溃。

### 6. 全选移调（全脏重排）
- 全选移调 → 所有段变灰、就近优先依次回绿，音频整体移调。
- 期望：批量编辑只重分块一次（BatchEnd），段重建后音频正确。

---

## 三、effect 链经拼接后仍生效

> 加 effect 入口：选中 MIDI part → 右侧 **Properties** 侧栏 **Effects** 面板 → Add。先确保 Alice 已能出声作基线。

### 7. Gain 改变响度
- 给一个 Alice part 加 **TLTestGain**，`gain` 调到 2 → 重过链后音量明显变大；调到 0 → 静音。
- 期望：voice 各段拼成的整段进 effect、整段输出，gain 效果与段化前一致。

### 8. Reverse 整段倒放
- 加 **TLTestReverse** → 整段音频倒放（注意是整 part 一条链倒放，不是逐段倒放）。
- 期望：拼接边界无异常断点；与段化前行为一致。

### 9. voice 改动触发整链重跑
- 在 7 的基础上改一个 note 音高 → voice 该段重合成完，**整条 effect 链重跑**（提交① 仍整 part 过链）、gain 仍生效。
- 期望：与段化前一致（per-segment 只重过改动段是提交②的目标，此处仍整链）。

---

## 四、回归底线

### 10. 切声库 / 删 part / 关工程
- Alice↔Bob 来回切、删 part、关闭并重开工程 → 无崩溃、无音频泄漏（旧段握柄随会话销毁）、无 `AssertDataThread` 异常弹出（DEBUG 构建）。
