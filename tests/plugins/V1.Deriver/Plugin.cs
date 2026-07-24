using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Deriver;

// deriver 参考实现：基于自相关（autocorrelation）的单声部 pitch→note 转写。
// 一次性从音频派生「音符 + 音高曲线」的新 MIDI part——最小、可读、无外部依赖，作 AI 参考与回归夹具。
// 单位纪律示范：产物一律说物理秒（note StartTime/EndTime、Pitch Point.X 皆绝对音频内容秒），插件从不碰 tick——
// 秒→tick 换算全在宿主侧（见 docs/deriver-sdk-design.md §1、docs/deriver-plugin-dev.md）。
//
// GetPropertyConfig 不依赖 Init：本引擎无模型，Init 空实现即可。产物随参数而变、不做静态能力声明。
public sealed class AutocorrelationDeriverEngine : IAudioDerivationEngine
{
    // config 是当前情境的纯函数；本引擎参数不随情境变，忽略 context 返回固定面板即可。
    public ObjectConfig GetPropertyConfig(IAudioDerivationContext context) => mConfig;

    public void Init() { }
    public void Destroy() { }

    public Task<DerivedResult?> Derive(IAudioDerivationInput input, IProgress<DerivationProgress> progress, CancellationToken cancellation = default)
    {
        int rate = input.SampleRate;
        int total = (int)Math.Min(int.MaxValue, input.SampleCount);
        if (rate <= 0 || total <= 0)
            return Task.FromResult<DerivedResult?>(null);

        // 参数（run-inputs，冻结）：读不到即用默认（稀疏值语义，同 voice/script 插件）。
        double minNoteSeconds = Math.Max(0.01, input.Properties.GetDouble("minNoteMs", 80) / 1000.0);
        double silenceThreshold = Math.Max(0, input.Properties.GetDouble("silence", 0.01));
        double voicedThreshold = Math.Clamp(input.Properties.GetDouble("voiced", 0.6), 0.1, 0.99);

        // 下混到单声道自有缓冲（copy-out：worker 只读快照）。
        var mono = new float[total];
        var scratch = new float[total];
        for (int c = 0; c < input.ChannelCount; c++)
        {
            input.Read(c, 0, scratch);
            for (int i = 0; i < total; i++)
                mono[i] += scratch[i];
        }
        if (input.ChannelCount > 1)
            for (int i = 0; i < total; i++)
                mono[i] /= input.ChannelCount;

        const double minF0 = 65.0, maxF0 = 1000.0;
        int minLag = Math.Max(2, (int)(rate / maxF0));
        int maxLag = Math.Max(minLag + 1, (int)(rate / minF0));
        int window = Math.Min(total, maxLag * 2);
        int hop = Math.Max(1, rate / 100);
        int lagStep = 2;   // 参考实现：粗采 lag 步以压运行时（真实模型更精细）

        var frameTimes = new List<double>();
        var frameSemitones = new List<double>();   // NaN = 该帧清音（unvoiced）
        int frameCount = Math.Max(1, (total - window) / hop + 1);

        for (int f = 0, start = 0; start + window <= total; f++, start += hop)
        {
            if (cancellation.IsCancellationRequested)
                return Task.FromResult<DerivedResult?>(null);   // 取消 = 正常结局：返回 null

            double energy = 0;
            for (int i = 0; i < window; i++)
                energy += (double)mono[start + i] * mono[start + i];
            energy /= window;

            double semitone = double.NaN;
            if (energy >= silenceThreshold)
            {
                double bestR = 0;
                int bestLag = 0;
                double r0 = Ac(mono, start, window, 0);
                for (int lag = minLag; lag <= maxLag; lag += lagStep)
                {
                    double r = Ac(mono, start, window - lag, lag) / (r0 + 1e-9);
                    if (r > bestR) { bestR = r; bestLag = lag; }
                }
                if (bestLag > 0 && bestR >= voicedThreshold)
                {
                    double f0 = (double)rate / bestLag;
                    semitone = 69.0 + 12.0 * Math.Log2(f0 / 440.0);
                }
            }

            frameTimes.Add((start + window / 2.0) / rate);
            frameSemitones.Add(semitone);

            if ((f & 15) == 0)
                progress.Report(new DerivationProgress { Progress = Math.Min(1.0, (double)f / frameCount), Message = "Analyzing pitch" });
        }

        var part = new DerivedMidiPart
        {
            // 不设 StartTime/EndTime：默认 0..+∞ = 整段；裁剪由宿主 apply 侧按源 part 裁剪窗处理，本插件不关心。
            Notes = SegmentNotes(frameTimes, frameSemitones, minNoteSeconds),
            Pitch = new DerivedPitch { Segments = BuildPitchSegments(frameTimes, frameSemitones) },
        };
        var track = new DerivedTrack { Name = "Transcribed", Parts = new DerivedPart[] { part } };
        return Task.FromResult<DerivedResult?>(new DerivedResult { Tracks = new[] { track } });
    }

