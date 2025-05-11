using TuneLab.Audio;
using TuneLab.Core.DataInfo;
using TuneLab.Foundation.DataStructures;
using TuneLab.Foundation.Document;
using TuneLab.Foundation.Event;

namespace TuneLab.Data
{
    internal abstract class Part : DataObject, IPart, IReadOnlyDataObject<PartInfo>, IAudioSource, IDuration, ITimeline
    {
        public IActionEvent SelectionChanged => mSelectionChanged;
        public ITrack Track { get; set; }
        public ITempoManager TempoManager => Track.TempoManager;
        public ITimeSignatureManager TimeSignatureManager => Track.TimeSignatureManager;
        public IPart? Next => ((ILinkedNode<IPart>)this).Next;
        public IPart? Last => ((ILinkedNode<IPart>)this).Last;
        public abstract IDataProperty<string> Name { get; }
        public abstract IDataProperty<double> Pos { get; }
        public abstract IDataProperty<double> Dur { get; }
        public bool IsSelected { get => mIsSelected; set { if (mIsSelected == value) return; mIsSelected = value; mSelectionChanged.Invoke(); } }

        public double StartPos => Pos.Value;
        public double EndPos => Pos.Value + Dur.Value;

        public Part(ITrack track)
        {
            Track = track;
        }

        public abstract PartInfo GetInfo();
        protected abstract int SampleRate { get; }
        public abstract IAudioData GetAudioData(int offset, int count);
        public abstract void OnSampleRateChanged();
        protected virtual int SampleCount()
        {
            return (int)(((IAudioSource)this).SampleRate * (TempoManager.GetTime(EndPos) - TempoManager.GetTime(StartPos)));
        }

        public virtual void Activate() { }
        public virtual void Deactivate() { }

        IActionEvent IDuration.DurationChanged => mDurationChanged;
        double IDuration.Duration => Dur.Value;

        double IAudioSource.StartTime => TempoManager.GetTime(StartPos);
        int IAudioSource.SampleRate => SampleRate;
        int IAudioSource.SampleCount => SampleCount();

        IPart? ILinkedNode<IPart>.Next { get; set; }
        IPart? ILinkedNode<IPart>.Last { get; set; }
        ILinkedList<IPart>? ILinkedNode<IPart>.LinkedList { get; set; }

        protected readonly ActionEvent mDurationChanged = new();
        readonly ActionEvent mSelectionChanged = new();

        bool mIsSelected = false;
    }
}
