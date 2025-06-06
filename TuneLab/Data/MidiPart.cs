using NAudio.CoreAudioApi;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using TuneLab.Audio;
using TuneLab.Extensions.ControllerConfigs;
using TuneLab.Core.DataInfo;
using TuneLab.Extensions.Synthesizer;
using TuneLab.Extensions.Voice;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;
using TuneLab.Foundation.Property;
using TuneLab.Foundation.Science;
using TuneLab.Foundation.Utils;

namespace TuneLab.Data;

internal class MidiPart : Part, IMidiPart
{
    public IActionEvent<ISynthesisPiece> SynthesisStatusChanged => mSynthesisStatusChanged;
    public override DataString Name { get; }
    public override DataStruct<double> Pos { get; }
    public override DataStruct<double> Dur { get; }
    public DataStruct<double> Gain { get; }
    public IDataPropertyObject Properties { get; }
    public INoteList Notes => mNotes;
    public IVoice Voice => mVoice;
    IDataProperty<double> IMidiPart.Gain => Gain;
    public IReadOnlyDataObjectMap<string, IAutomation> Automations => mAutomations;
    public IReadOnlyDataObjectList<IEffect> Effects => mEffects;
    public IPiecewiseCurve Pitch => mPitchLine;
    public IReadOnlyList<ISynthesisPiece> SynthesisPieces => mSynthesisPieces;
    public IReadOnlyDataObjectList<Vibrato> Vibratos => mVibratos;

    public MidiPart(ITrack track, MidiPartInfo info) : base(track)
    {
        mReSegmentMergeHandler = new(ReSegmentImpl);
        mPrepareMergeHandler = new(() => { foreach (var piece in mSynthesisPieces) piece.Prepare(); });
        Name = new(this, string.Empty);
        Pos = new(this);
        Dur = new(this);
        Gain = new(this);
        Properties = new DataPropertyObject(this);
        mVoice = new(this, new VoiceInfo());
        mNotes = new();
        mNotes.Attach(this);
        mVibratos = new(this);
        mAutomations = new(this);
        mEffects = new(this);
        mPitchLine = new();
        mPitchLine.Attach(this);
        Dur.Modified.Subscribe(mDurationChanged);
        mVoice.Modified.Subscribe(ReGeneratePieces);
        mPitchLine.RangeModified.Subscribe(OnPitchRangeModified);
        mNotes.ListModified.Subscribe(ReSegment);
        mVibratos.Any(vibrato => vibrato.RangeModified).Subscribe(OnPitchRangeModified);
        TempoManager.Modified.Subscribe(ReGeneratePieces); // TODO: 改为tempoManager改变发出重分片信号
        Pos.Modified.Subscribe(ReGeneratePieces); // TODO: 改为tempoManager改变发出重分片信号
        IDataObject<MidiPartInfo>.SetInfo(this, info);
    }

    public void InsertNote(INote note)
    {
        mNotes.Insert(note);
    }

    public bool RemoveNote(INote note)
    {
        return mNotes.Remove(note);
    }

    public void InsertVibrato(Vibrato vibrato)
    {
        int index = 0;
        for (; index < mVibratos.Count; index++)
        {
            if (mVibratos[index].Pos.Value < vibrato.Pos.Value)
                continue;

            if (mVibratos[index].Pos.Value > vibrato.Pos.Value)
                break;

            if (mVibratos[index].Dur.Value < vibrato.Dur.Value)
                break;
        }
        mVibratos.Insert(index, vibrato);
    }

    public bool RemoveVibrato(Vibrato vibrato)
    {
        return mVibratos.Remove(vibrato);
    }

    public void LockPitch(double start, double end, double extension)
    {
        double startTime = TempoManager.GetTime(Pos + start);
        double endTime = TempoManager.GetTime(Pos + end);
        foreach (var pitchLine in mSynthesisPieces.SelectMany(piece => piece.Output.SynthesizedPitch))
        {
            if (pitchLine.Count < 2)
                continue;

            var pitchEndTime = pitchLine.Last().X;
            if (pitchEndTime < startTime)
                continue;

            var pitchStartTime = pitchLine.First().X;
            if (pitchStartTime > endTime)
                break;

            double pos = Pos;
            var points = new List<Point>();
            foreach (var point in pitchLine)
            {
                double time = point.X;
                if (time < startTime)
                    continue;

                if (time > endTime)
                    break;

                points.Add(new(TempoManager.GetTick(time) - pos, point.Y));
            }
            mPitchLine.AddLine(points, extension);
        }
    }

