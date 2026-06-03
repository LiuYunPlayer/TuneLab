using System;
using TuneLab.Primitives.Audio;
using TuneLab.Primitives.DataStructures;
using TuneLab.SDK.Base;
using TuneLab.SDK.Effect;

namespace TuneLab.TestPlugins.V1Effect;

// 两个 effect 引擎打在同一个包/程序集里，验证：引擎按 [EffectEngine] 注册、一包多 effect、
// 参数面板（Gain 有 gain 滑块）、链串行（Gain 与 Reverse 串联）、bypass、增删/重排。

// 增益效果器：output = input * gain（gain 为参数）。gain=0 → 静音；gain=1 → 原样。
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
        map.Add("gain", new SliderConfig(1.0, 0.0, 2.0, false));
        return new ObjectConfig(map);
    }

    static readonly ObjectConfig mConfig = BuildConfig();
    static readonly OrderedMap<string, AutomationConfig> mAutomations = new();

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
                var src = input.Audio.Samples ?? Array.Empty<float>();
                var dst = new float[src.Length];
                for (int i = 0; i < src.Length; i++)
                    dst[i] = (float)(src[i] * gain);
                output.Audio = new MonoAudio(input.Audio.StartTime, input.Audio.SampleRate, dst);
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

    static readonly ObjectConfig mConfig = new(new OrderedMap<string, IControllerConfig>());
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
