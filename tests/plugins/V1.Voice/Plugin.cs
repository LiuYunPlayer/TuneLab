using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Voice;

// V1 voice 测试引擎（会话模型的原生参照实现）：2 个声库；按 note 间隙分块、分块账本托管
// 失效与产物；合成时按每个 note 的音高填一段正弦，并产出按出身 note 归属的扁平 phoneme。
// 用于验证：引擎注册、声库列表、CreateSession、分块状态带（多段着色/进度）、变更标脏增量重合成、
// snapshot.Notes 与 segment.Notes 的索引对齐契约（产物归属回活 note）。
// 测试钩子：把某 note 歌词改成 "fail"，该块合成必失败并带报错文案——验证失败段红条 + hover 报错 + 右键复制。

public sealed class TestVoiceEngine : IVoiceSynthesisEngine
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

    // 声库呈现布局（验证分组下拉全链路）：顶层裸声库(Alice)与分组同层交织、组内嵌套子组(Carol)，
    // v1-bob 故意不列入 → 验证宿主对未覆盖 id 的顶层兜底（按 map 序补出）。
    public IReadOnlyList<VoiceSourceLayoutItem> VoiceSourceLayout =>
    [
        VoiceSourceLayoutItem.Voice("v1-alice"),                              // 顶层裸声库（与下方组交织）
        VoiceSourceLayoutItem.Group("Group A (V1 Test)",
        [
            VoiceSourceLayoutItem.Group("Nested (V1 Test)",
            [
                VoiceSourceLayoutItem.Voice("v1-carol"),                      // 嵌套子组里的声库
            ]),
        ]),
        // v1-bob 未列入 → 宿主兜底补在本引擎顶层
    ];

    public void Init()
    {
        // 立绘路径按包目录拼出（DLL 经 Assembly.Location 自定位）。三态各覆盖一条路径：
        // Alice = 静态 webp，Carol = 动态 webp（animated），Bob = 无立绘。
        var packageDir = System.IO.Path.GetDirectoryName(typeof(TestVoiceEngine).Assembly.Location) ?? "";
        var staticPortrait = System.IO.Path.Combine(packageDir, "portrait.webp");
        var animatedPortrait = System.IO.Path.Combine(packageDir, "portrait-anim.webp");
        mVoiceInfos.Add("v1-alice", new VoiceSourceInfo { Name = "Alice (V1 Test)", Description = "Test voice Alice", Portrait = new FileImageResource(staticPortrait) });
        mVoiceInfos.Add("v1-bob", new VoiceSourceInfo { Name = "Bob (V1 Test)", Description = "Test voice Bob" });
        mVoiceInfos.Add("v1-carol", new VoiceSourceInfo { Name = "Carol (V1 Test)", Description = "Test voice Carol", Portrait = new FileImageResource(animatedPortrait) });
        // 量程端点描述文本（验证 SliderConfig Min/MaxLabel：滑条两端 + 上参数面板后 lane 上下界同源显示）。
        mNoteProperties.Add("tension", SliderConfig.Linear(0, -1, 1).WithMinLabel("Relaxed").WithMaxLabel("Tense"));
        // 整数滑条（验证吸附标度经属性 lane 全链路：侧栏滑条吸附 + 钉上参数面板后拖写同吸附整数格）。
        mNoteProperties.Add(("steps", "Steps (Int)"), SliderConfig.Integer(0, 0, 8));
        // per-phoneme 属性声明（验证音素属性链路：声明→侧栏面板→编辑→持久→快照读取）。按核相对角色两套：
        // 核（slot 0，元音）= accent + offset；引导 / 核后辅音 = 仅 offset——验证按 slot 差异化 schema 全链路。
        mCorePhonemeProperties.Add("accent", SliderConfig.Linear(0, 0, 1).WithMaxLabel("Strong"));
        // 无界数值框（验证 DraggableNumberBox/DraggableNumberBoxConfig）：横拖擦写、双击键入、无上下界、Shift 精调。
        var offset = DraggableNumberBoxConfig.Create(0).WithSensitivity(0.5).WithFormat(NumberFormat.Decimals(1));
        mCorePhonemeProperties.Add(("offset", "Offset (ms)"), offset);
        mConsonantPhonemeProperties.Add(("offset", "Offset (ms)"), offset);
        // 条件自动化轨开关（part 级）：勾选才暴露 Growl 轨——验证轨集合 = f(part 参数值)，
        // 取消勾选时 Growl 已画曲线由宿主保留隐藏、重新勾选即原样恢复。
        mPartProperties.Add(("growl_enabled", "Enable Growl"), CheckBoxConfig.Create(true));
        // 自定义自动化参数名避开宿主保留名（Volume/VibratoEnvelope 等内置项）。
        // Min/MaxLabel：验证 automation 端点描述文本（参数区上下界 + 侧栏默认值滑条两端，此前无 producer 覆盖）。
        mGrowlConfigs.Add(("Growl", "Growl"), AutomationConfig.Create(0, 100).WithColor("#E5A573").WithDefault(0)
            .WithMinLabel("Clean").WithMaxLabel("Growly"));
    }

    public void Destroy() { }

    public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context) => new TestSession(context);

    // 声明（引擎层、纯函数）：条件轨集合 = f(part 参数值)。声明先于会话求值，故会话构造期 Growl 轨已就绪、可订阅。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context)
    {
        // 连续轨 Growl（growl_enabled 勾选才暴露）+ 分段轨 Bend（恒在、DefaultValue=NaN）同在一张有序 map。
        var map = new OrderedMap<PropertyKey, AutomationConfig>();
        if (context.Parts.Select(p => p.PartProperties).Merge().GetBoolean("growl_enabled", true))
        {
            foreach (var kvp in mGrowlConfigs)
                map.Add(kvp.Key, kvp.Value);
        }
        map.Add(("Bend", "Bend"), mBendConfig);
        map.Add(("Semitone", "Semitone (Int)"), mSemitoneConfig);
        map.Add(("LogFreq", "LogFreq (Hz)"), mLogFreqConfig);
        map.Add(("Gate", "Gate (Band)"), mGateConfig);
        return map;
    }

    // 合成参数回显轨（只读）：恒声明一条 energy 回显轨（分段形、DefaultValue=NaN、自带色），合成前 key 即存在、可预声明。
    // 曲线数据经 IVoiceSynthesisSession.SynthesizedParameters 按同一 key（energy）承载；宿主作一等只读轨绘制。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => mReadbackConfigs;
    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => ObjectConfig.Create(mPartProperties);
    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => ObjectConfig.Create(mNoteProperties);
    // 音素属性声明（复用 note 上下文）：按核相对 slot 键控授 schema——核（slot 0）= accent + offset、
    // 辅音（slot ≠ 0）= 仅 offset，验证按角色差异化。slot 遍历用 SDK 共享口径 PhonemeSlots.UnionSlots；
    // schema 若依赖当前属性值，在此对同 slot 各成员值做三态 Merge（同 GetNotePropertyConfig 契约）再条件化。
    public IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context)
    {
        var core = ObjectConfig.Create(mCorePhonemeProperties);
        var consonant = ObjectConfig.Create(mConsonantPhonemeProperties);
        var map = new Map<int, ObjectConfig>();
        foreach (int slot in context.Notes.UnionSlots())
            map.Add(slot, slot == 0 ? core : consonant);
        return map;
    }

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
    readonly OrderedMap<PropertyKey, AutomationConfig> mGrowlConfigs = new();
    readonly OrderedMap<PropertyKey, IControllerConfig> mPartProperties = new();
    readonly OrderedMap<PropertyKey, IControllerConfig> mNoteProperties = new();
    readonly OrderedMap<PropertyKey, IControllerConfig> mCorePhonemeProperties = new();
    readonly OrderedMap<PropertyKey, IControllerConfig> mConsonantPhonemeProperties = new();
    // 分段轨（DefaultValue = NaN 表无基线）：验证声明/数据/路由/渲染/编辑/存盘链路；本参照实现的合成暂不消费它。
    static readonly AutomationConfig mBendConfig = AutomationConfig.Create(-100, 100).WithColor("#73C2E5");
    // 整数吸附标度连续轨（Create(INormalizedScale) 重载）：验证参数区绘制/拖锚点/双击键入锚点值全走标度吸附。
    static readonly AutomationConfig mSemitoneConfig = AutomationConfig.Create(NormalizedScale.Integer(-12, 12))
        .WithDefault(0).WithColor("#A5E573").WithFormat(NumberFormat.Decimals(0));
    // 对数轴（自定义 Custom 标度）连续轨：验证非线性标度全链路。频率 20..20000 Hz 几何插值——
    // value = 20·1000ⁿ（n∈[0,1]），故 Y 等距 = 频率等比、拖到中点约 632Hz。连续标度：投影退化为纯范围钳位。
    static readonly AutomationConfig mLogFreqConfig = AutomationConfig.Create(
            NormalizedScale.Custom(
                n => 20.0 * Math.Pow(1000.0, n),
                v => Math.Log(v / 20.0) / Math.Log(1000.0)))
        .WithDefault(440).WithColor("#73E5C2")
        .WithFormat(NumberFormat.Custom(
            v => v.ToString("0") + " Hz",
            text => double.TryParse(text.Replace("Hz", "").Trim(), out var r) ? r : (double?)null));
    // 二值区间轨（band）：分段（Create 默认 NaN）+ 退化量程 min==max ⇒ 宿主渲染为满高开关色带。
    // presence（非 NaN）= 开、gap = 关；值轴无意义、不参与。插件消费侧一句 !double.IsNaN(v) 即开关判定。
    static readonly AutomationConfig mGateConfig = AutomationConfig.Create(0, 0).WithColor("#E58AC9");
    // 回显轨声明（恒在、只读）：分段形（DefaultValue = NaN），曲线数据经 SynthesizedParameters 的 "energy" key 承载。
    static readonly OrderedMap<PropertyKey, AutomationConfig> mReadbackConfigs = new()
    {
        { ("energy", "Energy"), AutomationConfig.Create(0, 100).WithColor("#E573B0") },
    };
}