    public double[] GetVibratoDeviation(IReadOnlyList<double> ticks, string automationID = "")
    {
        double[] values = new double[ticks.Count];
        values.Fill(0);
        double start = ticks.First();
        double end = ticks.Last();
        int tickIndex = 0;
        foreach (var vibrato in mVibratos)
        {
            if (vibrato.EndPos() < start)
                continue;

            if (vibrato.StartPos() > end)
                break;

            double amplitude = string.IsNullOrEmpty(automationID) ? vibrato.Amplitude : vibrato.AffectedAutomations.GetValueOrDefault(automationID, 0);
            if (amplitude == 0)
                continue;

            while (tickIndex > 0 && ticks[tickIndex] > vibrato.StartPos())
            {
                tickIndex--;
            }

            while (tickIndex < ticks.Count && ticks[tickIndex] < vibrato.StartPos())
            {
                tickIndex++;
            }

            int offset = tickIndex;
            while (tickIndex < ticks.Count && ticks[tickIndex] <= vibrato.EndPos())
            {
                tickIndex++;
            }

            double[] ts = new double[tickIndex - offset];
            for (int i = 0; i < ts.Length; i++)
            {
                ts[i] = ticks[i + offset];
            }
            double[] amplitudes;
            if (Automations.TryGetValue(ConstantDefine.VibratoEnvelopeID, out var vibratoEnvelope))
            {
                amplitudes = vibratoEnvelope.GetValues(ts);
                for (int i = 0; i < amplitudes.Length; i++)
                {
                    amplitudes[i] = Math.Max(0, amplitudes[i]) * amplitude;
                }
            }
            else
            {
                amplitudes = new double[ts.Length];
                amplitudes.Fill(amplitude);
            }

            double startTime = TempoManager.GetTime(vibrato.GlobalStartPos());
            double endTime = TempoManager.GetTime(vibrato.GlobalEndPos());
            double durTime = endTime - startTime;
            double pos = this.Pos.Value;

            double[] times = new double[ts.Length];
            for (int i = 0; i < times.Length; i++)
            {
                times[i] = TempoManager.GetTime(pos + ts[i]) - startTime;
            }

            double attack = vibrato.Attack;
            for (int i = 0; i < times.Length; i++)
            {
                double r = times[i] / attack;
                if (r >= 1)
                    break;

                amplitudes[i] *= MathUtility.CubicInterpolation(r);
            }

            double release = vibrato.Release;
            for (int i = times.Length - 1; i >= 0; i--)
            {
                double r = (durTime - times[i]) / release;
                if (r >= 1)
                    break;

                amplitudes[i] *= MathUtility.CubicInterpolation(r);
            }

            double frequency = vibrato.Frequency;
            double phase = -vibrato.Phase * Math.PI;
            double w = 2 * Math.PI * frequency;
            for (int i = 0; i < times.Length; i++)
            {
                amplitudes[i] *= Math.Sin(w * times[i] + phase);
            }

            for (int i = 0; i < amplitudes.Length; i++)
            {
                values[i + offset] += amplitudes[i];
            }

            if (tickIndex == ticks.Count)
                break;
        }

        return values;
    }

    public double[] GetFinalPitch(IReadOnlyList<double> ticks)
    {
        var pitch = mPitchLine.GetValues(ticks);
        var vibratos = GetVibratoDeviation(ticks);
        for (int i = 0; i < ticks.Count; i++)
        {
            if (double.IsNaN(pitch[i]))
                continue;

            pitch[i] += vibratos[i];
        }

        return pitch;
    }

    public double[] GetAutomationValues(IReadOnlyList<double> ticks, string automationID)
    {
        double[] values;
        if (mAutomations.TryGetValue(automationID, out var automation))
        {
            values = automation.GetValues(ticks);
        }
        else
        {
            var defaultValue = GetEffectiveAutomationConfig(automationID).DefaultValue;
            values = new double[ticks.Count];
            values.Fill(defaultValue);
        }

        return values;
    }

