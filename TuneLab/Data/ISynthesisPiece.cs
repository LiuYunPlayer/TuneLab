using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Audio;
using TuneLab.Extensions.Voice;

namespace TuneLab.Data;

internal interface ISynthesisPiece
{
    event Action? Finished;
    event Action? Progress;
    IReadOnlyList<INote> Notes { get; }
    double SynthesisProgress { get; }
    string? LastError { get; }
    SynthesisStatus SynthesisStatus { get; }
    bool IsSynthesisEnabled { get; }
    Waveform? Waveform { get; }
    void StartSynthesis();
    void SetDirty(string dirtyType);
    double AudioStartTime { get; }
    int SampleRate { get; }
    int SampleCount { get; }
    IVoiceSynthesisInput Input { get; }
    IVoiceSynthesisOutput Output { get; }
}

internal static class ISynthesisPieceExtension
{
    public static double StartTime(this ISynthesisPiece piece)
    {
        return piece.Input.Notes.First().StartTime;
    }

    public static double EndTime(this ISynthesisPiece piece)
    {
        return piece.Input.Notes.Last().EndTime;
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