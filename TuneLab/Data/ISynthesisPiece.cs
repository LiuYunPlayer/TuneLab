using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Extensions.Voices;

namespace TuneLab.Data;

internal interface ISynthesisPiece : ISynthesisData
{
    event Action? Finished;
    event Action? Progress;
    new IEnumerable<INote> Notes { get; }
    double SynthesisProgress { get; }
    string? LastError { get; }
    SynthesisStatus SynthesisStatus { get; }
    bool IsSynthesisEnabled { get; }
    SynthesisResult? SynthesisResult { get; }
    Waveform? Waveform { get; }
    void StartSynthesis();
    void SetDirty(string dirtyType);
    IEnumerable<ISynthesisNote> ISynthesisData.Notes => Notes;
    double AudioStartTime { get; }
    int SampleRate { get; }
    int SampleCount { get; }
}

internal static class ISynthesisPieceExtension
{
    public static double StartTime(this ISynthesisPiece piece)
    {
        return ((ISynthesisData)piece).StartTime();
    }

    public static double EndTime(this ISynthesisPiece piece)
    {
        return ((ISynthesisData)piece).EndTime();
    }

    public static double AudioStartTime(this ISynthesisPiece piece)
    {
        return piece.AudioStartTime;
    }

    public static double AudioEndTime(this ISynthesisPiece piece)
    {
        return piece.AudioStartTime + (piece.SampleCount == 0 ? 0 : (double)piece.SampleCount / piece.SampleRate);
    }
}