    public double[] GetFinalAutomationValues(IReadOnlyList<double> ticks, string automationID)
    {
        var values = GetAutomationValues(ticks, automationID);

        var vibratos = GetVibratoDeviation(ticks, automationID);
        for (int i = 0; i < ticks.Count; i++)
        {
            values[i] += vibratos[i];
        }

        return values;
    }

    public double[] GetBasePitchByTimes(IReadOnlyList<double> times)
    {
        double[] pitch = new double[times.Count];
        return pitch;
    }

    public void BeginMergeReSegment()
    {
        PushAndDo(new Command(mReSegmentMergeHandler.Begin, mReSegmentMergeHandler.End));
    }

    public void EndMergeReSegment()
    {
        PushAndDo(new Command(mReSegmentMergeHandler.End, mReSegmentMergeHandler.Begin));
    }

    public void DisableAutoPrepare()
    {
        PushAndDo(new Command(mPrepareMergeHandler.Begin, mPrepareMergeHandler.End));
        PushAndDo(new RedoOnlyCommand(mPrepareMergeHandler.Trigger));
    }

    public void EnableAutoPrepare()
    {
        PushAndDo(new UndoOnlyCommand(mPrepareMergeHandler.Trigger));
        PushAndDo(new Command(mPrepareMergeHandler.End, mPrepareMergeHandler.Begin));
    }

    void ReSegment()
    {
        mReSegmentMergeHandler.Trigger();
    }

    public override void Activate()
    {
        ReSegment();
    }

    public override void Deactivate()
    {
        foreach (var piece in mSynthesisPieces)
        {
            piece.Dispose();
        }
        mSynthesisPieces.Clear();
    }

    void ReSegmentImpl()
    {
        var newSegments = mVoice.Segment(mNotes);
        List<SynthesisPiece> newPieces = new();
        foreach (var segment in newSegments)
        {
            bool exist = false;
            foreach (var piece in mSynthesisPieces)
            {
                if (piece.Notes.SequenceEqual(segment))
                {
                    exist = true;
                    newPieces.Add(piece);
                    mSynthesisPieces.Remove(piece);
                    break;
                }
            }
            if (!exist)
            {
                var newPiece = new SynthesisPiece(this, segment);
                newPiece.SynthesisStatusChanged += () => { mSynthesisStatusChanged.Invoke(newPiece); };
                newPiece.Finished += () => { if (string.IsNullOrEmpty(newPiece.LastError)) return; Log.Debug(string.Format("Synthesis error: {0} {1}", Voice.Name, newPiece.LastError)); };
                newPieces.Add(newPiece);
            }
        }

        foreach (var piece in mSynthesisPieces)
        {
            piece.Dispose();
        }
        mSynthesisPieces.Clear();

        mSynthesisPieces = newPieces;
    }

    public INote CreateNote(NoteInfo info)
    {
        return new Note(this, info);
    }

    public Vibrato CreateVibrato(VibratoInfo info)
    {
        var vibrato = new Vibrato(this);
        IDataObject<VibratoInfo>.SetInfo(vibrato, info);
        return vibrato;
    }

    public IEffect CreateEffect(EffectInfo info)
    {
        return new Effect(this, info);
    }

    public IAutomation? AddAutomation(string automationID)
    {
        if (mAutomations.TryGetValue(automationID, out var value))
            return value;

        if (!Voice.TryGetAutomationConfig(automationID, out var config))
            return null;

        var automation = CreateAutomation(automationID, new() { DefaultValue = config.DefaultValue });
        mAutomations.Add(automationID, automation);
        return automation;
    }

    public void InsertEffect(int index, IEffect effect)
    {
        mEffects.Insert(index, effect);
    }

    public void RemoveEffect(IEffect effect)
    {
        mEffects.Remove(effect);
    }

    public void RemoveEffectAt(int index)
    {
        mEffects.RemoveAt(index);
    }

    public AutomationConfig GetEffectiveAutomationConfig(string id)
    {
        if (Voice.TryGetAutomationConfig(id, out var config))
            return config;

        throw new ArgumentException(string.Format("Automation {0} is not effective!", id));
    }

