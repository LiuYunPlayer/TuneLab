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
// 厚处理器：每个「effect 实例 × 一个上游段」一个 processor，构造拿 IEffectContext 自订阅、自管失效：
//   · 订阅 context.Input.Committed / Properties.Modified / 自动化 RangeModified 标脏，于 context.Committed 触发 ProcessingRequested；
//   · 跨 Process 调用缓存内部中间结果（env 取值、gain 标量、输出段句柄）、按脏只重算受影响内容；
//   · gain_env 的变更秒区间若不与本段相交 → 不标脏、不触发处理 → 本段输出不变、下游被跳过；
//   · 输出经 context.CreateAudioSegment 持久持有句柄，重处理时就地重写并重 Commit（无关变化时不重 Commit）。
[EffectEngine("TLTestGain")]
public sealed class GainEffectEngine : IEffectEngine
{
    public ObjectConfig GetPropertyConfig(IEffectPropertyContext context) => mConfig;

    // 条件轨集合：env_enabled 勾选才暴露 gain_env（轨集合 = f(当前参数值)）。取消勾选时已画 gain_env
    // 曲线由宿主保留隐藏、重新勾选即原样恢复。
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IEffectPropertyContext context)
    {
        // 连续轨 gain_env（env_enabled 勾选才暴露）+ 分段轨 formant（恒在、DefaultValue=NaN）同在一张有序 map。
        // formant 可编辑、随工程序列化；本参照实现的 DSP 暂不消费它（effect 分段轨回写为未来需求）。
        var map = new OrderedMap<string, AutomationConfig>();
        if (context.Properties.GetBool("env_enabled", true))
        {
            foreach (var kvp in mAutomations)
                map.Add(kvp.Key, kvp.Value);
        }
        map.Add("formant", mFormantConfig);
        return map;
    }
    public void Init() { }
    public void Destroy() { }
    public IEffectProcessor CreateProcessor(IEffectContext context) => new GainProcessor(context);

    static ObjectConfig BuildConfig()
    {
        var map = new OrderedMap<string, IControllerConfig>();
        map.Add("gain", new SliderConfig { DefaultValue = 1.0, MinValue = 0.0, MaxValue = 2.0 });
        map.Add("env_enabled", new CheckBoxConfig { DefaultValue = true, DisplayText = "Show Gain Env" });
        return new ObjectConfig { Properties = map };
    }

    static OrderedMap<string, AutomationConfig> BuildAutomations()
    {
        var map = new OrderedMap<string, AutomationConfig>();
        map.Add("gain_env", new AutomationConfig { DisplayText = "Gain Env", DefaultValue = 1.0, MinValue = 0.0, MaxValue = 2.0, Color = "#FF8800" });
        return map;
    }

    static readonly ObjectConfig mConfig = BuildConfig();
    static readonly OrderedMap<string, AutomationConfig> mAutomations = BuildAutomations();
    static readonly AutomationConfig mFormantConfig = new() { DisplayText = "Formant", DefaultValue = double.NaN, MinValue = -100, MaxValue = 100, Color = "#00C2A8" };

    // output = input * gain * gainEnv(t)。持久缓存 env/gain/输出段，按脏差分复用。
    sealed class GainProcessor : IEffectProcessor
    {
        public GainProcessor(IEffectContext context)
        {
            mContext = context;
            mContext.Input.Committed += OnInputCommitted;
            mContext.Properties.Modified += OnPropertiesModified;
            mContext.Committed += OnCommitted;
            WireEnvAutomation();
        }

        public event Action? ProcessingRequested;

        public Task Process(CancellationToken cancellation = default)
        {
            // 同步前缀（数据线程）：抓输入 PCM 引用 + 预采参数/自动化值。
            var input = mContext.Input;
            var src = input.Samples;
            int rate = input.SampleRate;
            long offset = input.SampleOffset;
            int count = src.Length;
            double segStart = rate > 0 ? (double)offset / rate : 0;

            bool initial = mOutput == null;
            bool audioChanged = initial || mAudioDirty || input.CommitVersion != mLastInputVersion;
            bool gainChanged = initial || mPropertiesDirty;
            bool envChanged = initial || audioChanged || mEnvDirty;

            mAudioDirty = false;
            mPropertiesDirty = false;
            mEnvDirty = false;
            mLastInputVersion = input.CommitVersion;

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

            var srcSpan = src.Span;
            var dst = new float[count];
            for (int i = 0; i < count; i++)
            {
                double m = mGain * (mEnv != null && i < mEnv.Length ? mEnv[i] : 1.0);
                dst[i] = (float)(srcSpan[i] * m);
            }

            CommitOutput(offset, count, rate, dst);
            mOutput = dst;
            return Task.CompletedTask;   // 错误经抛异常报告（宿主 catch → passthrough），此处不吞异常。
        }

        void OnInputCommitted() => mAudioDirty = true;

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

        void OnCommitted()
        {
            if (mAudioDirty || mPropertiesDirty || mEnvDirty)
                ProcessingRequested?.Invoke();
        }

        void WireEnvAutomation()
        {
            if (mEnvAutomation != null)
                mEnvAutomation.RangeModified -= OnEnvRangeModified;
            mEnvAutomation = mContext.TryGetAutomation("gain_env", out var automation) ? automation : null;
            if (mEnvAutomation != null)
                mEnvAutomation.RangeModified += OnEnvRangeModified;
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
            mContext.Input.Committed -= OnInputCommitted;
            mContext.Properties.Modified -= OnPropertiesModified;
            mContext.Committed -= OnCommitted;
            if (mEnvAutomation != null)
                mEnvAutomation.RangeModified -= OnEnvRangeModified;
            mOutSegment?.Dispose();
        }

        readonly IEffectContext mContext;
        ILiveAutomation? mEnvAutomation;
        IAudioSegment? mOutSegment;
        long mOutOffset;
        int mOutCount;
        int mOutRate;

        bool mAudioDirty;
        bool mPropertiesDirty;
        bool mEnvDirty;
        int mLastInputVersion;

        double[]? mEnv;          // 缓存的逐采样 gain_env 取值
        double mGain = 1.0;      // 缓存的 gain 标量
        float[]? mOutput;        // 缓存的输出 buffer（无关变化时不重 Commit）
    }
}

