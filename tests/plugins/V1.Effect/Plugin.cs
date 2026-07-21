using System;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Effect;

// 四个 effect 引擎打在同一个包/程序集里，覆盖不同的会话姿势（引擎经 manifest classes 注册、一包多 effect、
// 参数面板 / per-effect 自动化 / 链串行 / bypass / 增删重排）：
//   · Gain     —— 缓存型会话示范：订阅颗粒事件（可选信息源）维护差分缓存，按脏只重算受影响内容、
//                 无关变化早退不重 Commit（下游被跳过）；
//   · Reverse  —— 零订阅最简会话：被调到 Process 就按当前输入干活（电平语义）；
//   · Slow Gain—— 声称上报 + 段内局部重合成范式（账本收窄重算窗、耗时∝窗长、输出 Resize 保身份）；
//   · Fail Demo—— 恒失败（降级琥珀呈现）。
// 失效判定权归宿主：会话零上报义务，调度时机全在宿主（作用域信号 + 区间相交 + 批量收口）。
public sealed class GainEffectEngine : IEffectSynthesisEngine
{
    public ObjectConfig GetPropertyConfig(IEffectSynthesisPropertyContext context) => mConfig;

    // 条件轨集合：env_enabled 勾选才暴露 gain_env（轨集合 = f(当前参数值)）。取消勾选时已画 gain_env
    // 曲线由宿主保留隐藏、重新勾选即原样恢复。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IEffectSynthesisPropertyContext context)
    {
        // 连续轨 gain_env（env_enabled 勾选才暴露）+ 分段轨 formant（恒在、DefaultValue=NaN）同在一张有序 map。
        // formant 可编辑、随工程序列化；本参照实现的 DSP 暂不消费它（effect 分段轨回写为未来需求）。
        // 多选裁决示范：config 是整个选区的纯函数——全部选中实例都启用才显示 gain_env（保守合并）。
        var map = new OrderedMap<PropertyKey, AutomationConfig>();
        bool envEnabled = context.Effects.Count > 0;
        foreach (var view in context.Effects)
            envEnabled &= view.Properties.GetBoolean("env_enabled", true);
        if (envEnabled)
        {
            foreach (var kvp in mAutomations)
                map.Add(kvp.Key, kvp.Value);
        }
        map.Add(("formant", "Formant"), mFormantConfig);
        return map;
    }

    // 回显轨声明（只读、分段形 DefaultValue=NaN）：处理产出的 loudness 包络回显，曲线数据经
    // GainSession.SynthesizedParameters 的 "loudness" key 承载。验证 effect 回显端到端与 voice 同构。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context) => mReadbackConfigs;

    public void Init() { }
    public void Destroy() { }
    public IEffectSynthesisSession CreateSession(IEffectSynthesisContext context) => new GainSession(context);

    static ObjectConfig BuildConfig()
    {
        var map = new OrderedMap<PropertyKey, IControllerConfig>();
        map.Add("gain", SliderConfig.Linear(1.0, 0.0, 2.0));
        map.Add(("env_enabled", "Show Gain Env"), CheckBoxConfig.Create(true));
        return ObjectConfig.Create(map);
    }

    static OrderedMap<PropertyKey, AutomationConfig> BuildAutomations()
    {
        var map = new OrderedMap<PropertyKey, AutomationConfig>();
        map.Add(("gain_env", "Gain Env"), AutomationConfig.Create(0.0, 2.0).WithColor("#FF8800").WithDefault(1.0));
        return map;
    }

    static OrderedMap<PropertyKey, AutomationConfig> BuildReadbackConfigs()
    {
        var map = new OrderedMap<PropertyKey, AutomationConfig>();
        map.Add(("loudness", "Loudness"), AutomationConfig.Create(0.0, 2.0).WithColor("#00B0FF"));
        return map;
    }

    static readonly ObjectConfig mConfig = BuildConfig();
    static readonly OrderedMap<PropertyKey, AutomationConfig> mAutomations = BuildAutomations();
    static readonly OrderedMap<PropertyKey, AutomationConfig> mReadbackConfigs = BuildReadbackConfigs();
    static readonly AutomationConfig mFormantConfig = AutomationConfig.Create(-100, 100).WithColor("#00C2A8");