    public ISynthesisPiece? FindNextNotCompletePiece(double time)
    {
        ISynthesisPiece? result = null;

        foreach (var piece in SynthesisPieces)
        {
            if (!piece.IsSynthesisEnabled || piece.SynthesisStatus == SynthesisStatus.SynthesisSucceeded || piece.SynthesisStatus == SynthesisStatus.SynthesisFailed)
                continue;

            if (result == null)
            {
                result = piece;
                continue;
            }

            if (result.EndTime() < time)
            {
                if (piece.EndTime() < time && piece.StartTime() > result.StartTime())
                {
                    continue;
                }
            }
            else
            {
                if (piece.EndTime() < time || piece.StartTime() > result.StartTime())
                {
                    continue;
                }
            }

            result = piece;
        }

        return result;
    }

    public override MidiPartInfo GetInfo()
    {
        return new()
        {
            Name = Name,
            Pos = Pos,
            Dur = Dur,
            Gain = Gain,
            Notes = mNotes.GetInfo().ToInfo(),
            Automations = mAutomations.GetInfo().ToInfo(),
            Effects = mEffects.GetInfo().ToInfo(),
            Pitch = mPitchLine.GetInfo(),
            Vibratos = mVibratos.GetInfo().ToInfo(),
            Voice = mVoice.GetInfo(),
            Properties = new(Properties.GetInfo()),
        };
    }

    void IDataObject<MidiPartInfo>.SetInfo(MidiPartInfo info)
    {
        IDataObject<MidiPartInfo>.SetInfo(Name, info.Name);
        IDataObject<MidiPartInfo>.SetInfo(Name, info.Name);
        IDataObject<MidiPartInfo>.SetInfo(Pos, info.Pos);
        IDataObject<MidiPartInfo>.SetInfo(Dur, info.Dur);
        IDataObject<MidiPartInfo>.SetInfo(Gain, info.Gain);
        IDataObject<MidiPartInfo>.SetInfo(mNotes, info.Notes.Convert(CreateNote).ToArray());
        IDataObject<MidiPartInfo>.SetInfo(mVibratos, info.Vibratos.Convert(CreateVibrato).ToArray());
        IDataObject<MidiPartInfo>.SetInfo(mAutomations, info.Automations.Convert(CreateAutomation).ToMap());
        IDataObject<MidiPartInfo>.SetInfo(mEffects, info.Effects.Convert(CreateEffect).ToArray());
        IDataObject<MidiPartInfo>.SetInfo(mPitchLine, info.Pitch);
        IDataObject<MidiPartInfo>.SetInfo(mVoice, info.Voice);
        IDataObject<MidiPartInfo>.SetInfo(Properties, info.Properties);
    }

    void ReGeneratePieces()
    {
        Deactivate();
        Activate();
    }

    string GetNotePropertyDirtyType()
    {
        return "duration"; //TODO: 改成正确的实现
    }

    Automation CreateAutomation(string automationID, AutomationInfo info)
    {
        var automation = new Automation(this, info);
        if (automationID != ConstantDefine.VolumeID)
        {
            automation.RangeModified.Subscribe(OnAutomationRangeModified);
            automation.DefaultValue.Modified.Subscribe(() => { this.SetAllPieceDirty(""); }); // TODO: 传递id
        }
        return automation;
    }

    void OnPitchRangeModified(double start, double end)
    {
        double startTime = TempoManager.GetTime(Pos + start);
        double endTime = TempoManager.GetTime(Pos + end);
        foreach (var piece in mSynthesisPieces)
        {
            if (piece.AudioEndTime() < startTime)
                continue;

            if (piece.AudioStartTime() > endTime)
                break;

            piece.SetDirty("TODO");
        }
    }

    void OnAutomationRangeModified(double start, double end)
    {
        double startTime = TempoManager.GetTime(Pos + start);
        double endTime = TempoManager.GetTime(Pos + end);
        foreach (var piece in mSynthesisPieces)
        {
            if (piece.AudioEndTime() < startTime)
                continue;

            if (piece.AudioStartTime() > endTime)
                break;

            piece.SetDirty("TODO");
        }
    }

