using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Effect;

// 两个 effect 引擎打在同一个包/程序集里，验证：引擎按 [EffectEngine] 注册、一包多 effect、
// 参数面板（Gain 有 gain 滑块）、per-effect 时间轴自动化（Gain 有 gain_env 自动化轨）、
// 链串行（Gain 与 Reverse 串联）、bypass、增删/重排。
//
// 处理器为持久句柄，演示按段细粒度增量（IEffectChange）：
//   · 跨调用缓存内部中间结果（env 取值、gain 标量、输出 buffer）；
//   · 按 change 只重算受影响的内部内容——只改 gain 不重取 env 曲线，只改 gain_env 不重读 gain；
//   · gain_env 的变更秒区间若不与本段相交 → 本段输出不变，返回同一输出数组引用，宿主据此跳过本段下游。
[EffectEngine("TLTestGain")]
public sealed class GainEffectEngine : IEffectEngine
{
    public ObjectConfig GetPartPropertyConfig(IEffectPropertyContext context) => mConfig;

    // 条件轨集合：env_enabled 勾选才暴露 gain_env（轨集合 = f(当前参数值)）。取消勾选时已画 gain_env
    // 曲线由宿主保留隐藏、重新勾选即原样恢复。
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IEffectPropertyContext context)
        => context.Properties.GetBool("env_enabled", true) ? mAutomations : mEmptyAutomations;
    public void Init() { }
    public void Destroy() { }
    public IEffectProcessor CreateProcessor() => new GainProcessor();

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
    static readonly OrderedMap<string, AutomationConfig> mEmptyAutomations = new();

    // output = input * gain * gainEnv(t)。持久缓存 env/gain/输出，按 change 差分复用。
    sealed class GainProcessor : IEffectProcessor
    {
        public Task Process(IEffectInput input, IEffectOutput output, IEffectChange change,
                            IProgress<double>? progress = null, CancellationToken cancellation = default)
        {
            var audio = input.Audio;
            var src = audio.Samples ?? Array.Empty<float>();
            double segStart = audio.StartTime;
            double segEnd = audio.StartTime + (src.Length == 0 ? 0 : (double)src.Length / audio.SampleRate);

            bool audioChanged = change.IsInitial || !ReferenceEquals(src, mLastInput) || change.TryGetAudioChange(out _, out _);
            bool gainChanged = change.IsInitial || change.ChangedProperties.Contains("gain");

            // env 仅在：首次 / 音频变（逐采样时间轴随之变）/ env_enabled 显隐切换 / gain_env 变更区间与本段相交
            // 时重取曲线。env_enabled 切换会改 gain_env 轨的有无，须重取（关掉后 TryGetAutomation 失败 → env 清空）。
            bool envChanged;
            if (change.IsInitial || audioChanged || change.ChangedProperties.Contains("env_enabled"))
                envChanged = true;
            else if (change.TryGetAutomationChange("gain_env", out double autoStart, out double autoEnd))
                envChanged = autoEnd >= segStart && autoStart <= segEnd;
            else
                envChanged = false;

            // 与本段无关的变化（如 gain_env 改在别的段时间区间）：输出不变，返回缓存（同数组引用）→ 宿主跳过下游。
            if (!audioChanged && !gainChanged && !envChanged && mOutput != null)
            {
                output.Audio = new MonoAudio(mOutStartTime, mOutSampleRate, mOutput);
                progress?.Report(1);
                return Task.CompletedTask;
            }

            if (gainChanged)
                mGain = input.Properties.GetDouble("gain", 1.0);

            if (envChanged)
            {
                if (input.TryGetAutomation("gain_env", out var automation) && src.Length > 0)
                {
                    var times = new double[src.Length];
                    for (int i = 0; i < src.Length; i++)
                        times[i] = audio.StartTime + (double)i / audio.SampleRate;
                    mEnv = automation.Evaluate(times);
                }
                else
                {
                    mEnv = null;
                }
            }

            var dst = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                double m = mGain * (mEnv != null && i < mEnv.Length ? mEnv[i] : 1.0);
                dst[i] = (float)(src[i] * m);
            }
            mLastInput = src;
            mOutput = dst;
            mOutStartTime = audio.StartTime;
            mOutSampleRate = audio.SampleRate;
            output.Audio = new MonoAudio(audio.StartTime, audio.SampleRate, dst);
            progress?.Report(1);
            return Task.CompletedTask;   // 错误经抛异常报告（宿主 catch → passthrough），此处不吞异常。
        }

        public void Dispose() { }

        float[]? mLastInput;     // 上次输入样本引用（判音频是否换）
        double[]? mEnv;          // 缓存的逐采样 gain_env 取值
        double mGain = 1.0;      // 缓存的 gain 标量
        float[]? mOutput;        // 缓存的输出 buffer（无关变化时原样返回，供宿主跳过下游）
        double mOutStartTime;
        int mOutSampleRate;
    }
}

// 倒放效果器：无参数，反转样本顺序。与 Gain 同包，链中可观察顺序/增删效果。
// 仅被上游音频变化触发（无参数/自动化）；音频未变时返回缓存输出（同引用）。
[EffectEngine("TLTestReverse")]
public sealed class ReverseEffectEngine : IEffectEngine
{
    public ObjectConfig GetPartPropertyConfig(IEffectPropertyContext context) => mConfig;
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IEffectPropertyContext context) => mAutomations;
    public void Init() { }
    public void Destroy() { }
    public IEffectProcessor CreateProcessor() => new ReverseProcessor();

    static readonly ObjectConfig mConfig = new() { Properties = new OrderedMap<string, IControllerConfig>() };
    static readonly OrderedMap<string, AutomationConfig> mAutomations = new();

    sealed class ReverseProcessor : IEffectProcessor
    {
        public Task Process(IEffectInput input, IEffectOutput output, IEffectChange change,
                            IProgress<double>? progress = null, CancellationToken cancellation = default)
        {
            var audio = input.Audio;
            var src = audio.Samples ?? Array.Empty<float>();

            bool audioChanged = change.IsInitial || !ReferenceEquals(src, mLastInput) || change.TryGetAudioChange(out _, out _);
            if (!audioChanged && mOutput != null)
            {
                output.Audio = new MonoAudio(mOutStartTime, mOutSampleRate, mOutput);
                progress?.Report(1);
                return Task.CompletedTask;
            }

            var dst = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
                dst[i] = src[src.Length - 1 - i];
            mLastInput = src;
            mOutput = dst;
            mOutStartTime = audio.StartTime;
            mOutSampleRate = audio.SampleRate;
            output.Audio = new MonoAudio(audio.StartTime, audio.SampleRate, dst);
            progress?.Report(1);
            return Task.CompletedTask;
        }

        public void Dispose() { }

        float[]? mLastInput;
        float[]? mOutput;
        double mOutStartTime;
        int mOutSampleRate;
    }
}