public sealed class TestSession : IVoiceSynthesisSession
{
    public TestSession(IVoiceSynthesisContext context)
    {
        mContext = context;

        // 变更接线（数据线程，handler 只做廉价标脏；重活延迟到 Committed 重分块）：
        // note 字段变化 → 标脏所在块 + 待重分块；增删 → 待重分块；
        // 曲线区间变化 → 标脏相交块；时基/part 属性 → 全部标脏。
        // 分级失效（链：时长/歌词[几何] → 音素 → 音高 → 参数 → 音频）：不同事件清不同下游产物，按级各开一个 WhenAnyItem。
        context.Notes.WhenAnyItem(n => n.StartTime.Modified, n => n.EndTime.Modified, n => n.Lyric.Modified)
            .Subscribe(note => MarkNoteDirty(note, phonemesStale: true, parametersStale: true), mSubscriptions);
        context.Notes.WhenAnyItem(n => n.Pitch.Modified, n => n.LeadingPhonemes.Modified, n => n.BodyPhonemes.Modified, n => n.BodyOffset.Modified)
            .Subscribe(note => MarkNoteDirty(note, phonemesStale: false, parametersStale: true), mSubscriptions);
        context.Notes.WhenAnyItem(n => n.Properties.Modified)
            .Subscribe(note => MarkNoteDirty(note, phonemesStale: false, parametersStale: false), mSubscriptions);
        context.Notes.ItemAdded.Subscribe(OnNotesStructureChanged, mSubscriptions);
        context.Notes.ItemRemoved.Subscribe(OnNotesStructureChanged, mSubscriptions);
        context.PartProperties.Modified.Subscribe(MarkAllDirtyAndResegment, mSubscriptions);
        context.Committed.Subscribe(OnCommitted);
        context.Pitch.RangeModified.Subscribe(OnPitchRangeModified);
        context.PitchDeviation.RangeModified.Subscribe(OnPitchRangeModified);
        // 构造期即订阅自己声明的 Growl 轨：宿主在建会话之前已按引擎声明填好 AutomationConfigs，
        // 故此处 context.Automations 已含自己声明的轨——参数绘制后的区间失效经此回调送达、触发重渲。
        if (context.Automations.TryGetValue("Growl", out var growl))
            growl.RangeModified.Subscribe(OnAutomationRangeModified);

        mNeedResegment = true;
    }