    // output = input * gain * gainEnv(t)。缓存型会话示范：订阅颗粒事件（可选信息源）维护差分缓存，
    // Process 里按脏只重算受影响内容；调度归宿主（被调到才干活、无关变化早退不重 Commit → 下游被跳过）。
    sealed class GainSession : IEffectSynthesisSession
    {
        public GainSession(IEffectSynthesisContext context)
        {
            mContext = context;
            mContext.Input.RangeModified.Subscribe(OnInputRangeModified);
            mContext.Properties.Modified.Subscribe(OnPropertiesModified);
            WireEnvAutomation();
        }

        // 同步瞬时处理，不自报状态（空声称 → 宿主按调度事实兜底呈现，验证兜底路径）。
        public IReadOnlyList<SynthesisStatusSegment> GetStatus() => EmptyStatus;
        public IActionEvent StatusChanged => mStatusChanged;
        readonly ActionEvent mStatusChanged = new();

        // 本段 loudness 回显（与输出同步重算）；输出无变化时沿用上轮。线程同输出：Process 在数据线程同步发布。
        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mReadback;
        public IActionEvent SynthesizedParametersChanged => mParametersChanged;
        readonly ActionEvent mParametersChanged = new();

        public Task Process(CancellationToken cancellation = default)
        {
            // 同步前缀（数据线程）：把输入 PCM 拷出到自有缓冲 + 预采参数/自动化值。
            int rate = mContext.Input.SampleRate;
            long offset = mContext.Input.SampleOffset;
            int count = mContext.Input.SampleCount;
            var src = new float[count];
            mContext.Input.Read(0, src);
            double segStart = rate > 0 ? (double)offset / rate : 0;

            bool initial = mOutput == null;
            bool geometryChanged = mOutSegment == null || mOutOffset != offset || mOutCount != count || mOutRate != rate;
            bool audioChanged = initial || mAudioDirty || geometryChanged;   // 纯几何提交（裁剪）账本静默，靠几何比对捕获
            bool gainChanged = initial || mPropertiesDirty;
            bool envChanged = initial || audioChanged || mEnvDirty;

            mAudioDirty = false;
            mPropertiesDirty = false;
            mEnvDirty = false;

            // 与本段无关的变化（如 gain_env 改在别的段时间区间）：输出不变 → 不重 Commit，下游据版本不变跳过。
            if (!audioChanged && !gainChanged && !envChanged && mOutput != null)
                return Task.CompletedTask;

            if (gainChanged)
                mGain = mContext.Properties.GetValue("gain", PropertyValue.Create(1.0)).ToDouble(out var g) ? g : 1.0;

            if (envChanged)
            {
                if (mEnvAutomation != null && count > 0)
                {
                    var times = new double[count];
                    for (int i = 0; i < count; i++)
                        times[i] = segStart + (double)i / rate;
                    mEnv = mEnvAutomation.Evaluate(times);
                }
                else
                {
                    mEnv = null;
                }
            }

            var dst = new float[count];
            for (int i = 0; i < count; i++)
            {
                double m = mGain * (mEnv != null && i < mEnv.Length ? mEnv[i] : 1.0);
                dst[i] = (float)(src[i] * m);
            }

            CommitOutput(offset, count, rate, dst);
            mOutput = dst;
            mReadback = BuildReadback(dst, rate, segStart);
            mParametersChanged.Invoke();   // 回显发布即触发（本引擎同步收尾发布，兜底重聚合也会覆盖；示范契约姿势）
            return Task.CompletedTask;   // 错误经抛异常报告（宿主 catch → passthrough），此处不吞异常。
        }

        // loudness 回显：对输出按 ~20ms 窗算 RMS，得一条 (全局秒, 值) 折线（单段）。空段 → 空 map。
        static IReadOnlyMap<string, SynthesizedParameter> BuildReadback(float[] samples, int rate, double segStart)
        {
            if (samples.Length == 0 || rate <= 0)
                return EmptyReadback;

            int window = Math.Max(1, rate / 50);
            var points = new List<Point>();
            for (int start = 0; start < samples.Length; start += window)
            {
                int end = Math.Min(start + window, samples.Length);
                double sum = 0;
                for (int i = start; i < end; i++)
                    sum += (double)samples[i] * samples[i];
                double rms = Math.Sqrt(sum / (end - start));
                double time = segStart + (start + (end - start) / 2.0) / rate;
                points.Add(new Point(time, rms));
            }

            var map = new Map<string, SynthesizedParameter>();
            map.Add("loudness", new SynthesizedParameter { Segments = new IReadOnlyList<Point>[] { points } });
            return map;
        }

