using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Extensions.Instruments;
using TuneLab.Extensions.Voices;
using TuneLab.Foundation;
using TuneLab.GUI;
using TuneLab.SDK;

namespace TuneLab.ScreenshotBot;

// 文档截图用的样例工程：一条歌声轨（带歌词/音高曲线/颤音）、一条乐器和声轨、一条参考音频轨。
// 内容全部在代码里生成，保证每次重跑得到同一张图。
internal static class DemoProject
{
    const int R = 480;              // PPQ：一个四分音符 = 480 tick
    const int Bar = R * 4;          // 4/4 一小节

    public static ProjectInfo Build(string audioPath)
    {
        // 轨道颜色不写死：新建工程时宿主按 Style.GetNewColor(序号) 配色，插图要照出用户真会看到的那几个色。
        // （Editor.CreateProject 会给 Color 为空的轨道补色，但截图工具直接 new Project，故在此显式补。）
        var info = new ProjectInfo
        {
            Tempos = [new() { Pos = 0, Bpm = 96 }, new() { Pos = Bar * 8, Bpm = 108 }],
            TimeSignatures = [new() { BarIndex = 0, Numerator = 4, Denominator = 4 }],
            Tracks =
            [
                new TrackInfo
                {
                    Name = "主唱",
                    Parts = [BuildVocalPart()],
                },
                new TrackInfo
                {
                    Name = "和声",
                    Gain = -4,
                    Parts = [BuildHarmonyPart()],
                },
                new TrackInfo
                {
                    Name = "参考音频",
                    Parts = [new AudioPartInfo { Name = "参考音频", Pos = 0, EndOffset = Bar * 4, Path = audioPath }],
                },
            ],
        };

        for (int i = 0; i < info.Tracks.Count; i++)
            info.Tracks[i].Color = Style.GetNewColor(i);

        return info;
    }

    // 歌声片段：一句中文歌词，音符长短错落；配一条跟随音符的音高曲线和一处颤音。
    static MidiPartInfo BuildVocalPart()
    {
        // 逐字带上拼音发音：引擎的音素预测吃的是「最终发音」（有显式发音则用它），
        // 汉字本身不带音素信息，写上拼音音素带才有内容可看。
        (string Lyric, string Pronunciation, int Pitch, int Beats)[] line =
        [
            ("晚", "wan", 67, 1), ("风", "feng", 69, 1), ("轻", "qing", 71, 2), ("轻", "qing", 69, 1),
            ("吹", "chui", 67, 1), ("过", "guo", 64, 2), ("小", "xiao", 66, 1), ("城", "cheng", 67, 1),
            ("的", "de", 69, 1), ("街", "jie", 71, 4),
        ];

        var notes = new List<NoteInfo>();
        double pos = Bar;   // 空一小节起唱
        foreach (var (lyric, pronunciation, pitch, beats) in line)
        {
            notes.Add(new NoteInfo { Pos = pos, Dur = R * beats, Pitch = pitch, Lyric = lyric, Pronunciation = pronunciation });
            pos += R * beats;
        }

        // 时间轴画布「呼吸」：几个长音上画一条呼吸强度曲线（秒 = 距音符起点），参数面板里画布的插图才有东西可看。
        (int Note, double[][] Curve)[] breaths =
        [
            (2, [[0, 0.15], [0.35, 0.75], [0.9, 0.35]]),
            (5, [[0, 0.3], [0.5, 0.85], [0.95, 0.2]]),
            (9, [[0, 0.1], [0.6, 0.5], [1.3, 0.9], [1.95, 0.25]]),
        ];
        foreach (var (index, curve) in breaths)
        {
            var array = new PropertyArray(curve.Select(p => (PropertyValue)new PropertyArray([PropertyValue.Create(p[0]), PropertyValue.Create(p[1])])).ToList());
            notes[index].Properties = new PropertyObject(new Map<string, PropertyValue> { { "breath", new PropertyObject(new Map<string, PropertyValue> { { "curve", array } }) } });
        }

        // 参数曲线：音量做一个渐强渐弱，气声在末字上抬起来——参数面板的插图才有东西可看。
        var automations = new Map<string, AutomationInfo>();
        automations.Add("Volume", new AutomationInfo
        {
            DefaultValue = 0,
            Points = BuildCurve(notes[0].Pos, notes[^1].Pos + notes[^1].Dur,
                (t, span) => 3.0 * Math.Sin(Math.PI * t / span) - 0.5),
        });
        automations.Add("Growl", new AutomationInfo
        {
            DefaultValue = 0,
            Points = BuildCurve(notes[^1].Pos - R, notes[^1].Pos + notes[^1].Dur,
                (t, span) => 70.0 * Math.Pow(t / span, 1.5)),
        });

        return new MidiPartInfo
        {
            Name = "主歌",
            Automations = automations,
            Pos = 0,
            EndOffset = Bar * 8,
            SoundSource = PickVoice(),
            Notes = notes,
            Pitch = BuildPitch(notes),
            Vibratos =
            [
                // 末字上的颤音：起振/收束都留一点，画面上能看出包络。
                new VibratoInfo
                {
                    Pos = notes[^1].Pos + R / 2, Dur = R * 3,
                    Frequency = 5.2, Amplitude = 0.45, Phase = 0,
                    Attack = 0.18, Release = 0.25,
                },
            ],
        };
    }

