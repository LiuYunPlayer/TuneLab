using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Effect;

// 两个 effect 引擎打在同一个包/程序集里，验证：引擎按 [EffectEngine] 注册、一包多 effect、
// 参数面板（Gain 有 gain 滑块）、per-effect 时间轴自动化（Gain 有 gain_env 自动化轨）、
// 链串行（Gain 与 Reverse 串联）、bypass、增删/重排。
// 处理器为持久句柄：本测试每次整段重处理、忽略 change 的增量提示（演示「整段进整段出」的退化情形）。

// 增益效果器：output = input * gain * gainEnv(t)。gain 为静态参数滑块；gain_env 为一条可编辑的时间轴自动化轨
// （验证 per-effect 自动化：声明 AutomationConfig + 处理时按采样点时间取值并应用）。gain=0 → 静音；env=1 → 不改变。
[EffectEngine("TLTestGain")]
public sealed class GainEffectEngine : IEffectEngine
{
    // 静态面板：忽略 context 返回固定 config。
    public ObjectConfig GetPropertyConfig(IEffectPropertyContext context) => mConfig;
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomations;
    public void Init() { }
    public void Destroy() { }
    public IEffectProcessor CreateProcessor() => new GainProcessor();

    static ObjectConfig BuildConfig()
    {
        var map = new OrderedMap<string, IControllerConfig>();
        map.Add("gain", new SliderConfig { DefaultValue = 1.0, MinValue = 0.0, MaxValue = 2.0 });
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

    sealed class GainProcessor : IEffectProcessor
    {
        public Task Process(IEffectSynthesisInput input, IEffectSynthesisOutput output, IEffectChange change,
                            IProgress<double>? progress = null, CancellationToken cancellation = default)
        {
            double gain = input.Properties.GetDouble("gain", 1.0);
            var audio = input.Audio;
            var src = audio.Samples ?? Array.Empty<float>();
            var dst = new float[src.Length];

            // gain_env 自动化轨：存在则按每个采样点的时间取值，乘到增益上。
            double[]? env = null;
            if (input.TryGetAutomation("gain_env", out var automation) && src.Length > 0)
            {
                var times = new double[src.Length];
                for (int i = 0; i < src.Length; i++)
                    times[i] = audio.StartTime + (double)i / audio.SampleRate;
                env = automation.Evaluate(times);
            }

            for (int i = 0; i < src.Length; i++)
            {
                double m = gain * (env != null ? env[i] : 1.0);
                dst[i] = (float)(src[i] * m);
            }
            output.Audio = new MonoAudio(audio.StartTime, audio.SampleRate, dst);
            progress?.Report(1);
            return Task.CompletedTask;   // 错误经抛异常报告（宿主 catch → passthrough），此处不吞异常。
        }

        public void Dispose() { }
    }
}

// 倒放效果器：无参数，反转样本顺序。与 Gain 同包，链中可观察顺序/增删效果。
[EffectEngine("TLTestReverse")]
public sealed class ReverseEffectEngine : IEffectEngine
{
    public ObjectConfig GetPropertyConfig(IEffectPropertyContext context) => mConfig;
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomations;
    public void Init() { }
    public void Destroy() { }
    public IEffectProcessor CreateProcessor() => new ReverseProcessor();

    static readonly ObjectConfig mConfig = new() { Properties = new OrderedMap<string, IControllerConfig>() };
    static readonly OrderedMap<string, AutomationConfig> mAutomations = new();

    sealed class ReverseProcessor : IEffectProcessor
    {
        public Task Process(IEffectSynthesisInput input, IEffectSynthesisOutput output, IEffectChange change,
                            IProgress<double>? progress = null, CancellationToken cancellation = default)
        {
            var audio = input.Audio;
            var src = audio.Samples ?? Array.Empty<float>();
            var dst = new float[src.Length];
            for (int i = 0; i < src.Length; i++)
                dst[i] = src[src.Length - 1 - i];
            output.Audio = new MonoAudio(audio.StartTime, audio.SampleRate, dst);
            progress?.Report(1);
            return Task.CompletedTask;
        }

        public void Dispose() { }
    }
}