        // 输入区间账本 (start=绝对采样位置, count)（本引擎无局部能力，只消费到布尔粒度；
        // 局部重合成范式见同包 Slow Gain：累积区间、按账本收窄重算窗、成功产出后清账）。
        void OnInputRangeModified(long start, int count) => mAudioDirty = true;

        void OnPropertiesModified()
        {
            // 参数变（含 env_enabled 切换显隐 gain_env 轨）→ 标参数 + env 脏，并重接 env 订阅。
            mPropertiesDirty = true;
            mEnvDirty = true;
            WireEnvAutomation();
        }

        void OnEnvRangeModified(double startTime, double endTime)
        {
            // 仅当变更秒区间与本段相交才标脏（否则本段无关、不触发处理 → 下游被跳过）。
            if (IntersectsSegment(startTime, endTime))
                mEnvDirty = true;
        }

        void WireEnvAutomation()
        {
            if (mEnvAutomation != null)
                mEnvAutomation.RangeModified.Unsubscribe(OnEnvRangeModified);
            mEnvAutomation = mContext.Automations.TryGetValue("gain_env", out var automation) ? automation : null;
            if (mEnvAutomation != null)
                mEnvAutomation.RangeModified.Subscribe(OnEnvRangeModified);
        }

        bool IntersectsSegment(double startTime, double endTime)
        {
            var input = mContext.Input;
            if (input.SampleRate <= 0 || input.SampleCount == 0)
                return true;
            double segStart = (double)input.SampleOffset / input.SampleRate;
            double segEnd = segStart + (double)input.SampleCount / input.SampleRate;
            return endTime >= segStart && startTime <= segEnd;
        }

        void CommitOutput(long offset, int count, int rate, float[] samples)
        {
            // 同几何：就地重写重 Commit。几何变：同率走 Resize（身份保持、下游节点与缓存存活）；换率才换段。
            if (mOutSegment == null || mOutRate != rate)
            {
                mOutSegment?.Dispose();
                mOutSegment = mContext.CreateAudioSegment(offset, count, rate);
            }
            else if (mOutCount != count || mOutOffset != offset)
            {
                mOutSegment.Resize(offset, count);
            }
            mOutOffset = offset;
            mOutCount = count;
            mOutRate = rate;
            mOutSegment.Write(0, samples);
            mOutSegment.Commit();
        }

        public void Dispose()
        {
            mContext.Input.RangeModified.Unsubscribe(OnInputRangeModified);
            mContext.Properties.Modified.Unsubscribe(OnPropertiesModified);
            if (mEnvAutomation != null)
                mEnvAutomation.RangeModified.Unsubscribe(OnEnvRangeModified);
            mOutSegment?.Dispose();
        }

        readonly IEffectSynthesisContext mContext;
        ISynthesisAutomation? mEnvAutomation;
        IAudioSegment? mOutSegment;
        long mOutOffset;
        int mOutCount;
        int mOutRate;

        bool mAudioDirty;
        bool mPropertiesDirty;
        bool mEnvDirty;

        double[]? mEnv;          // 缓存的逐采样 gain_env 取值
        double mGain = 1.0;      // 缓存的 gain 标量
        float[]? mOutput;        // 缓存的输出 buffer（无关变化时不重 Commit）
        IReadOnlyMap<string, SynthesizedParameter> mReadback = EmptyReadback;

        static readonly IReadOnlyMap<string, SynthesizedParameter> EmptyReadback = new Map<string, SynthesizedParameter>();
        static readonly SynthesisStatusSegment[] EmptyStatus = [];
    }
}