    // 自相关 sum(x[start+i] * x[start+i+lag])，i∈[0,len)。
    static double Ac(float[] x, int start, int len, int lag)
    {
        double sum = 0;
        for (int i = 0; i < len; i++)
            sum += (double)x[start + i] * x[start + i + lag];
        return sum;
    }

    // 音符切分：连续「同（四舍五入）半音」的浊音帧成一个音符；清音断开；短于 minNoteSeconds 的丢弃。
    static IReadOnlyList<DerivedNote> SegmentNotes(List<double> times, List<double> semitones, double minNoteSeconds)
    {
        var notes = new List<DerivedNote>();
        int runStart = -1;
        int runPitch = 0;
        for (int i = 0; i <= semitones.Count; i++)
        {
            bool voiced = i < semitones.Count && !double.IsNaN(semitones[i]);
            int pitch = voiced ? (int)Math.Round(semitones[i]) : int.MinValue;
            bool breakRun = runStart >= 0 && (!voiced || pitch != runPitch);
            if (breakRun)
            {
                double startTime = times[runStart];
                double endTime = times[i - 1];
                if (endTime - startTime >= minNoteSeconds)
                    notes.Add(new DerivedNote { StartTime = startTime, EndTime = endTime, Pitch = runPitch });
                runStart = -1;
            }
            if (voiced && runStart < 0)
            {
                runStart = i;
                runPitch = pitch;
            }
        }
        return notes;
    }

    // 音高曲线：浊音连续段各成一条 (绝对秒, 半音 float) 折线；清音帧断开段。
    static IReadOnlyList<IReadOnlyList<Point>> BuildPitchSegments(List<double> times, List<double> semitones)
    {
        var segments = new List<IReadOnlyList<Point>>();
        List<Point>? current = null;
        for (int i = 0; i < semitones.Count; i++)
        {
            if (double.IsNaN(semitones[i]))
            {
                if (current is { Count: > 1 }) segments.Add(current);
                current = null;
            }
            else
            {
                current ??= new List<Point>();
                current.Add(new Point(times[i], semitones[i]));
            }
        }
        if (current is { Count: > 1 }) segments.Add(current);
        return segments;
    }

    static ObjectConfig BuildConfig()
    {
        var map = new OrderedMap<PropertyKey, IControllerConfig>();
        map.Add(("minNoteMs", "Min Note (ms)"), SliderConfig.Linear(80, 10, 500));
        map.Add(("silence", "Silence Threshold"), SliderConfig.Linear(0.01, 0.0, 0.1));
        map.Add(("voiced", "Voiced Threshold"), SliderConfig.Linear(0.6, 0.1, 0.95));
        return ObjectConfig.Create(map);
    }

    static readonly ObjectConfig mConfig = BuildConfig();
}