    public string DefaultLyric => "la";

    // 延音判定（本引擎语义 = 编辑器 "-" 约定，参考实现）：歌词 "-" ∧ 经不断裂相接链回溯到内容 note
    // （相接 = 前末 ≥ 后起，严格比较——边界同源 tick 换算，相接即精确相等）∧ 本 note 无钉死音素
    // （钉死即内容、退出乘客并成为合法链头；孤儿 = false）。**与合成行为成对**：Render 的快照域
    // IsCont 是本判定在快照上的等价复刻（按间隙分块 ⇒ 块内自判与 live 等价），两处语义必须一致——
    // 判定为延续的 note 不产音素、其时段由链头元音铺过（绑定性）。
    public bool IsContinuation(IVoiceSynthesisNote note)
    {
        if (note.Lyric.Value != "-" || note.LeadingPhonemes.Value.Count > 0 || note.BodyPhonemes.Value.Count > 0)
            return false;
        var cur = note;
        while (true)
        {
            var prev = cur.Previous;
            if (prev == null)
                return false;                              // 链跑出开头、无内容 note → 孤儿
            if (prev.EndTime.Value < cur.StartTime.Value)
                return false;                              // 空隙断链 → 孤儿
            if (prev.Lyric.Value != "-" || prev.LeadingPhonemes.Value.Count > 0 || prev.BodyPhonemes.Value.Count > 0)
                return true;                               // 回溯到链头 → 生效延续
            cur = prev;
        }
    }

    // —— 调度：窗内第一个脏块的纯值边界（peek 廉价）——
    public SynthesisRange? GetNextPendingSynthesisRange(double startTime, double endTime)
    {
        return FindNextDirtyPiece(startTime, endTime) is { } piece
            ? new SynthesisRange(piece.StartTime, piece.EndTime)
            : null;
    }

    // peek 与 commit 共用同一查找（确定性 + 同调度 tick 无编辑 ⇒ commit 重算得到 peek 报出的同一块）。
    Piece? FindNextDirtyPiece(double startTime, double endTime)
    {
        if (mNeedResegment)
            Resegment();

        foreach (var piece in mPieces)
        {
            if (!piece.Dirty || piece.Failed || piece.Synthesizing)
                continue;

            if (piece.EndTime < startTime || piece.StartTime > endTime)
                continue;

            return piece;
        }
        return null;
    }

    public async Task SynthesizeNext(double startTime, double endTime,
        CancellationToken cancellation = default)
    {
        if (FindNextDirtyPiece(startTime, endTime) is not { } piece)
            return;

        // 同步前缀（数据线程）拉取快照：notes 即本块全集；automation/pitch 宿主全量冻结（不开窗）。
        var snapshot = mContext.GetSnapshot(piece.Notes);

        piece.Dirty = false; // 合成期间到达的新变更会重新标脏，完成后自然重排
        piece.Synthesizing = true;
        piece.Progress = 0;
        NotifyProducts();   // 该块转入合成中 → 其产物不再报告（留白），产物随之变化

        // Progress<T> 捕获数据线程上下文：worker 的进度上报 marshal 回来落账再转发宿主。
        var report = new Progress<double>(p =>
        {
            piece.Progress = p;
            mStatusChanged.Invoke();
        });

        try
        {
            var rendered = await Task.Run(() => Render(snapshot, report, cancellation), CancellationToken.None);
            if (rendered != null && mPieces.Contains(piece))
            {
                // 段握柄：每次完成丢旧建新（一握柄 = 一次渲染）；写入整段后 Commit 把冻结音频交宿主驱动 effect。
                piece.Segment?.Dispose();
                piece.Segment = mContext.CreateAudioSegment((long)(rendered.StartTime * kSampleRate), rendered.Audio.Length, kSampleRate);
                piece.Segment.Write(0, rendered.Audio);
                piece.Segment.Commit();
                piece.Phonemes = rendered.Phonemes;
                piece.EnergyReadback = rendered.EnergyReadback;
                piece.PhonemesStale = false;     // 新产物落地：各级回显恢复有效
                piece.ParametersStale = false;
            }
        }
        catch (Exception ex)
        {
            piece.Failed = true;
            piece.Error = ex.Message;
        }
        finally
        {
            piece.Synthesizing = false;
            NotifyProducts();   // 块完成（产物写入）/ 失败（退出合成中）→ 产物变化
        }
    }