// 倒放效果器：无参数，反转样本顺序。与 Gain 同包，链中可观察顺序/增删效果。
// **最简会话示范**：零订阅、零状态——被调到 Process 就按当前输入干活（电平语义），
// 调度时机全权归宿主（宿主只在本段真变时调它）。
public sealed class ReverseEffectEngine : IEffectSynthesisEngine
{
    public ObjectConfig GetPropertyConfig(IEffectSynthesisPropertyContext context) => mConfig;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IEffectSynthesisPropertyContext context) => mAutomations;
    // 无回显（仅倒放音频）：返回空声明。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context) => mReadbackConfigs;
    public void Init() { }
    public void Destroy() { }
    public IEffectSynthesisSession CreateSession(IEffectSynthesisContext context) => new ReverseSession(context);

    static readonly ObjectConfig mConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
    static readonly OrderedMap<PropertyKey, AutomationConfig> mAutomations = new();
    static readonly OrderedMap<PropertyKey, AutomationConfig> mReadbackConfigs = new();

    sealed class ReverseSession(IEffectSynthesisContext context) : IEffectSynthesisSession
    {
        public IReadOnlyList<SynthesisStatusSegment> GetStatus() => EmptyStatus;
        public IActionEvent StatusChanged => mStatusChanged;
        readonly ActionEvent mStatusChanged = new();

        // 无回显：恒空 map、信号永不触发。
        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => EmptyReadback;
        public IActionEvent SynthesizedParametersChanged => mParametersChanged;
        readonly ActionEvent mParametersChanged = new();

        public Task Process(CancellationToken cancellation = default)
        {
            var input = context.Input;
            int rate = input.SampleRate;
            long offset = input.SampleOffset;
            int count = input.SampleCount;

            var src = new float[count];
            input.Read(0, src);
            var dst = new float[count];
            for (int i = 0; i < count; i++)
                dst[i] = src[count - 1 - i];

            CommitOutput(offset, count, rate, dst);
            return Task.CompletedTask;
        }

        void CommitOutput(long offset, int count, int rate, float[] samples)
        {
            // 同几何：就地重写重 Commit。几何变：同率走 Resize（身份保持）；换率才换段。
            if (mOutSegment == null || mOutRate != rate)
            {
                mOutSegment?.Dispose();
                mOutSegment = context.CreateAudioSegment(offset, count, rate);
            }
            else if (mOutCount != count || mOutOffset != offset)
            {
                mOutSegment.Resize(offset, count);
            }
            mOutOffset = offset;
            mOutCount = count;
            mOutRate = rate;
            mOutSegment.Write(0, samples);
            mOutSegment.Commit();
        }

        public void Dispose() => mOutSegment?.Dispose();

        IAudioSegment? mOutSegment;
        long mOutOffset;
        int mOutCount;
        int mOutRate;

        static readonly IReadOnlyMap<string, SynthesizedParameter> EmptyReadback = new Map<string, SynthesizedParameter>();
        static readonly SynthesisStatusSegment[] EmptyStatus = [];
    }
}

