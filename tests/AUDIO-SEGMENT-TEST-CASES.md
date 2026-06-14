# 音频段握柄测试用例（voice→effect 段化 + 按段增量重渲染）

> 本文覆盖**音频产物经 `IAudioSegment` 段握柄交付**、以及 **effect 链按段独立过链 / 按段增量重渲染**的受影响范围。
> 模型：voice 经 `context.CreateAudioSegment` 交付的每个音频段各自过 effect 链（cache[segment][stage]）——
> 某段重合成只重过该段的链、其余段缓存复用；effect 参数/启用/自动化变化则各段从该级重跑。
> **逐段的粒度 = voice 分块粒度**（段边界归 voice 分片：note 间有间隙才分成多块/多段）。
> voice 会话调度 / 状态带 / 失效重排的完整用例见 [VOICE-SESSION-TEST-CASES.md](VOICE-SESSION-TEST-CASES.md)，
> effect 链/参数/自动化用例见 [EFFECT-TEST-CASES.md](EFFECT-TEST-CASES.md)，本文不重复。
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
- `v1-effect.tlx` —— 真 effect 包，含 **TLTestGain**（参数 `gain` 0~2、默认 1）与 **TLTestReverse**（倒放）：验证段拼接后 effect 链仍生效。
- `legacy-voice.tlx` —— 1.x 自包含声库，经 compat 层加载（compat 单段路径）。

> 选声库 / 加 effect 的入口都用菜单/面板里的**显示名**（上面加粗的），不是插件 Type id。

---

## 一、voice 音频经段握柄交付

### 1. 多块声库出声
- 用 **Alice (V1 Test)** 写两簇隔开的 note（中间留明显间隙）→ 状态带分两段、依次变绿。
- 播放 → **每簇都能听到正弦**，音高随 note；波形**每簇各自一段**，**间隙处留白**（不画静音线，回到 legacy 分 piece 形态）。
- 期望：多段各自交付/混音/绘制，无错位、无重叠串扰。

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

## 三、effect 链按段独立过链 / 按段增量

> 加 effect 入口：选中 MIDI part → 右侧 **Properties** 侧栏 **Effects** 面板 → Add。先确保 Alice 已能出声作基线。

### 7. Gain 各段独立过链
- Alice 写两簇隔开的 note（两段），加 **TLTestGain**，`gain` 调到 2 → 两簇都明显变响；调到 0 → 静音。
- 期望：每段各自过 gain、按时间拼接（gain 逐样本，结果与整段过等价）。

### 8. Reverse 逐段倒放（粒度 = voice 分块）
- 写**三个隔开**的 note（间有间隙 → voice 分三块 → 三段），加 **TLTestReverse** → **每个音符各自倒放**（段内倒放），不是三个作为整体倒放。
- 把三个 note **首尾相连**（无间隙 → voice 归一块 → 一段）→ reverse 是这三个**作为一段一起倒**。
- 期望：逐段粒度由 voice 分块决定——隔开则逐段倒、相连则整段倒；段边界归 voice 分片。

### 9. 改一个音符只重过那一段（提交② 核心收益）
- 三段隔开的 note 挂 **TLTestReverse**，全部合成完。改**中间**那个 note 的音高 → 只有中间段 voice 重合成 + **只有中间段重过 reverse**，另两段的 effect 输出**缓存复用、不重跑**。
- 期望：编辑期 effect 不再整 part 重过；改一个音符的等待 ≈ 一段而非整条 part（SVC 类慢模型差异显著）。

### 10. effect 参数变化：各段从该级重跑（与 voice 段脏正交）
- 链上加两级 **TLTestGain → TLTestReverse**，三段都合成完。改 `gain` 值 → 各段从 gain 级重跑（reverse 级随后重算），**voice 不重合成**。
- 期望：effect 参数脏 = 各段 stage 级增量（每段 cache 从该级起失效），与"改 note → 某段重合成"两个维度互不干扰。

---

## 四、回归底线

### 11. 切声库 / 删 part / 关工程
- Alice↔Bob 来回切、删 part、关闭并重开工程 → 无崩溃、无音频泄漏（旧段握柄随会话销毁）、无 `AssertDataThread` 异常弹出（DEBUG 构建）。
- 删光一个 part 的所有 note → 该 part 不再出声、波形清空（段全销毁、最终音频归空）。