    public SynthesizedPitch SynthesizedPitch => new() { Segments = [] };

    // 参数回显：各已合成块的 energy 回显段聚成 map（轨 id → 分段曲线），key 与 GetSynthesizedParameterConfigs 对齐。
    // 回显轨显隐由宿主标题栏管控；此处恒返回数据，隐藏时宿主不绘制——无害。
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters
    {
        get
        {
            var segments = new List<IReadOnlyList<Point>>();
            foreach (var piece in mPieces)
            {
                if (piece.ParametersStale || piece.Failed || piece.Segment == null)
                    continue;   // 参数回显失效（上游：音素 / 音高级及以上变）→ 留白，待重渲；改音高即清此处、不清音素
                if (piece.EnergyReadback.Count > 0)
                    segments.Add(piece.EnergyReadback);
            }

            var map = new Map<string, SynthesizedParameter>();
            if (segments.Count > 0)
                map.Add("energy", new SynthesizedParameter { Segments = segments });
            return map;
        }
    }

    // 合成音素：仅在「音素几何未失效」（PhonemesStale=false）时报告。分级失效——音素几何只被**上游**（时长 / 歌词 /
    // 结构）变动清掉；同级（锁定音素）/ 下游（音高 / 参数 / 音频）变动**不**清音素，故锁定音素、改音高、画曲线时音素照常
    // 显示、不留白。不看 Dirty / Synthesizing：音素未失效时即便该块正重渲音频，旧音素仍有效、持续显示。
    public IReadOnlyMap<string, SynthesizedSyllable> SynthesizedPhonemes
    {
        get
        {
            var result = new Map<string, SynthesizedSyllable>();
            foreach (var piece in mPieces)
            {
                if (piece.PhonemesStale || piece.Failed || piece.Segment == null)
                    continue;
                foreach (var kvp in piece.Phonemes)   // 块间 note 不相交，直接并入
                    result.Add(kvp.Key, kvp.Value);
            }
            return result;
        }
    }

    public IReadOnlyList<SynthesisStatusSegment> Status => BuildStatus();

    IReadOnlyList<SynthesisStatusSegment> BuildStatus()
    {
        var result = new List<SynthesisStatusSegment>(mPieces.Count);
        foreach (var piece in mPieces)
        {
            var status = piece.Failed ? SynthesisSegmentStatus.Failed
                : piece.Synthesizing ? SynthesisSegmentStatus.Synthesizing
                : piece.Dirty || piece.Segment == null ? SynthesisSegmentStatus.Pending
                : SynthesisSegmentStatus.Synthesized;
            result.Add(new SynthesisStatusSegment
            {
                StartTime = piece.StartTime,
                EndTime = piece.EndTime,
                Status = status,
                Message = piece.Failed ? piece.Error : piece.Synthesizing ? "rendering" : null,
                Progress = piece.Synthesizing ? piece.Progress : 0,
            });
        }
        return result;
    }

    public IActionEvent SynthesizedPhonemesChanged => mSynthesizedPhonemesChanged;
    public IActionEvent SynthesizedParametersChanged => mSynthesizedParametersChanged;
    public IActionEvent SynthesizedPitchChanged => mSynthesizedPitchChanged;
    public IActionEvent StatusChanged => mStatusChanged;
    readonly ActionEvent mSynthesizedPhonemesChanged = new();
    readonly ActionEvent mSynthesizedParametersChanged = new();
    readonly ActionEvent mSynthesizedPitchChanged = new();
    readonly ActionEvent mStatusChanged = new();

    // 产物（音素 / 参数 / 音高）一并变化时统一通知 + 状态：本参照实现三者同源——块完成 / 标脏 / 重分块时一起变，
    // 故一起 fire。状态/进度（进度 tick）只 fire StatusChanged，不带动产物重读（这是信号分离的收益所在）。
    void NotifyProducts()
    {
        mSynthesizedPhonemesChanged.Invoke();
        mSynthesizedParametersChanged.Invoke();
        mSynthesizedPitchChanged.Invoke();
        mStatusChanged.Invoke();
    }

    public void Dispose()
    {
        mSubscriptions.DisposeAll();
        mContext.Committed.Unsubscribe(OnCommitted);
        mContext.Pitch.RangeModified.Unsubscribe(OnPitchRangeModified);
        mContext.PitchDeviation.RangeModified.Unsubscribe(OnPitchRangeModified);
        foreach (var piece in mPieces)
            piece.Segment?.Dispose();
        mPieces.Clear();
    }

    // —— 合成（worker 线程，只读冻结快照；产物归属经 snapshot.Notes[i].Id 键，零活引用）——
    sealed record RenderResult(float[] Audio, double StartTime, IReadOnlyMap<string, SynthesizedSyllable> Phonemes, List<Point> EnergyReadback);

