using System;
using TuneLab.Primitives.Audio;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Effect;

// 两个 effect 引擎打在同一个包/程序集里，验证：引擎按 [EffectEngine] 注册、一包多 effect、
// 参数面板（Gain 有 gain 滑块）、per-effect 时间轴自动化（Gain 有 gain_env 自动化轨）、
// 链串行（Gain 与 Reverse 串联）、bypass、增删/重排。

// 增益效果器：output = input * gain * gainEnv(t)。gain 为静态参数滑块；gain_env 为一条可编辑的时间轴自动化轨
// （验证 per-effect 自动化：声明 AutomationConfig + 合成时按采样点时间取值并应用）。gain=0 → 静音；env=1 → 不改变。
[EffectEngine("TLTestGain")]
public sealed class GainEffectEngine : IEffectEngine
{
    public ObjectConfig PropertyConfig => mConfig;
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomations;
    public bool Init(string enginePath, out string? error) { error = null; return true; }
    public void Destroy() { }
    public IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output)
        => new GainTask(input, output);

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

    sealed class GainTask(IEffectSynthesisInput input, IEffectSynthesisOutput output) : IEffectSynthesisTask
    {
        public event Action? Complete;
        public event Action<double>? Progress;
        public event Action<string>? Error;

        public void Start()
        {
            try
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
                Progress?.Invoke(1);
                Complete?.Invoke();
            }
            catch (Exception ex) { Error?.Invoke(ex.Message); }
        }

        public void Stop() { }
    }
}

// 倒放效果器：无参数，反转样本顺序。与 Gain 同包，链中可观察顺序/增删效果。
[EffectEngine("TLTestReverse")]
public sealed class ReverseEffectEngine : IEffectEngine
{
    public ObjectConfig PropertyConfig => mConfig;
    public IReadOnlyOrderedMap<string, AutomationConfig> AutomationConfigs => mAutomations;
    public bool Init(string enginePath, out string? error) { error = null; return true; }
    public void Destroy() { }
    public IEffectSynthesisTask CreateSynthesisTask(IEffectSynthesisInput input, IEffectSynthesisOutput output)
        => new ReverseTask(input, output);

    static readonly ObjectConfig mConfig = new() { Properties = new OrderedMap<string, IControllerConfig>() };
    static readonly OrderedMap<string, AutomationConfig> mAutomations = new();

    sealed class ReverseTask(IEffectSynthesisInput input, IEffectSynthesisOutput output) : IEffectSynthesisTask
    {
        public event Action? Complete;
        public event Action<double>? Progress;
        public event Action<string>? Error;

        public void Start()
        {
            try
            {
                var src = input.Audio.Samples ?? Array.Empty<float>();
                var dst = new float[src.Length];
                for (int i = 0; i < src.Length; i++)
                    dst[i] = src[src.Length - 1 - i];
                output.Audio = new MonoAudio(input.Audio.StartTime, input.Audio.SampleRate, dst);
                Progress?.Invoke(1);
                Complete?.Invoke();
            }
            catch (Exception ex) { Error?.Invoke(ex.Message); }
        }

        public void Stop() { }
    }
}