// 倒放效果器：无参数，反转样本顺序。与 Gain 同包，链中可观察顺序/增删效果。
// 仅被上游音频变化触发（无参数/自动化）；音频未变时不重 Commit（下游被跳过）。
[EffectEngine("TLTestReverse")]
public sealed class ReverseEffectEngine : IEffectEngine
{
    public ObjectConfig GetPropertyConfig(IEffectPropertyContext context) => mConfig;
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IEffectPropertyContext context) => mAutomations;
    public void Init() { }
    public void Destroy() { }
    public IEffectProcessor CreateProcessor(IEffectContext context) => new ReverseProcessor(context);

    static readonly ObjectConfig mConfig = new() { Properties = new OrderedMap<string, IControllerConfig>() };
    static readonly OrderedMap<string, AutomationConfig> mAutomations = new();

    sealed class ReverseProcessor : IEffectProcessor
    {
        public ReverseProcessor(IEffectContext context)
        {
            mContext = context;
            mContext.Input.Committed += OnInputCommitted;
            mContext.Committed += OnCommitted;
        }

        public event Action? ProcessingRequested;

        public Task Process(CancellationToken cancellation = default)
        {
            var input = mContext.Input;
            var src = input.Samples;
            int rate = input.SampleRate;
            long offset = input.SampleOffset;
            int count = src.Length;

            bool initial = mOutput == null;
            bool audioChanged = initial || mAudioDirty || input.CommitVersion != mLastInputVersion;
            mAudioDirty = false;
            mLastInputVersion = input.CommitVersion;

            if (!audioChanged && mOutput != null)
                return Task.CompletedTask;

            var srcSpan = src.Span;
            var dst = new float[count];
            for (int i = 0; i < count; i++)
                dst[i] = srcSpan[count - 1 - i];

            CommitOutput(offset, count, rate, dst);
            mOutput = dst;
            return Task.CompletedTask;
        }

        void OnInputCommitted() => mAudioDirty = true;

        void OnCommitted()
        {
            if (mAudioDirty)
                ProcessingRequested?.Invoke();
        }

        void CommitOutput(long offset, int count, int rate, float[] samples)
        {
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
            mContext.Input.Committed -= OnInputCommitted;
            mContext.Committed -= OnCommitted;
            mOutSegment?.Dispose();
        }

        readonly IEffectContext mContext;
        IAudioSegment? mOutSegment;
        long mOutOffset;
        int mOutCount;
        int mOutRate;

        bool mAudioDirty;
        int mLastInputVersion;
        float[]? mOutput;
    }
}