    // 音高曲线：逐音符一段，段内从上一个音滑到本音，让画面看得出「音高是可画的曲线」。
    static PitchInfo BuildPitch(List<NoteInfo> notes)
    {
        var segments = new List<List<Point>>();
        var segment = new List<Point>();
        for (int i = 0; i < notes.Count; i++)
        {
            var note = notes[i];
            double from = i == 0 ? note.Pitch : notes[i - 1].Pitch;
            double glide = Math.Min(R / 2.0, note.Dur / 3);
            // 锚点密度按「手画后已简化」的实际形态给：太密的话锚点工具下会糊成一条点带。
            const double step = 120;
            for (double t = 0; t <= note.Dur; t += step)
            {
                double target = note.Pitch;
                double value = t < glide
                    ? from + (target - from) * (t / glide)
                    : target;
                // 稳定段加一点极缓的起伏，避免看起来像尺子画的直线。
                value += Math.Sin(t / 90.0) * 0.06;
                segment.Add(new Point(note.Pos + t, value));
            }
            // 与下一个音符不相邻时断开成新段（段间 = 音高关断）。
            bool contiguous = i + 1 < notes.Count && Math.Abs(notes[i + 1].Pos - (note.Pos + note.Dur)) < 1e-6;
            if (!contiguous)
            {
                segments.Add(segment);
                segment = new List<Point>();
            }
        }
        if (segment.Count > 0)
            segments.Add(segment);
        return new PitchInfo { Segments = segments };
    }

    // 采样一条曲线：从 startTick 到 endTick 每 60 tick 一个锚点，value = shape(已走过的 tick, 总长)。
    static List<Point> BuildCurve(double startTick, double endTick, Func<double, double, double> shape)
    {
        var points = new List<Point>();
        double span = endTick - startTick;
        for (double t = 0; t <= span; t += 60)
            points.Add(new Point(startTick + t, shape(t, span)));
        return points;
    }

    // 乐器片段：三音和弦叠在一起——乐器音源允许同一片段内音符重叠。
    static MidiPartInfo BuildHarmonyPart()
    {
        var notes = new List<NoteInfo>();
        (int Root, int Bar)[] chords = [(52, 1), (57, 2), (55, 3), (48, 4)];
        foreach (var (root, bar) in chords)
            foreach (int interval in new[] { 0, 4, 7 })
                notes.Add(new NoteInfo { Pos = Bar * bar, Dur = Bar, Pitch = root + interval, Lyric = "-" });

        return new MidiPartInfo
        {
            Name = "和声",
            Pos = 0,
            EndOffset = Bar * 8,
            SoundSource = PickInstrument(),
            Notes = notes,
        };
    }

    // 音源按运行时可用者挑选：沙盒里装的是文档演示插件，取它的第一个音源。
    static SoundSourceInfo PickVoice()
    {
        foreach (var type in VoicesManager.GetAllVoiceEngines())
        {
            if (string.IsNullOrEmpty(type))
                continue;   // 空声源（内置占位）没有可展示的内容
            var infos = VoicesManager.GetAllVoiceInfos(type);
            if (infos == null || infos.Keys.Count == 0)
                continue;
            return new SoundSourceInfo { Kind = SourceKind.Voice, Type = type, Id = infos.Keys[0] };
        }
        Console.WriteLine("[warn] no voice engine available; vocal part will be silent");
        return new SoundSourceInfo { Kind = SourceKind.Voice };
    }

    static SoundSourceInfo PickInstrument()
    {
        foreach (var type in InstrumentsManager.GetAllInstrumentEngines())
        {
            if (string.IsNullOrEmpty(type))
                continue;
            var infos = InstrumentsManager.GetAllInstrumentInfos(type);
            if (infos == null || infos.Keys.Count == 0)
                continue;
            return new SoundSourceInfo { Kind = SourceKind.Instrument, Type = type, Id = infos.Keys[0] };
        }
        Console.WriteLine("[warn] no instrument engine available; harmony part will be silent");
        return new SoundSourceInfo { Kind = SourceKind.Instrument };
    }
}