    public override IAudioData GetAudioData(int offset, int count)
    {
        float[] data = new float[count];
        int sampleRate = ((IAudioSource)this).SampleRate;
        double startTime = ((IAudioSource)this).StartTime;
        int partOffset = (int)(sampleRate * startTime);
        foreach (var piece in mSynthesisPieces)
        {
            int pieceOffset = (int)(piece.AudioStartTime() * piece.SampleRate);
            int startIndex = partOffset + offset - pieceOffset;
            if (startIndex >= piece.SampleCount)
                continue;

            int endIndex = startIndex + count;
            if (endIndex <= 0)
                break;

            var pieceData = piece.GetAudioFloatArray(startIndex, endIndex - startIndex);
            for (int i = 0; i < count; i++)
                data[i] += pieceData[i];
        }
        double pos = Pos;
        var ticks = new double[count];
        for (int i = 0; i < count; i++)
        {
            ticks[i] = TempoManager.GetTick((double)(offset + i) / sampleRate + startTime) - pos;
        }
        var volumes = GetFinalAutomationValues(ticks, ConstantDefine.VolumeID);
        for (int i = 0; i < volumes.Length; i++)
        {
            data[i] = (float)(data[i] * Volume2Level(volumes[i]));
        }
        if (Gain != 0)
        {
            float volume = (float)MusicTheory.dB2Level(Gain);
            for (int i = 0; i < data.Length; i++)
            {
                data[i] *= volume;
            }
        }
        return new MonoAudioData(data);
    }

    public override void OnSampleRateChanged()
    {
        this.SetAllPieceDirty("");
    }

    static MidiPart()
    {
        var a = Math.Log(Math.Pow(10, 1 / 20.0));
        x0 = 1 / a - 12;
        k = Math.Pow(10, x0 / 20) * a;
    }
    const double VOLUME_MAX = 12;
    public static double Volume2Level(double volume)
    {
        if (volume > x0)
            return MusicTheory.dB2Level(volume);

        if (volume > -VOLUME_MAX)
            return (volume + VOLUME_MAX) * k;

        return 0;
    }

    static readonly double x0;
    static readonly double k;

    protected override int SampleRate => AudioEngine.SampleRate.Value;
    bool AutoPrepare => !mPrepareMergeHandler.IsMerging;

    readonly MergeHandler mReSegmentMergeHandler;
    readonly MergeHandler mPrepareMergeHandler;

    readonly ActionEvent<ISynthesisPiece> mSynthesisStatusChanged = new();

    readonly NoteList mNotes;
    readonly DataObjectList<Vibrato> mVibratos;
    readonly DataObjectMap<string, IAutomation> mAutomations;
    readonly DataObjectList<IEffect> mEffects;
    readonly PiecewiseCurve mPitchLine;
    readonly Voice mVoice;

