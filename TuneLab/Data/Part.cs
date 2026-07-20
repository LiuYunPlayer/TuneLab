using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Audio;

namespace TuneLab.Data
{
    internal abstract class Part : DataObject, IPart, IReadOnlyDataObject<PartInfo>, IAudioSource, IDuration, ITimeline
    {
        public IActionEvent SelectionChanged => mSelectionChanged;
        public ITrack Track { get; set; }
        public ITempoManager TempoManager => Track.TempoManager;
        public ITimeSignatureManager TimeSignatureManager => Track.TimeSignatureManager;
        public IPart? Next => ((ILinkedNode<IPart>)this).Next;
        public IPart? Previous => ((ILinkedNode<IPart>)this).Previous;
        public abstract IDataProperty<string> Name { get; }
        public abstract IDataProperty<double> Pos { get; }
        public abstract IDataProperty<double> StartOffset { get; }
        public abstract IDataProperty<double> EndOffset { get; }
        public bool IsSelected { get => mIsSelected; set { if (mIsSelected == value) return; mIsSelected = value; mSelectionChanged.Invoke(); } }

        // 派生几何：起点/终点 = 锚点 ± 偏移；可见长度 = 两偏移之差（Dur 不再是原始可写字段，见 IPart 几何模型）。
        public double Dur => EndOffset.Value - StartOffset.Value;
        public double StartPos => Pos.Value + StartOffset.Value;
        public double EndPos => Pos.Value + EndOffset.Value;

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
        double IDuration.Duration => Dur;

        double IAudioSource.StartTime => TempoManager.GetTime(StartPos);
        int IAudioSource.SampleRate => SampleRate;
        int IAudioSource.SampleCount => SampleCount();

        IPart? ILinkedNode<IPart>.Next { get; set; }
        IPart? ILinkedNode<IPart>.Previous { get; set; }
        ILinkedList<IPart>? ILinkedNode<IPart>.LinkedList { get; set; }

        protected readonly ActionEvent mDurationChanged = new();
        readonly ActionEvent mSelectionChanged = new();

        bool mIsSelected = false;
    }
}
