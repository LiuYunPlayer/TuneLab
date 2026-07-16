using System;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Effect;

// 两个 effect 引擎打在同一个包/程序集里，验证：引擎按 [EffectEngine] 注册、一包多 effect、
// 参数面板（Gain 有 gain 滑块）、per-effect 时间轴自动化（Gain 有 gain_env 自动化轨）、
// 链串行（Gain 与 Reverse 串联）、bypass、增删/重排。
//
// 厚处理器：每个「effect 实例 × 一个上游段」一个 processor，构造拿 IEffectSynthesisContext 自订阅、自管失效：
//   · 订阅 context.Input.Committed / Properties.Modified / 自动化 RangeModified 标脏，于 context.Committed 触发 ProcessingRequested；
//   · 跨 Process 调用缓存内部中间结果（env 取值、gain 标量、输出段句柄）、按脏只重算受影响内容；
//   · gain_env 的变更秒区间若不与本段相交 → 不标脏、不触发处理 → 本段输出不变、下游被跳过；
//   · 输出经 context.CreateAudioSegment 持久持有句柄，重处理时就地重写并重 Commit（无关变化时不重 Commit）。
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
            envEnabled &= view.Properties.GetBool("env_enabled", true);
        if (envEnabled)
        {
            foreach (var kvp in mAutomations)
                map.Add(kvp.Key, kvp.Value);
        }
        map.Add(("formant", "Formant"), mFormantConfig);
        return map;
    }

    // 回显轨声明（只读、分段形 DefaultValue=NaN）：处理产出的 loudness 包络回显，曲线数据经
    // GainProcessor.SynthesizedParameters 的 "loudness" key 承载。验证 effect 回显端到端与 voice 同构。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context) => mReadbackConfigs;

    public void Init() { }
    public void Destroy() { }
    public IEffectSynthesisProcessor CreateProcessor(IEffectSynthesisContext context) => new GainProcessor(context);

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

    // output = input * gain * gainEnv(t)。缓存型处理器示范：订阅颗粒事件（可选信息源）维护差分缓存，
    // Process 里按脏只重算受影响内容；调度归宿主（被调到才干活、无关变化早退不重 Commit → 下游被跳过）。
    sealed class GainProcessor : IEffectSynthesisProcessor
    {
        public GainProcessor(IEffectSynthesisContext context)
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
            bool audioChanged = initial || mAudioDirty;   // 失效判据 = Input.Committed 事件（SDK 无版本号）
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

        // 输入区间账本 (offset, count)（本引擎无局部能力，只消费到布尔粒度；局部重合成引擎在此累积区间、成功产出后清账）。
        void OnInputRangeModified(int offset, int count) => mAudioDirty = true;

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
            // 重用同位同长同率的输出段句柄（就地重写重 Commit）；几何变了则换段。
            if (mOutput != null && mOutSegment != null && mOutCount == count && mOutOffset == offset && mOutRate == rate)
            {
                mOutSegment.Write(0, samples);
                mOutSegment.Commit();
                return;
            }
            mOutSegment?.Dispose();
            mOutSegment = mContext.CreateAudioSegment(offset, count, rate);
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
// **最简处理器示范**：零订阅、零状态——被调到 Process 就按当前输入干活（电平语义），
// 调度时机全权归宿主（宿主只在本段真变时调它）。
public sealed class ReverseEffectEngine : IEffectSynthesisEngine
{
    public ObjectConfig GetPropertyConfig(IEffectSynthesisPropertyContext context) => mConfig;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IEffectSynthesisPropertyContext context) => mAutomations;
    // 无回显（仅倒放音频）：返回空声明。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context) => mReadbackConfigs;
    public void Init() { }
    public void Destroy() { }
    public IEffectSynthesisProcessor CreateProcessor(IEffectSynthesisContext context) => new ReverseProcessor(context);

    static readonly ObjectConfig mConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
    static readonly OrderedMap<PropertyKey, AutomationConfig> mAutomations = new();
    static readonly OrderedMap<PropertyKey, AutomationConfig> mReadbackConfigs = new();

    sealed class ReverseProcessor(IEffectSynthesisContext context) : IEffectSynthesisProcessor
    {
        public IReadOnlyList<SynthesisStatusSegment> GetStatus() => EmptyStatus;
        public IActionEvent StatusChanged => mStatusChanged;
        readonly ActionEvent mStatusChanged = new();

        // 无回显：恒空 map。
        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => EmptyReadback;

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
            // 重用同位同长同率的输出段句柄（就地重写重 Commit）；几何变了则换段。
            if (mOutSegment != null && mOutCount == count && mOutOffset == offset && mOutRate == rate)
            {
                mOutSegment.Write(0, samples);
                mOutSegment.Commit();
                return;
            }
            mOutSegment?.Dispose();
            mOutSegment = context.CreateAudioSegment(offset, count, rate);
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

// 慢速增益（声称上报演示）：模拟长耗时离线模型（SVC 类）——worker 分步处理 ~3s、逐步发布声称段
// （Synthesizing + 进度，状态带呈纵向水位），验证流光与取消（编辑打断后正常返回不写产物）。
// 固定 0.8 增益，无参数/自动化；调度归宿主，零失效订阅。
public sealed class SlowGainEffectEngine : IEffectSynthesisEngine
{
    public ObjectConfig GetPropertyConfig(IEffectSynthesisPropertyContext context) => mConfig;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IEffectSynthesisPropertyContext context) => mEmpty;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context) => mEmpty;
    public void Init() { }
    public void Destroy() { }
    public IEffectSynthesisProcessor CreateProcessor(IEffectSynthesisContext context) => new SlowGainProcessor(context);

    static readonly ObjectConfig mConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
    static readonly OrderedMap<PropertyKey, AutomationConfig> mEmpty = new();

    sealed class SlowGainProcessor(IEffectSynthesisContext context) : IEffectSynthesisProcessor
    {
        // 声称时间线：处理中发布一条 Synthesizing 段（本段范围 + 整体进度）；完成/取消清空（事实层接管）。
        // 发布 = 换引用（不可变数组）、任意线程 Invoke StatusChanged——宿主 marshal 后拉取。
        public IReadOnlyList<SynthesisStatusSegment> GetStatus() => mStatus;
        public IActionEvent StatusChanged => mStatusChanged;
        readonly ActionEvent mStatusChanged = new();

        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => EmptyReadback;

        public async Task Process(CancellationToken cancellation = default)
        {
            // 同步前缀（数据线程）：把输入 PCM 拷出到自有缓冲。
            var input = context.Input;
            int rate = input.SampleRate;
            long offset = input.SampleOffset;
            int count = input.SampleCount;

            var src = new float[count];
            input.Read(0, src);
            double segStart = rate > 0 ? (double)offset / rate : 0;
            double segEnd = rate > 0 ? segStart + (double)count / rate : segStart;

            // offload 到 worker 分步处理并报声称（Synthesizing + 进度）；取消 = 正常返回 null（不写产物）。
            var dst = await Task.Run(() =>
            {
                const int steps = 30;
                var buffer = new float[count];
                for (int step = 0; step < steps; step++)
                {
                    if (cancellation.IsCancellationRequested)
                        return null;
                    Thread.Sleep(100);
                    int begin = (int)((long)count * step / steps);
                    int end = (int)((long)count * (step + 1) / steps);
                    for (int i = begin; i < end; i++)
                        buffer[i] = src[i] * 0.8f;
                    PublishStatus(segStart, segEnd, (step + 1) / (double)steps);
                }
                return buffer;
            });
            if (dst == null)
            {
                ClearStatus();
                return;   // 取消是正常调度结局
            }

            // await 续延回数据线程：写出并 Commit，声称退场（宿主事实层接管呈现）。
            CommitOutput(offset, count, rate, dst);
            ClearStatus();
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

        void CommitOutput(long offset, int count, int rate, float[] samples)
        {
            if (mOutSegment != null && mOutCount == count && mOutOffset == offset && mOutRate == rate)
            {
                mOutSegment.Write(0, samples);
                mOutSegment.Commit();
                return;
            }
            mOutSegment?.Dispose();
            mOutSegment = context.CreateAudioSegment(offset, count, rate);
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
    public IEffectSynthesisProcessor CreateProcessor(IEffectSynthesisContext context) => new FailProcessor(context);

    static readonly ObjectConfig mConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
    static readonly OrderedMap<PropertyKey, AutomationConfig> mEmpty = new();

    sealed class FailProcessor(IEffectSynthesisContext context) : IEffectSynthesisProcessor
    {
        public IReadOnlyList<SynthesisStatusSegment> GetStatus() => EmptyStatus;
        public IActionEvent StatusChanged => mStatusChanged;
        readonly ActionEvent mStatusChanged = new();

        public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => EmptyReadback;

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
