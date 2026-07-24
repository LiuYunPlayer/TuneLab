# 编写 deriver 插件（开发指南 + AI 参考）

> 面向插件作者与 AI 助手。设计动机与取舍见 [deriver-sdk-design.md](deriver-sdk-design.md)；
> ABI 冻结 / DIM / 加性演化纪律见 [sdk-api-evolution.md](sdk-api-evolution.md)。
> 本文只讲「怎么写一个 deriver 引擎」的实操契约，以当前 SDK 面为准。

## 1. deriver 是什么

deriver 是**一次性、音频驱动的派生**插件类型：对一段固定音频跑一次离线模型，从它派生出新的工程材料
（audio→MIDI 转写、音高、节拍/速度、声部 stem、变声…）。与 voice / instrument / effect 的反应式模型相反：

- 用户显式触发一次，**不监听输入变更自动重跑**；
- 产物是**用户拥有、可自由编辑**的新 part / 新轨（不是只读回显）；
- **源音频保留**，产物并存（派生而非取代）。

一个引擎类 = 一种派生算法。

## 2. 实现 `IAudioDerivationEngine`

```csharp
public sealed class MyDeriverEngine : IAudioDerivationEngine
{
    public void Init() { /* 懒加载模型；失败抛异常 */ }
    public void Destroy() { }

    // 参数面板：当前情境（context）的纯函数、不依赖 Init。静态面板忽略 context 返回固定 config 即可。
    public ObjectConfig GetPropertyConfig(IAudioDerivationContext context) => mConfig;

    public Task<DerivedResult?> Derive(IAudioDerivationInput input,
        IProgress<DerivationProgress> progress, CancellationToken cancellation = default) { ... }
}
```

manifest 声明（`type` = `deriver`，`engine` = 身份 id，`classes` 列入口类）：

```json
{
  "id": "com.example.mypack", "name": "My Pack", "version": "1.0.0", "sdk-version": "1.0",
  "extensions": [
    { "type": "deriver", "engine": "MyPitchToMidi", "name": "Transcribe to MIDI",
      "classes": ["Example.MyDeriverEngine"], "assembly": "MyPack.dll" }
  ]
}
```

`engine` 是跨包可重名的身份 id；`name` 仅供 UI 展示、可本地化。多包声明同 `engine` 均并存，用户在
「Extension Routing」矩阵选活实现。

### 没有静态「能力声明」

「能产什么」不做预声明——产物随参数而变、无法（也不该）提前预知，**唯一真相是运行时结果**
（`DerivedResult` 里哪些槽非空）。宿主对空结果 no-op + 提示。

### `GetPropertyConfig` 是反应式的、不依赖 Init

对话框在用户改任一值时按当前 `context` 重算 config 并 diff 到控件树（条件字段随值显隐）。故它须是
**当前情境的纯函数**、不读模型状态——对话框在 **Init 之前**即会反复调用。`context` v1 只提供当前已填参数值
（`Properties`，稀疏）；`IAudioDerivationContext` 是宿主实现的接口、将来可加性扩展（如源音频精确样本数），
不破已装插件。**Init 只在真正 `Derive` 前触发。**

### `Derive` 契约

- 收一份不可变输入快照（冻结源音频 + 冻结参数）→ 返回一次产物；
- **`null` = 取消或什么都没产出**；取消是正常结局（返回 `null`、**不要**抛 `OperationCanceledException`）；
- 真正错误才抛异常，宿主 catch（任务标记失败）；
- `progress.Report(new DerivationProgress { Progress = p, Message = "…" })` 报进度 [0,1] + 一句文案；
- 宿主在**后台 worker 线程**调用——你只读输入快照，永不回碰宿主数据。

## 3. 输入：`IAudioDerivationInput`（宿主实现、你只读）

```csharp
int SampleRate; int ChannelCount; long SampleCount;
void Read(int channel, long offset, Span<float> destination);   // 冻结源音频随机访问，越界非法
PropertyObject Properties;                                       // 冻结的参数值
```

- **喂的是整段源音频内容**（位置无关、与工程时间线 / 裁剪无关）。裁剪是 apply-side、不进输入。
- **按声道分读**（planar）：`Read(channel, …)` 逐声道取；要单声道自己下混（逐声道读了平均）。
- 参数用稀疏值语义读：`Properties.GetDouble("key", 默认)`——缺键即未设、用默认。
- 快照**全程不变**：copy-out 到自有缓冲后随意分析。

## 4. 产物：`DerivedResult`（purpose-built 派生产物族）

产物是一套专为「描述分析产出」设计的表示（`Derived*`），**刻意不与导入导出格式 `DataInfo`（`*Info`）同构**
——那套表达用户意图 / 可编辑工程状态，本套是对音频自然属性的描述。全程物理秒，宿主转 tick。