// 慢速增益（声称上报 + **段内局部重合成**双示范）：模拟长耗时离线模型（SVC 类）。
// 局部重合成范式（完整闭环）：订阅 Input.RangeModified 累积绝对轴脏区间账本 → Process 按账本收窄
// 重算窗（只 Read/重算/写回变更区，耗时 ∝ 窗长——编辑 inpainting 引擎的一个 note 时肉眼可感）→
// 输出几何变化走 Resize（身份保持，交集内容 = 仍有效的旧产物原样保留）→ 成功产出后清账（取消退账）。
// 声称只罩重算窗（Synthesizing + 进度，状态带只有窗口区变橙+水位）。固定 0.8 增益，无参数/自动化。
public sealed class SlowGainEffectEngine : IEffectSynthesisEngine
{
    public ObjectConfig GetPropertyConfig(IEffectSynthesisPropertyContext context) => mConfig;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IEffectSynthesisPropertyContext context) => mEmpty;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context) => mEmpty;
    public void Init() { }
    public void Destroy() { }
    public IEffectSynthesisSession CreateSession(IEffectSynthesisContext context) => new SlowGainSession(context);

    static readonly ObjectConfig mConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
    static readonly OrderedMap<PropertyKey, AutomationConfig> mEmpty = new();

    sealed class SlowGainSession : IEffectSynthesisSession
    {
        public SlowGainSession(IEffectSynthesisContext context)
        {
            mContext = context;
            mContext.Input.RangeModified.Subscribe(OnInputRangeModified);
        }

        // 声称时间线：处理中发布一条 Synthesizing 段（重算窗范围 + 进度）；完成/取消清空（事实层接管）。
        // 发布 = 换引用（不可变数组）、任意线程 Invoke StatusChanged——宿主 marshal 后拉取。
        public IReadOnlyList<SynthesisStatusSegment> GetStatus() => mStatus;
        public IActionEvent StatusChanged => mStatusChanged;
        readonly ActionEvent mStatusChanged = new();

        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => EmptyReadback;
        public IActionEvent SynthesizedParametersChanged => mParametersChanged;
        readonly ActionEvent mParametersChanged = new();

        public async Task Process(CancellationToken cancellation = default)
        {
            // —— 同步前缀（数据线程）——
            var input = mContext.Input;
            int rate = input.SampleRate;
            long offset = input.SampleOffset;
            int count = input.SampleCount;
            if (count <= 0 || rate <= 0)
                return;

            bool geometryChanged = mOutSegment == null || mOutOffset != offset || mOutCount != count || mOutRate != rate;

            // 重算窗：账本并集 → **外扩上下文余量**（新旧内容的对接处需要重算——「收到范围变更后扩张一点
            // 标脏范围，让新内容与旧内容对上」的参考姿势；本引擎点态，余量纯为示范，真实引擎按感受野取）
            // → ∩ 段界。裁剪掉的区域账目照收（绝对轴「从有到无」也是内容变更，且上游 Resize 对称差必入账）：
            // 求交后落在段外的部分自然消失，但其外扩壳留在界内——边界邻域被如实重算。
            int winFrom, winLength;
            if (mOutSegment != null && mOutRate == rate && mPending.Count > 0)
            {
                int margin = Math.Max(1, (int)(kContextMarginSeconds * rate));
                long boundStart = long.MaxValue, boundEnd = long.MinValue;
                foreach (var (start, length) in mPending)
                {
                    boundStart = Math.Min(boundStart, start);
                    boundEnd = Math.Max(boundEnd, start + length);
                }
                boundStart = Math.Max(boundStart - margin, offset);
                boundEnd = Math.Min(boundEnd + margin, offset + count);
                if (boundEnd <= boundStart)
                {
                    // 变更（含外扩壳）全在段界外：界内无内容可重算——几何跟随（Resize 自动把对称差
                    // 入账给自己的下游）+ 空提交；几何没变则纯 no-op。
                    mPending.Clear();
                    if (geometryChanged)
                    {
                        mOutSegment.Resize(offset, count);
                        mOutOffset = offset;
                        mOutCount = count;
                        mOutSegment.Commit();
                    }
                    return;
                }
                winFrom = (int)(boundStart - offset);
                winLength = (int)(boundEnd - boundStart);
            }
            else if (mOutSegment != null && mOutRate == rate && !geometryChanged)
            {
                return;   // 无账本、无几何变化：内容没变（如上游 no-op 重提交），早退不重 Commit
            }
            else
            {
                winFrom = 0;
                winLength = count;   // 首跑 / 换率（Resize 不改率，换率换段整算）
            }
            mPending.Clear();   // 账本消费（成功产出即清；取消/失败在结局处退账）

            var src = new float[winLength];
            input.Read(winFrom, src);
            double winStart = (offset + winFrom) / (double)rate;
            double winEnd = winStart + winLength / (double)rate;

            // —— offload：耗时 ∝ 重算窗占比（整段 ~3s；局部重算按比例缩短——账本收窄重算量的可感证据）——
            int totalMs = Math.Max(200, (int)(3000.0 * winLength / count));
            var dst = await Task.Run(() =>
            {
                int steps = Math.Max(3, totalMs / 100);
                var buffer = new float[winLength];
                for (int step = 0; step < steps; step++)
                {
                    if (cancellation.IsCancellationRequested)
                        return null;
                    Thread.Sleep(totalMs / steps);
                    int begin = (int)((long)winLength * step / steps);
                    int end = (int)((long)winLength * (step + 1) / steps);
                    for (int i = begin; i < end; i++)
                        buffer[i] = src[i] * 0.8f;
                    PublishStatus(winStart, winEnd, (step + 1) / (double)steps);
                }
                return buffer;
            });
            if (dst == null)
            {
                // 取消：退账（本窗未产出、不丢更新），下轮重排。
                mPending.Add((offset + winFrom, winLength));
                ClearStatus();
                return;
            }

            // —— await 续延回数据线程：产出。几何变化走 Resize（身份保持，交集旧产物仍有效原样保留），
            //    只写回重算窗并 Commit——下游账本随之只有本窗。——
            if (mOutSegment == null || mOutRate != rate)
            {
                mOutSegment?.Dispose();
                mOutSegment = mContext.CreateAudioSegment(offset, count, rate);
            }
            else if (geometryChanged)
            {
                mOutSegment.Resize(offset, count);
            }
            mOutOffset = offset;
            mOutCount = count;
            mOutRate = rate;
            mOutSegment.Write(winFrom, dst);
            mOutSegment.Commit();
            ClearStatus();
        }

        // 输入区间账本（绝对轴）：累积进待处理集；绝对坐标在上游 Resize 前后天然稳定，无需重定基。
        void OnInputRangeModified(long start, int count)
        {
            if (count <= 0)
                return;
            long end = start + count;
            for (int i = mPending.Count - 1; i >= 0; i--)
            {
                var (s, c) = mPending[i];
                long e = s + c;
                if (e < start || s > end)
                    continue;
                start = Math.Min(start, s);
                end = Math.Max(end, e);
                mPending.RemoveAt(i);
            }
            mPending.Add((start, (int)Math.Min(int.MaxValue, end - start)));
        }

        void PublishStatus(double startTime, double endTime, double progress)
        {
            mStatus = new[]
            {
                new SynthesisStatusSegment
                {
                    StartTime = startTime,
                    EndTime = endTime,
                    Status = SynthesisSegmentStatus.Synthesizing,
                    Progress = progress,
                },
            };
            mStatusChanged.Invoke();
        }

        void ClearStatus()
        {
            if (mStatus.Length == 0)
                return;
            mStatus = EmptyStatus;
            mStatusChanged.Invoke();
        }

        public void Dispose()
        {
            mContext.Input.RangeModified.Unsubscribe(OnInputRangeModified);
            mOutSegment?.Dispose();
        }

        const double kContextMarginSeconds = 0.1;   // 示范用上下文余量（真实上下文引擎按自己的感受野取）

        readonly IEffectSynthesisContext mContext;
        readonly List<(long Start, int Count)> mPending = new();   // 脏区间账本（绝对轴；成功产出清、取消退）
        IAudioSegment? mOutSegment;
        long mOutOffset;
        int mOutCount;
        int mOutRate;

        SynthesisStatusSegment[] mStatus = EmptyStatus;   // 发布 = 换引用（引用赋值原子，宿主跨线程读安全）

        static readonly IReadOnlyMap<string, SynthesizedParameter> EmptyReadback = new Map<string, SynthesizedParameter>();
        static readonly SynthesisStatusSegment[] EmptyStatus = [];
    }
}