    class AutomationValueGetter(MidiPart part, string automationID) : IAutomationValueGetter
    {
        public double[] GetValue(IReadOnlyList<double> times)
        {
            double pos = part.Pos;
            var ticks = part.TempoManager.GetTicks(times);
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] -= pos;
            }
            return part.GetFinalAutomationValues(ticks, automationID);
        }
    }

    class PitchValueGetter(MidiPart part) : IAutomationValueGetter
    {
        public double[] GetValue(IReadOnlyList<double> times)
        {
            double pos = part.Pos;
            var ticks = part.TempoManager.GetTicks(times);
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] -= pos;
            }
            return part.GetFinalPitch(ticks);
        }
    }

    class piecewiseCurveValueGetter(MidiPart part, IPiecewiseCurve piecewiseCurve) : IAutomationValueGetter
    {
        public double[] GetValue(IReadOnlyList<double> times)
        {
            double pos = part.Pos;
            var ticks = part.TempoManager.GetTicks(times);
            for (int i = 0; i < ticks.Length; i++)
            {
                ticks[i] -= pos;
            }
            return piecewiseCurve.GetValues(ticks);
        }
    }

    class NoteList : DataObjectLinkedList<INote>, INoteList
    {
        public IMergableEvent SelectionChanged => mSelectionChanged;

        public NoteList()
        {
            ListModified.Subscribe(mSelectionChanged);
            this.Any(note => note.SelectionChanged).Subscribe(mSelectionChanged);
        }

        protected override bool IsInOrder(INote prev, INote next)
        {
            if (prev.StartPos() < next.StartPos())
                return true;

            if (prev.StartPos() > next.StartPos())
                return false;

            if (prev.EndPos() > next.EndPos())
                return true;

            if (prev.EndPos() < next.EndPos())
                return false;

            return true;
        }

        readonly MergableEvent mSelectionChanged = new();
    }

    class VoiceSynthesisInput(MidiPart midiPart, IReadOnlyList<ISynthesisNote> notes) : IVoiceSynthesisInput
    {
        public IReadOnlyList<ISynthesisNote> Notes => notes;

        public IReadOnlyMap<string, IReadOnlyPropertyValue> Properties => midiPart.Properties;

        public IAutomationValueGetter Pitch => new PitchValueGetter(midiPart);

        public bool GetAutomation(string automationID, [MaybeNullWhen(false), NotNullWhen(true)] out IAutomationValueGetter? automation)
        {
            if (!midiPart.Voice.IsEffectiveAutomation(automationID))
            {
                automation = null;
                return false;
            }

            automation = new AutomationValueGetter(midiPart, automationID);
            return true;
        }
    }

    class VoiceSynthesisOutput : IVoiceSynthesisOutput
    {
        public MonoAudio Audio { get; set; } = new MonoAudio() { StartTime = 0, Samples = [], SampleRate = 0 };

        public IMap<string, IReadOnlyList<IReadOnlyList<Point>>> SynthesizedAutomations { get; set; } = [];
        public IReadOnlyList<IReadOnlyList<Point>> SynthesizedPitch { get; set; } = [];
        public IReadOnlyMap<ISynthesisNote, SynthesizedPhoneme[]> SynthesizedPhonemes { get; set; } = [];

        public float[] Read(int offset, int count)
        {
            float[] data = new float[count];
            int start = Math.Max(offset, 0);
            int end = Math.Min(offset + count, Audio.Samples.Length);
            for (int i = start; i < end; i++)
            {
                data[i - offset] = Audio.Samples[i];
            }
            return data;
        }
    }

    class SynthesisPiece : ISynthesisPiece, IDisposable
    {
        public event Action? Finished;
        public event Action? Progress;
        public event Action? SynthesisStatusChanged;
        public double SynthesisProgress => mSynthesisProgress;
        public string? LastError => mLastError;
        public Waveform? Waveform => mWaveform;
        public bool IsSynthesisEnabled => mIsPrepared;
        public SynthesisStatus SynthesisStatus
        {
            get => mSynthesisStatus;
            private set
            {
                if (mSynthesisStatus == value)
                    return;

                mSynthesisStatus = value;
                SynthesisStatusChanged?.Invoke();
            }
        }
        public double AudioStartTime => mOutput.Audio.StartTime;
        public int SampleRate => mOutput.Audio.SampleRate;
        public int SampleCount => mOutput.Audio.Samples.Length;

        public IAutomationValueGetter Pitch => new PitchValueGetter(mPart);

        public IReadOnlyList<INote> Notes => mNotes;

        public IVoiceSynthesisInput Input => mInput;

        public IVoiceSynthesisOutput Output => mOutput;

        public SynthesisPiece(MidiPart part, IReadOnlyList<INote> notes)
        {
            mPart = part;
            mNotes = notes;
            mInput = new VoiceSynthesisInput(part, notes);
            mOutput = new VoiceSynthesisOutput();
            mNotes.ConstFirst().LastInSegment = null;
            mNotes.ConstLast().NextInSegment = null;
            for (int i = 0; i < mNotes.Count - 1; i++)
            {
                mNotes[i].NextInSegment = mNotes[i + 1];
            }
            for (int i = mNotes.Count - 1; i > 0; i--)
            {
                mNotes[i].LastInSegment = mNotes[i - 1];
            }
            mIsPrepared = part.AutoPrepare;

            part.Properties.Modified.Subscribe(SetDirtyAndResegment, s);
            foreach (var note in Notes)
            {
                note.Pos.Modified.Subscribe(SetDirtyAndResegment, s);
                note.Dur.Modified.Subscribe(SetDirtyAndResegment, s);
                note.Pitch.Modified.Subscribe(SetDirtyAndResegment, s);
                note.Lyric.Modified.Subscribe(SetDirtyAndResegment, s);
                note.Pronunciation.Modified.Subscribe(SetDirtyAndResegment, s);
                note.Properties.Modified.Subscribe(OnNotePropertyModified, s);
                note.Phonemes.Modified.Subscribe(OnPhonemeChanged, s);
            }
        }

        public void Prepare()
        {
            mIsPrepared = true;
        }

        public void StartSynthesis()
        {
            if (SynthesisStatus != SynthesisStatus.NotSynthesized)
                return;

            SynthesisStatus = SynthesisStatus.Synthesizing;
            if (mTask == null)
                CreateSynthesisTask();

            mTask.StartSynthesis();
        }

        public void SetDirty(string dirtyType)
        {
            mIsPrepared = mPart.AutoPrepare;
            if (SynthesisStatus == SynthesisStatus.Synthesizing)
                mTask?.StopSynthesis();
            mTask?.OnDirtyEvent(null); // TODO: 传递dirtyEvent
            SynthesisStatus = SynthesisStatus.NotSynthesized;
        }

        public void Dispose()
        {
            s.DisposeAll();

            if (SynthesisStatus == SynthesisStatus.Synthesizing)
                mTask?.StopSynthesis();
        }

        public IAudioData GetAudioData(int offset, int count)
        {
            return new MonoAudioData(GetAudioFloatArray(offset, count));
        }

        public float[] GetAudioFloatArray(int offset, int count)
        {
            return mOutput.Read(offset, count);
        }

        public void OnSampleRateChanged()
        {
            SetDirty("");
        }

        [MemberNotNull(nameof(mTask))]
        void CreateSynthesisTask()
        {
            mTask = mPart.Voice.CreateSegment(mInput, mOutput); // TODO: 传递参数
            mTask.Finished += (result) =>
            {
                if (result != null)
                {
                    context.Post(_ =>
                    {
                        mLastError = result.ToString();
                        SynthesisStatus = SynthesisStatus.SynthesisFailed;
                        Finished?.Invoke();
                    }, null);
                    return;
                }

                if (mOutput.Audio.SampleRate != AudioEngine.SampleRate.Value)
                {
                    var data = AudioUtils.Resample(mOutput.Audio.Samples, 1, mOutput.Audio.SampleRate, AudioEngine.SampleRate.Value);
                    mOutput.Audio = new MonoAudio() { StartTime = mOutput.Audio.StartTime, Samples = data, SampleRate = AudioEngine.SampleRate.Value };
                }

                context.Post(_ =>
                {
                    mWaveform = new(mOutput.Audio.Samples);
                    foreach (var note in Notes)
                    {
                        if (mOutput.SynthesizedPhonemes.TryGetValue(note, out var phonemes))
                        {
                            note.SynthesizedPhonemes = phonemes;
                        }
                        else
                        {
                            note.SynthesizedPhonemes = null;
                        }
                    }
                    SynthesisStatus = SynthesisStatus.SynthesisSucceeded;
                    Finished?.Invoke();
                }, null);
            };
            mTask.ProgressUpdated += () =>
            {
                context.Post(_ =>
                {
                    mSynthesisProgress = mTask.Progress;
                    Progress?.Invoke();
                }, null);
            };
        }

        void SetDirtyAndResegment()
        {
            Dispose();
            mPart.mSynthesisPieces.Remove(this);
            mPart.ReSegment();
        }

        void OnNotePropertyModified()
        {
            SetDirty("TODO");
        }

        void OnPhonemeChanged()
        {
            SetDirty("duration"); // TODO: 改成正确实现
        }

        static SynthesisPiece()
        {
            context = SynchronizationContext.Current!;
        }

        static SynchronizationContext context;

        MidiPart mPart;
        IReadOnlyList<INote> mNotes;
        SynthesisStatus mSynthesisStatus = SynthesisStatus.NotSynthesized;
        bool mIsPrepared;
        VoiceSynthesisInput mInput;
        VoiceSynthesisOutput mOutput;
        IVoiceSynthesisSegment mTask;
        Waveform? mWaveform = null;
        double mSynthesisProgress = 0;
        string? mLastError = null;

        DisposableManager s = new();
    }

    List<SynthesisPiece> mSynthesisPieces = new();
}