    static RenderResult? Render(VoiceSynthesisSnapshot snapshot,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        var notes = snapshot.Notes;
        if (notes.Count == 0)
        {
            progress?.Report(1);
            return new RenderResult([], 0, new Map<string, SynthesizedSyllable>(), []);
        }

        // 模拟合成耗时：分步等待并上报进度（取消即中途退出，产物保持上一版）。期间宿主显示该块「合成中」、
        // 音素带留白（合成音素已被清除），结束后下方回填才重新显示——便于肉眼对比合成前后音素是否一致。
        if (kSimulatedRenderMs > 0)
        {
            const int steps = 20;
            for (int sIdx = 0; sIdx < steps; sIdx++)
            {
                if (cancellation.WaitHandle.WaitOne(kSimulatedRenderMs / steps))
                    return null;
                progress?.Report((double)(sIdx + 1) / steps);
            }
        }

        // 测试用：歌词为 "fail" 的 note 必定让本块合成失败并带报错文案——用于验证失败段红条 + hover 报错 + 右键复制。
        // 放在模拟耗时之后抛，可肉眼看到该段 橙(合成中)→红(失败) 的过渡。仅本块受影响，其他块照常合成。
        if (notes.Any(n => string.Equals(n.Lyric, "fail", StringComparison.OrdinalIgnoreCase)))
            throw new Exception("Synthesis failed: forced test failure triggered by lyric \"fail\".");

        // note 可重叠（和弦）：起点恒为首 note（已按 StartTime 升序），但结束须取全体最大——
        // 同起点和弦的数据层序是 EndPos 降，notes[^1] 反而结束最早，不能当块尾。
        double startTime = notes[0].StartTime;
        double endTime = notes.Max(n => n.EndTime);
        int sampleCount = Math.Max(1, (int)((endTime - startTime) * kSampleRate));
        var audio = new float[sampleCount];

        for (int n = 0; n < notes.Count; n++)
        {
            if (cancellation.IsCancellationRequested)
                return null; // 取消是正常调度结局：不抛异常，产物保持上一版

            var note = notes[n];
            double noteStart = note.StartTime;
            double noteEnd = note.EndTime;   // 有效末（宿主已去重叠、单声部音频口径）
            int from = Math.Clamp((int)((noteStart - startTime) * kSampleRate), 0, sampleCount);
            int to = Math.Clamp((int)((noteEnd - startTime) * kSampleRate), 0, sampleCount);

            // 双通道音高消费（参照实现）：finalPitch = resolve(Pitch) + PitchDeviation——
            // Pitch 钉死区用用户曲线、NaN 自由区回退 note 音高，再叠加偏差（vibrato 由此在
            // 未绘制区域同样生效）。控制率 100Hz 采样后逐 sample 线性插值、相位积分调频。
            int controlCount = Math.Max(2, (int)((noteEnd - noteStart) * kControlRate) + 1);
            var controlTimes = new double[controlCount];
            for (int c = 0; c < controlCount; c++)
            {
                controlTimes[c] = noteStart + (noteEnd - noteStart) * c / (controlCount - 1);
            }
            var pitchValues = snapshot.Pitch.Evaluator.Evaluate(controlTimes);
            var deviation = snapshot.PitchDeviation.Evaluator.Evaluate(controlTimes);
            for (int c = 0; c < controlCount; c++)
            {
                pitchValues[c] = (double.IsNaN(pitchValues[c]) ? note.Pitch : pitchValues[c]) + deviation[c];
            }

            // attack/release 线性包络：note 边界处波形从 0 渐入/渐出，消除截断造成的爆音（"啪"声）。
            // 渐入/渐出各取设定时长与半个 note 长度的较小者，短音符也不会重叠（attack==0 时不进渐变分支，无除零）。
            int length = to - from;
            int attack = Math.Min(kAttackSamples, length / 2);
            int release = Math.Min(kReleaseSamples, length / 2);
            double phase = 0;
            for (int i = from; i < to; i++)
            {
                int pos = i - from;
                double envelope = pos < attack ? (double)pos / attack
                    : pos >= length - release ? (double)(length - pos) / release
                    : 1.0;
                double t = (double)pos / Math.Max(1, length - 1) * (controlCount - 1);
                int c0 = Math.Min((int)t, controlCount - 2);
                double pitch = pitchValues[c0] + (pitchValues[c0 + 1] - pitchValues[c0]) * (t - c0);
                double freq = 440.0 * Math.Pow(2, (pitch - 69) / 12.0);
                phase += 2 * Math.PI * freq / kSampleRate;
                audio[i] += (float)(0.2 * envelope * Math.Sin(phase)); // 混音叠加：重叠 note（和弦）各自发声而非互相覆盖
            }

            progress?.Report((double)(n + 1) / notes.Count);
        }

        // —— 音素产出：歌词解析预测（自然放置，无压缩）→ 按出身归属 ——
        // 本引擎只声明每个 note 的音素 + 标称时长 + 权重 + IsLead，按归属 note 键成 map 回报；定位 / 去重叠 / 后盖前 /
        // 跨 note 辅音簇压缩全交宿主显示侧独占布局（合成音频也不消费音素位置，故本引擎无需自行解析）。需要把标称时长
        // 解析成真实时序来驱动音频帧的引擎，调 SDK 的 PhonemeLayout.Resolve 即可与宿主显示一致；不调就自由放置、错位非致命。
        //
        // 歌词解析（便于组合测试不同音素形态）：
        // · 延续 note 不产音素（绑定性：判定为延续的 note 不得回传音素，区段发音全部挂链头）；
        //   空 / 纯辅音无元音 → 单个 "" 覆盖整组。
        // · 否则按「前置辅音簇(w=0) + 元音簇 + 后辅音簇(w=0)」解析（元音字母 = a/e/i/o/u）：
        //     前置辅音各 kLeadIn 从核起点往左累积（IsLead=true）；**元音簇逐字母拆成多个 w=1 弹性音素**（测多元音）；后辅音各 kTrailDur。
        //     ka→k/a；skat→s,k/a/t（多前置辅音）；kai→k/a/i（**多元音**）；kait→k/a/i/t；kalt→k/a/l,t（多后辅音）。
        // · **歌词前缀 `*` = 跨拍测试**：把该 note 的 Preutterance 设成「首音素时长的一半」，令音符头切进首音素中点
        //   （分界不对齐音符头、首音素跨拍：前半落拍前域 / 后半落拍后域）。如 `*ka`→k 跨拍；`*ai`→a 跨拍。无 `*` 时核起点恒在音符头。
        const double kLeadIn = 0.1;    // 前辅音时长（取较大值便于肉眼观察）
        const double kTrailDur = 0.1;  // 后辅音时长（固定）
        const double kVowelDur = 0.2;  // 每个元音字母的标称时长（同权重→按此比例分核空间；相等则均分）
        bool IsVowelCh(char c) => "aeiou".IndexOf(char.ToLowerInvariant(c)) >= 0;

        // 快照域延音判定（与本会话对宿主的判定同语义——即 SDK 接口默认体："-" ∧ 相接链回溯到内容 note
        // ∧ 本 note 无钉死音素）。判定契约的作用域是 live 数据；本引擎按 note 间隙分块、链不跨块，
        // 故块内快照自判与 live 判定等价（块首即 "-" 时链头必不在别的块 → 孤儿）。
        bool IsCont(int index)
        {
            // 有无钉死音素 = 双列表任一非空（SDK 不供扁平 Phonemes，消费方从结构化列表自数）。
            static bool HasPinned(VoiceSynthesisNoteSnapshot n) => n.LeadingPhonemes.Count > 0 || n.BodyPhonemes.Count > 0;
            var it = notes[index];
            if (it.Lyric != "-" || HasPinned(it))
                return false;
            for (int i = index; i > 0; i--)
            {
                if (notes[i - 1].EndTime < notes[i].StartTime)
                    return false;                          // 空隙断链 → 孤儿（严格比较：边界同源 tick 换算，相接即精确相等）
                if (notes[i - 1].Lyric != "-" || HasPinned(notes[i - 1]))
                    return true;                           // 回溯到链头 → 生效延续
            }
            return false;                                  // 触底无链头 → 孤儿
        }

        var predicted = new List<RefLayout.Pred>();
        var notePreutter = new Dictionary<int, double>();   // 每 free note 的前置量（= 前置辅音时长和；`*` 跨拍时落进音素内部）
        for (int n = 0; n < notes.Count; n++)
        {
            var note = notes[n];
            if (IsCont(n))                  // 绑定性：判定为延续的 note 不回传音素
                continue;

            // 音素几何用 note 有效末 EndTime：元音自然铺到组末，后盖前 / 跨 note 压缩交宿主独占布局（RefLayout 仅报时长）。
            double groupEnd = note.EndTime;
            for (int m = n + 1; m < notes.Count && IsCont(m); m++)
                groupEnd = notes[m].EndTime;

            string lyric = note.Lyric ?? "";
            bool straddle = lyric.StartsWith("*");   // 前缀 `*` = 跨拍测试（剥掉后再解析歌词）
            if (straddle) lyric = lyric.Substring(1);

            // 切分：前置辅音簇 [0,vi) | 元音簇 [vi,vj) | 后辅音簇 [vj,len)
            int vi = 0; while (vi < lyric.Length && !IsVowelCh(lyric[vi])) vi++;
            int vj = lyric.Length; while (vj > vi && !IsVowelCh(lyric[vj - 1])) vj--;

            if (vi >= vj)   // 无元音（空 / 纯辅音 / 不认识）：单个 "" 覆盖整组
            {
                predicted.Add(new RefLayout.Pred { NoteIndex = n, Symbol = "", StretchWeight = 1, StartTime = note.StartTime, EndTime = groupEnd });
                notePreutter[n] = straddle ? 0.5 * Math.Max(0, groupEnd - note.StartTime) : 0;
                continue;
            }

            double vowelOnset = note.StartTime;                     // 核起点 = 音符头
            int onsetCount = vi, codaCount = lyric.Length - vj;
            for (int k = 0; k < onsetCount; k++)                    // 前置辅音：从核起点往左累积，归引导列表
            {
                double s = vowelOnset - (onsetCount - k) * kLeadIn;
                predicted.Add(new RefLayout.Pred { NoteIndex = n, Symbol = lyric[k].ToString(), StretchWeight = 0, StartTime = s, EndTime = s + kLeadIn, Leading = true });
            }
            double vowelEnd = codaCount > 0 ? Math.Max(vowelOnset, groupEnd - codaCount * kTrailDur) : groupEnd;
            for (int k = vi; k < vj; k++)                           // 元音簇逐字母 → 多个 w=1 弹性音素（测多元音）
            {
                double s = vowelOnset + (k - vi) * kVowelDur;
                predicted.Add(new RefLayout.Pred { NoteIndex = n, Symbol = lyric[k].ToString(), StretchWeight = 1, StartTime = s, EndTime = s + kVowelDur });
            }
            double t = vowelEnd;
            for (int k = vj; k < lyric.Length; k++)                 // 后辅音：核后依次，IsLead=false
            {
                predicted.Add(new RefLayout.Pred { NoteIndex = n, Symbol = lyric[k].ToString(), StretchWeight = 0, StartTime = t, EndTime = t + kTrailDur });
                t += kTrailDur;
            }
            // 前置量 = 前置辅音时长和；`*` 跨拍时改为「首音素时长的一半」令音符头切进首音素中点。
            notePreutter[n] = straddle ? 0.5 * (onsetCount > 0 ? kLeadIn : kVowelDur) : onsetCount * kLeadIn;
        }

        // 参考预测：钉死 note 报钉死时长、自由 note 报预测时长，按归属 note 键成 map（位置 / 压缩交宿主）。
        var phonemes = RefLayout.Build(snapshot, predicted, notePreutter);

        // 参数回显（energy）：本参照实现产出一条「引擎实际施加的 energy」分段曲线，与音频/音高同一秒时间系，
        // 供宿主作只读回显轨绘制。此处用一条确定性正弦波形（10..90，落在 energy 的 0..100 域内）驱动回显路径。
        var energy = new List<Point>();
        double duration = Math.Max(1e-6, endTime - startTime);
        int energyCount = Math.Max(2, (int)(duration * 20)); // 20Hz 采样
        for (int g = 0; g < energyCount; g++)
        {
            double t = startTime + duration * g / (energyCount - 1);
            double v = 50 + 40 * Math.Sin(2 * Math.PI * (t - startTime) / duration);
            energy.Add(new Point(t, v));
        }

        return new RenderResult(audio, startTime, phonemes, energy);
    }