// 恒失败（降级演示）：Process 恒抛异常 → 宿主 catch → 该段 passthrough——验证状态带琥珀 Degraded、
// pill 文案（含引擎异常消息、可复制）、以及失败级 passthrough 后下游/链尾仍可播。
public sealed class FailEffectEngine : IEffectSynthesisEngine
{
    public ObjectConfig GetPropertyConfig(IEffectSynthesisPropertyContext context) => mConfig;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IEffectSynthesisPropertyContext context) => mEmpty;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context) => mEmpty;
    public void Init() { }
    public void Destroy() { }
    public IEffectSynthesisSession CreateSession(IEffectSynthesisContext context) => new FailSession(context);

    static readonly ObjectConfig mConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
    static readonly OrderedMap<PropertyKey, AutomationConfig> mEmpty = new();

    sealed class FailSession(IEffectSynthesisContext context) : IEffectSynthesisSession
    {
        public IReadOnlyList<SynthesisStatusSegment> GetStatus() => EmptyStatus;
        public IActionEvent StatusChanged => mStatusChanged;
        readonly ActionEvent mStatusChanged = new();

        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => EmptyReadback;
        public IActionEvent SynthesizedParametersChanged => mParametersChanged;
        readonly ActionEvent mParametersChanged = new();

        public Task Process(CancellationToken cancellation = default)
        {
            _ = context;   // 恒失败演示：不消费输入
            throw new InvalidOperationException("Simulated failure for degraded-status testing.");
        }

        public void Dispose() { }

        static readonly IReadOnlyMap<string, SynthesizedParameter> EmptyReadback = new Map<string, SynthesizedParameter>();
        static readonly SynthesisStatusSegment[] EmptyStatus = [];
    }
}