```csharp
new DerivedResult
{
    Tracks = new[] { new DerivedTrack { Name = "Transcribed", Parts = new DerivedPart[]
    {
        new DerivedMidiPart
        {
            // 不设 StartTime/EndTime → 默认 0..+∞ = 整段（裁剪归宿主 apply 侧）
            Notes = new[] { new DerivedNote { StartTime = 1.2, EndTime = 1.6, Pitch = 60, Lyric = "la" } },
            Pitch = new DerivedPitch { Segments = new IReadOnlyList<Point>[] { new[] { new Point(1.2, 60.1) } } },  // (秒, 半音)
        },
    } } },
    // Tempos / TimeSignatures 省略 = 空 = 不产此项
};
```

### 单位纪律

- **一律说物理量**：秒 / BPM / MIDI 半音。**绝不碰 tick**——秒→tick 换算全在宿主侧。
- **所有位置都是绝对音频内容秒**（采样点 0 = 0 秒）：part `Time`、`note.StartTime/EndTime`、`Pitch.Segments` 的 `Point.X` 同坐标系。
- `Pitch`：具名封装 `DerivedPitch { Segments }`，nested-segments：各段时间升序、互不重叠；段内 `Point =(秒, 半音 float)`；段间间隙 = 自由区（不填 NaN）。
- **空 = 「这项我不产」**：集合空 / 字符串空 / `DerivedPitch.Segments` 空皆表示不产（全非空默认，无可空、免 NRE）；只填你专精的槽。

### part 层与裁剪

保留 part 层（而非把内容直接挂轨）是为了表达**轨内多个不重叠 part**（如按静音段切分整曲的插件，产物天然是
一轨内若干音频 part）。`DerivedPart` 就是占一段绝对内容秒 **`[StartTime, EndTime]`**（不用 DataInfo 的锚点+偏移裁剪模型）。`StartTime`
默认 **0**（内容起点）、`EndTime` 默认 **+∞**（终点开放、宿主应用时钳到内容/输入末）——只关心一端的插件只设该端即可，
另一端零心智负担。切分型插件（如按静音段切分）才两端都显式给、界定各 part 窗口。

### 音符与音素

`DerivedNote { StartTime, EndTime, Pitch, Lyric?, LeadingPhonemes, BodyPhonemes, BodyOffset }`。
音素用 **`DerivedPhoneme { Symbol, Duration, StretchWeight }`**（取 `DataInfo` 音素结构之形的独立类型、不共类型、
不带创作字段 Properties），单位秒：转谱 / 强制对齐类插件能产音素级信息，落成真实 `MidiPart` 后就是可编辑用户数据、
音素域本就是秒基，宿主逐字转换、零信息损失。不产音素就留两列表为空。

### v1 不含 automation 产物

「可编辑 automation」与「只读参考曲线」是两种东西、需各自设计，故 v1 不产 automation，将来纯加性补。

### 精简字段

只留派生得出的东西，丢尽 gain/pan/color/soundsource/effects/vibrato/properties 等创作字段——宿主并入工程时以默认填
（如新 MIDI part 音源默认为空，用户再指派）。

## 5. 宿主怎么用你的产物（你不必管，但要知道）

1. **提交**（数据线程）：宿主冻结整段源音频 + 参数为 `IAudioDerivationInput`，与源解耦，工程零改动。
2. **运行**（worker）：调你的 `Derive`。结果存入用户目录**内容寻址缓存**（键 = 源音频内容 hash + engineId +
   插件 manifest version + 参数 hash）——移动 / 裁剪 part 都不改键、再触发即秒出、跳过模型。
3. **待应用**：完成后进「待应用」态（中央「派生」面板可见），工程仍零改动。
4. **应用**（用户显式）：宿主按当前工程时间线把秒换算成 tick、按当前裁剪窗口过滤，作**一条普通栈顶 undo 命令**
   新建轨/part 落地（插源轨之下；源已删则追加末尾）。音素与 `BodyOffset` 秒基、逐字落地。

## 6. 参考实现

`tests/plugins/V1.Deriver/`：基于自相关的单声部 pitch→note 转写，产音符 + 音高曲线。UI 显示名
= `Transcribe to MIDI`（右键工程内音频 part →「派生」子菜单）。

## 7. 演化纪律

- 产物 DTO 新增可空槽是加性的（旧插件不填即 `null`）；
- 引擎接口新增成员一律用默认接口方法（DIM）兜底；
- 破坏性改动须先与维护者确认（见 [sdk-api-evolution.md](sdk-api-evolution.md)）。

## 8. 尚未开放 / 缓后

- **音频产物**（stem / 变声 / 切分音频片段，落 `DerivedAudioPart`）：v1 不产，交付机制待实现音频输出时定死，
  现阶段 `DerivedAudioPart` 仅占位。
- **automation 产物**（可编辑 automation + 只读参考曲线两套）：推迟、加性补。
- **曲线/属性 retargeting**（把音高/歌词落到用户指定的已有 part）：v1 推迟。