    // —— 分块（数据线程；按 note 间隙分块，note 集等价的块保留缓存与状态）——
    void Resegment()
    {
        mNeedResegment = false;

        // 按 note 间隙分块；note 可重叠（和弦），故以"组内最大结束"判间隙，而非上一 note 的结束
        //（同起点和弦里上一 note 可能结束更早，用它会把仍在响的长音错误地切出去）。
        var groups = new List<List<IVoiceSynthesisNote>>();
        List<IVoiceSynthesisNote>? current = null;
        double groupMaxEnd = 0;
        foreach (var note in mContext.Notes)
        {
            if (current == null || note.StartTime.Value > groupMaxEnd)
            {
                current = new List<IVoiceSynthesisNote>();
                groups.Add(current);
                groupMaxEnd = note.EndTime.Value;
            }
            else
            {
                groupMaxEnd = Math.Max(groupMaxEnd, note.EndTime.Value);
            }
            current.Add(note);
        }

        var newPieces = new List<Piece>(groups.Count);
        foreach (var notes in groups)
        {
            double pieceEnd = notes.Max(n => n.EndTime.Value); // 块尾 = 组内最大结束（重叠安全）
            var existing = mPieces.FirstOrDefault(piece => piece.Notes.SequenceEqual(notes));
            if (existing != null)
            {
                mPieces.Remove(existing);
                existing.StartTime = notes[0].StartTime.Value;
                existing.EndTime = pieceEnd;
                newPieces.Add(existing);
            }
            else
            {
                newPieces.Add(new Piece
                {
                    Notes = notes,
                    StartTime = notes[0].StartTime.Value,
                    EndTime = pieceEnd,
                    Dirty = true,
                });
            }
        }

        // 未被复用的旧块（其 note 集已不存在）：释放段握柄，宿主丢对应 effect 缓存。
        foreach (var piece in mPieces)
            piece.Segment?.Dispose();
        mPieces.Clear();
        mPieces.AddRange(newPieces);
        NotifyProducts();   // 重分块：块集合 / 脏态变 → 产物报告随之变化
    }

