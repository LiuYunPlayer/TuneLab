using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Tests;

// 脚本 effect 测试共享替身（宿主实现面）：ScriptedEffectTests（假 sidecar）与
// ScriptedPythonE2ETests（真解释器）同用一份，保证两路对拍的是同一个语义。

internal sealed class EffectTestInput : IEffectSynthesisAudio
{
    public readonly float[] Buffer;
    public EffectTestInput(int count)
    {
        Buffer = new float[count];
        for (int i = 0; i < count; i++)
            Buffer[i] = i / (float)count;
    }
    public long SampleOffset => 44100;
    public int SampleCount => Buffer.Length;
    public int SampleRate => 44100;
    public void Read(int offset, Span<float> destination) => Buffer.AsSpan(offset, destination.Length).CopyTo(destination);
    public IActionEvent<long, int> RangeModified { get; } = new ActionEvent<long, int>();
}

internal sealed class EffectTestSegment : IAudioSegment
{
    public readonly List<float> Written = [];
    public bool Committed, Disposed;
    public void Write(int offset, ReadOnlySpan<float> samples) => Written.AddRange(samples.ToArray());
    public void Commit() => Committed = true;
    public void Resize(long sampleOffset, int sampleCount) { }
    public void Dispose() => Disposed = true;
}

internal sealed class EffectTestAutomation : ISynthesisAutomation
{
    public bool Touched;
    public void Evaluate(IReadOnlyList<double> positions, Span<double> results)
    {
        Touched = true;
        for (int i = 0; i < results.Length; i++)
            results[i] = 0.25;
    }
    public IActionEvent<double, double> RangeModified { get; } = new ActionEvent<double, double>();
}

internal sealed class EffectTestProperties : IReadOnlyNotifiablePropertyObject
{
    public IActionEvent WillModify => ActionEvent.Empty;
    public IActionEvent Modified => ActionEvent.Empty;
    public IReadOnlyNotifiablePropertyObject Object(string key) => this;
    public PropertyValue GetValue(string key, PropertyValue defaultValue)
        => key == "amount" ? PropertyValue.Create(0.5) : defaultValue;
}

internal sealed class EffectTestContext : IEffectSynthesisContext
{
    public readonly EffectTestInput Input = new(4410);
    public readonly List<EffectTestSegment> Segments = [];
    public IEffectSynthesisAudio InputLocal => Input;
    IEffectSynthesisAudio IEffectSynthesisContext.Input => Input;
    public IReadOnlyNotifiablePropertyObject Properties { get; } = new EffectTestProperties();
    public IReadOnlyMap<string, ISynthesisAutomation> Automations { get; } =
        new Map<string, ISynthesisAutomation> { ["intensity"] = new EffectTestAutomation() };
    public EffectSynthesisSnapshot GetSnapshot(double startTime, double endTime) => throw new NotImplementedException();
    public IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate)
    {
        var s = new EffectTestSegment();
        Segments.Add(s);
        return s;
    }
}

internal sealed class EffectTestView : IEffectSynthesisView
{
    public PropertyObject Properties => PropertyObject.Empty;
    public IReadOnlyMap<string, IAutomationEvaluator> Automations { get; } = new Map<string, IAutomationEvaluator>();
}

internal sealed class EffectTestPropContext : IEffectSynthesisPropertyContext
{
    public IReadOnlyList<IEffectSynthesisView> Effects { get; } = [new EffectTestView()];
}