    void OnNotesStructureChanged(IVoiceSynthesisNote note) => mNeedResegment = true;

    void MarkAllDirtyAndResegment()
    {
        foreach (var piece in mPieces)
        {
            piece.Dirty = true;
            piece.Failed = false;
        }
        mNeedResegment = true;
    }

    void OnCommitted()
    {
        if (mNeedResegment)
            Resegment();
    }

    // 音高曲线（含 PitchDeviation）= 音高级输入 → 清下游参数回显（不动音素；音高自身无独立回显产物）。
    void OnPitchRangeModified(double startTime, double endTime) => MarkRangeDirty(startTime, endTime, parametersStale: true);

    // 其他 automation 曲线（如 Growl）= 与参数同级的兄弟输入 → 仅重渲音频，不清任何回显。
    void OnAutomationRangeModified(double startTime, double endTime) => MarkRangeDirty(startTime, endTime, parametersStale: false);

    void MarkRangeDirty(double startTime, double endTime, bool parametersStale)
    {
        foreach (var piece in mPieces)
        {
            if (piece.EndTime < startTime || piece.StartTime > endTime)
                continue;

            piece.Dirty = true;
            piece.Failed = false;
            if (parametersStale)
                piece.ParametersStale = true;
        }
        NotifyProducts();
    }

    // 分级标脏：当级输入变 → 置其下游产物的 stale（音频恒重渲）。phonemesStale / parametersStale 控制对应回显在重渲期间是否留白。
    void MarkNoteDirty(IVoiceSynthesisNote note, bool phonemesStale, bool parametersStale)
    {
        foreach (var piece in mPieces)
        {
            if (!piece.Notes.Contains(note))
                continue;

            piece.Dirty = true;
            piece.Failed = false;
            if (phonemesStale)
                piece.PhonemesStale = true;
            if (parametersStale)
                piece.ParametersStale = true;
        }
        mNeedResegment = true;
    }

    sealed class Piece
    {
        public required IReadOnlyList<IVoiceSynthesisNote> Notes;
        public double StartTime;
        public double EndTime;
        public bool Dirty;            // 音频 / 产物需重渲（任何上游变动都置）
        public bool PhonemesStale;    // 音素几何失效（上游：时长 / 歌词 / 结构）——留白音素回显，待重派生
        public bool ParametersStale;  // 参数回显失效（上游：音素 / 音高级及以上）——留白参数回显
        public bool Failed;
        public bool Synthesizing;
        public string? Error;
        public double Progress;
        public IAudioSegment? Segment;
        public IReadOnlyMap<string, SynthesizedSyllable> Phonemes = new Map<string, SynthesizedSyllable>();
        public IReadOnlyList<Point> EnergyReadback = [];
    }

    // 模拟引擎合成耗时（真实引擎合成需时间）：瞬时回填看不出「合成前留白 → 合成后回显」两态。
    // 编辑 note（移动 / 缩放）后宿主已清除该 note 的合成音素 → 音素带先留白；此延时结束、Render 回填才重新显示。
    // 用于肉眼验证合成前后音素是否一致（上下文变化时长度 / 个数 / 种类可能不同）。设 0 即恢复瞬时合成。
    const int kSimulatedRenderMs = 1000;
    const int kSampleRate = 44100;
    const int kControlRate = 100;                            // 音高控制率（Hz）
    const int kAttackSamples = (int)(0.008 * kSampleRate);   // 8ms 渐入
    const int kReleaseSamples = (int)(0.012 * kSampleRate);  // 12ms 渐出

    readonly IVoiceSynthesisContext mContext;
    readonly DisposableManager mSubscriptions = new();
    readonly List<Piece> mPieces = new();
    bool mNeedResegment;
}

// 参考预测实现：把歌词解析出的每个音素的「标称时长 + 权重 + IsLead」按归属 note 键成 map 回报。
// 不再做定位 / 跨 note 去重叠 / melisma 铺设，也不带出身字段——契约只报时长 + 按 note 归属，位置 / 压缩 / 铺设全归宿主。
// 钉死 note 报其钉死时长，自由 note 报预测时长。核(w>0)的时长会被宿主忽略（恒按填充派生），报多少无所谓。
static class RefLayout
{
    // 自由 note 的预测音素（标称几何，时长 = EndTime − StartTime；位置仅预测内部用，不进契约）。
    public struct Pred
    {
        public int NoteIndex; public string Symbol; public double StretchWeight;
        public double StartTime; public double EndTime;
        public bool Leading;   // 归引导列表（核前前置辅音）；元音簇 / 后辅音 / 无元音兜底 = false（主体）
    }

    // 主入口：钉死 note 报钉死时长、自由 note 报预测时长，按归属 note 键成 map。位置 / 压缩 / 相接判定交宿主。
    public static IReadOnlyMap<string, SynthesizedSyllable> Build(VoiceSynthesisSnapshot snapshot, IReadOnlyList<Pred> predicted, IReadOnlyDictionary<int, double>? preutterOverride = null)
    {
        var byNote = new Dictionary<int, List<Pred>>();
        foreach (var p in predicted)
        {
            if (!byNote.TryGetValue(p.NoteIndex, out var list))
                byNote[p.NoteIndex] = list = new List<Pred>();
            list.Add(p);
        }

        static SynthesizedPhoneme Ph(string symbol, double dur, double w) => new() { Symbol = symbol, Duration = dur, StretchWeight = w };

        var result = new Map<string, SynthesizedSyllable>();
        for (int ni = 0; ni < snapshot.Notes.Count; ni++)
        {
            var note = snapshot.Notes[ni];
            var leading = new List<SynthesizedPhoneme>();
            var body = new List<SynthesizedPhoneme>();
            double bodyOffset;
            if (note.LeadingPhonemes.Count > 0 || note.BodyPhonemes.Count > 0)   // 钉死 note：报钉死双列表（位置不报），结合线取快照
            {
                foreach (var ph in note.LeadingPhonemes) leading.Add(Ph(ph.Symbol, ph.Duration, ph.StretchWeight));   // 几何字段（本回显不读 per-phoneme 属性）
                foreach (var ph in note.BodyPhonemes) body.Add(Ph(ph.Symbol, ph.Duration, ph.StretchWeight));
                bodyOffset = note.BodyOffset;
            }
            else if (byNote.TryGetValue(ni, out var preds))   // 自由 note：报预测时长；结合线由调用方按 note 给出「有效前置量」换算
            {
                double leadSum = 0;
                foreach (var p in preds)
                {
                    var ph = Ph(p.Symbol, p.EndTime - p.StartTime, p.StretchWeight);
                    if (p.Leading) { leading.Add(ph); leadSum += Math.Max(0, ph.Duration); }
                    else body.Add(ph);
                }
                // BodyOffset = Σ引导时长 − 有效前置量（preutterOverride 沿用旧标量语义：note 头到全序列首音素起点的距离）。
                double preutter = preutterOverride != null && preutterOverride.TryGetValue(ni, out var ov) ? ov : 0;
                bodyOffset = leadSum - preutter;
            }
            else
            {
                continue;
            }
            if (leading.Count > 0 || body.Count > 0)
                result.Add(snapshot.Notes[ni].Id, new SynthesizedSyllable(leading, body, bodyOffset));
        }
        return result;
    }
}
