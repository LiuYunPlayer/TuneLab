using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Audio;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using TuneLab.Data;
using TuneLab.Extensions.Voices;

namespace TuneLab.Utils
{
    internal class XvsSynthesisTask : ISynthesisTask
    {
        public XvsSynthesisTask(IVoice voice1,IVoice voice2, ISynthesisPiece piece)
        {
            mData = piece;
            mBaseVoice = voice1;
            mSecondVoice = (voice2 is Voice v2 && !v2.isEmptyVoice) ? voice2 : null;
            InitTask();
        }

        public event Action<SynthesisResult>? Complete;
        public event Action<double>? Progress;
        public event Action<string>? Error;

        enum SynthesisTrack
        {
            baseTrack = 0,
            secondTrack,
            defaultTrack
        }
        class SynthesisState
        {
            public SynthesisResult? result = null;
            public double progress = 0;
            public string? error = null;
        }

        Tuple<double[], double[]> GetXVSTuple(ISynthesisData data, IAutomationValueGetter automation)
        {
            double st = data.Notes.First().StartTime;
            double et = data.Notes.Last().EndTime;
            double coe120 = 1 / 960.0d;
            List<double> times = new List<double>();
            for (double i = st; i <= et; i = i + coe120) times.Add(i);
            double[] xseValue = automation.GetValue(times.ToArray());
            return new Tuple<double[], double[]>(times.ToArray(), xseValue);
        }
        ISynthesisData prepareData(ISynthesisPiece piece, SynthesisTrack track)
        {
            if (track == SynthesisTrack.defaultTrack) return piece;
            XVSSynthesisPiece pData = new XVSSynthesisPiece(piece);
            OrderedMap<ISynthesisNote, ISynthesisNote> newMap = new OrderedMap<ISynthesisNote, ISynthesisNote>();
            foreach(var kv in pData.NoteMap)
            {
                ISynthesisNote newNote = kv.Key;
                if (newNote is Note oldNote)
                {
                    var noteInfo = oldNote.GetInfo();
                    int inS = noteInfo.Lyric.LastIndexOf("<");
                    int inE = noteInfo.Lyric.LastIndexOf(">");
                    if (inS > 1 && inE > inS + 1)
                    {
                        string lyric1 = noteInfo.Lyric.Substring(0, inS);
                        string lyric2 = noteInfo.Lyric.Substring(inS + 1, inE - inS - 1);
                        if (track == SynthesisTrack.baseTrack)
                        {
                            noteInfo.Lyric = lyric1;
                            newNote = new Note(oldNote.Part, noteInfo);
                        }
                        else if (track == SynthesisTrack.secondTrack)
                        {
                            noteInfo.Lyric = lyric2;
                            newNote = new Note(oldNote.Part, noteInfo);
                        }
                    }
                }
                newMap.Add(newNote,kv.Value);
            }
            pData.NoteMap = newMap;
            return pData;
        }

        void InitTask()
        {
            if (!mData.GetAutomation(ConstantDefine.XvsLineID, out var crossSynthAutomation))
            {
                crossSynthAutomation = null;
            }
            if (mSecondVoice == null || crossSynthAutomation == null)
            {
                mBaseSynthTask = mBaseVoice.CreateSynthesisTask(prepareData(mData,SynthesisTrack.defaultTrack));
                mBaseSynthTask.Progress += (e) => { Progress?.Invoke(e); };
                mBaseSynthTask.Complete += (e) => { Complete?.Invoke(e); };
                mBaseSynthTask.Error += (e) => { Error?.Invoke(e); };
            }
            else
            {
                var mXseTuple = GetXVSTuple(mData, crossSynthAutomation);
                if (mXseTuple.Item2.Length == 0 ||
                   (mXseTuple.Item2.Length > 0 && mXseTuple.Item2.Where(p => p != 0).Count() == 0)
                   )
                {
                    var data = prepareData(mData, SynthesisTrack.baseTrack);
                    mBaseSynthTask = mBaseVoice.CreateSynthesisTask(data);
                    mBaseSynthTask.Progress += (e) => { Progress?.Invoke(e); };
                    mBaseSynthTask.Complete += (e) => { SynthTask_Complete(data, e); };
                    mBaseSynthTask.Error += (e) => { Error?.Invoke(e); };
                }
                else if (mXseTuple.Item2.Length > 0 && mXseTuple.Item2.Where(p => p != 1).Count() == 0)
                {
                    var data = prepareData(mData, SynthesisTrack.secondTrack);
                    mBaseSynthTask = mSecondVoice.CreateSynthesisTask(data);
                    mBaseSynthTask.Progress += (e) => { Progress?.Invoke(e); };
                    mBaseSynthTask.Complete += (e) => { SynthTask_Complete(data,e); };
                    mBaseSynthTask.Error += (e) => { Error?.Invoke(e); };
                }
                else
                {
                    var baseData = prepareData(mData, SynthesisTrack.baseTrack);
                    mBaseSynthTask = mBaseVoice.CreateSynthesisTask(baseData);
                    mBaseSynthTask.Progress += (e) => { SynthTask_Progress(SynthesisTrack.baseTrack, e); };
                    mBaseSynthTask.Complete += (e) => { SynthTask_Complete(SynthesisTrack.baseTrack, baseData, e, crossSynthAutomation); };
                    mBaseSynthTask.Error += (e) => { SynthTask_Error(SynthesisTrack.baseTrack, e); };

                    var secondData = prepareData(mData, SynthesisTrack.secondTrack);
                    mSecondSynthTask = mSecondVoice.CreateSynthesisTask(secondData);
                    mSecondSynthTask.Progress += (e) => { SynthTask_Progress(SynthesisTrack.secondTrack, e); };
                    mSecondSynthTask.Complete += (e) => { SynthTask_Complete(SynthesisTrack.secondTrack, baseData, e, crossSynthAutomation); };
                    mSecondSynthTask.Error += (e) => { SynthTask_Error(SynthesisTrack.secondTrack, e); };
                }
            }
        }
        private void SynthTask_Error(SynthesisTrack track, string obj)
        {
            if (track == SynthesisTrack.secondTrack) mSecondState.error = obj; else mBaseState.error = obj;
            Stop();
            Error?.Invoke(obj);
        }

        private void SynthTask_Complete(ISynthesisData data, SynthesisResult obj)
        {
            if (data is XVSSynthesisPiece pData)
            {
                Dictionary<ISynthesisNote, SynthesizedPhoneme[]> map = new Dictionary<ISynthesisNote, SynthesizedPhoneme[]>();
                foreach(var kv in obj.SynthesizedPhonemes)
                {
                    map.Add(pData.NoteMap[kv.Key], kv.Value);
                }
                SynthesisResult CrossedResult = new SynthesisResult(
                    obj.StartTime,
                    obj.SamplingRate,
                    obj.AudioData,
                    obj.SynthesizedPitch,
                    map
                    );
                Complete?.Invoke(CrossedResult);
            }
            else
                Complete?.Invoke(obj);
        }
        private void SynthTask_Complete(SynthesisTrack track, ISynthesisData data, SynthesisResult obj, IAutomationValueGetter crossSynthAutomation)
        {
            if (track == SynthesisTrack.secondTrack) mSecondState.result = obj; else mBaseState.result = obj;
            if (mSecondState.result != null && mBaseState.result != null)
            {
                //Done
                SynthesisResult BaseResult = mBaseState.result;
                SynthesisResult SecondResult = mSecondState.result;
                int basePtr = 0; int secondPtr = 0;

                if (BaseResult.StartTime > SecondResult.StartTime)
                {
                    var secondTime = SecondResult.StartTime;
                    while (secondTime < BaseResult.StartTime)
                    {
                        secondPtr++;
                        secondTime = SecondResult.StartTime + (double)secondPtr / SecondResult.SamplingRate;
                    }
                }
                else if (BaseResult.StartTime < SecondResult.StartTime)
                {
                    var baseTime = BaseResult.StartTime;
                    while (baseTime < SecondResult.StartTime)
                    {
                        basePtr++;
                        baseTime = BaseResult.StartTime + (double)basePtr / BaseResult.SamplingRate;
                    }
                }

                double timePtr = BaseResult.StartTime + (double)basePtr / BaseResult.SamplingRate;

                float[] audioData = BaseResult.AudioData.ToArray();
                while (basePtr < BaseResult.AudioData.Count() && secondPtr < SecondResult.AudioData.Count())
                {
                    var xse = crossSynthAutomation.GetValue([timePtr])[0];
                    audioData[basePtr] = (float)(SecondResult.AudioData[secondPtr] * xse + BaseResult.AudioData[basePtr] * (1.0f - xse));

                    basePtr++;
                    secondPtr++;
                    timePtr = BaseResult.StartTime + (double)basePtr / BaseResult.SamplingRate;
                }
                Dictionary<ISynthesisNote, SynthesizedPhoneme[]> map;
                map = BaseResult.SynthesizedPhonemes.ToDictionary();

                if (data is XVSSynthesisPiece pData)
                {
                    map = new Dictionary<ISynthesisNote, SynthesizedPhoneme[]>();
                    foreach (var kv in BaseResult.SynthesizedPhonemes)
                    {
                        map.Add(pData.NoteMap[kv.Key], kv.Value);
                    }
                }
                else
                    map = BaseResult.SynthesizedPhonemes.ToDictionary();
                SynthesisResult CrossedResult = new SynthesisResult(
                    BaseResult.StartTime,
                    BaseResult.SamplingRate,
                    audioData,
                    BaseResult.SynthesizedPitch,
                    map
                    );
                Complete?.Invoke(CrossedResult);
            }
        }

        private void SynthTask_Progress(SynthesisTrack track, double obj)
        {
            if (track == SynthesisTrack.secondTrack) mSecondState.progress = obj; else mBaseState.progress = obj;
            Progress?.Invoke(mBaseState.progress * 0.5 + mSecondState.progress * 0.5);
        }

        public void Resume()
        {
            if (mBaseSynthTask != null) mBaseSynthTask.Resume();
            if (mSecondSynthTask != null) mSecondSynthTask.Resume();
        }

        public void SetDirty(string dirtyType)
        {
            if (mBaseSynthTask != null) mBaseSynthTask.SetDirty(dirtyType);
            if (mSecondSynthTask != null) mSecondSynthTask.SetDirty(dirtyType);
        }

        public void Start()
        {
            if (mBaseSynthTask != null) mBaseSynthTask.Start();
            if (mSecondSynthTask != null) mSecondSynthTask.Start();
        }

        public void Stop()
        {
            if (mBaseSynthTask != null) mBaseSynthTask.Stop();
            if (mSecondSynthTask != null) mSecondSynthTask.Stop();
        }

        public void Suspend()
        {
            if (mBaseSynthTask != null) mBaseSynthTask.Suspend();
            if (mSecondSynthTask != null) mSecondSynthTask.Suspend();
        }


        ISynthesisPiece mData;
        IVoice mBaseVoice;
        IVoice mSecondVoice;

        ISynthesisTask? mBaseSynthTask;
        ISynthesisTask? mSecondSynthTask;

        SynthesisState mBaseState = new();
        SynthesisState mSecondState = new();
    }

    class XVSSynthesisPiece : ISynthesisData
    {
        ISynthesisData mData;
        public XVSSynthesisPiece(ISynthesisPiece piece)
        {
            mData = piece;
            foreach(var note in piece.Notes)
            {
                NoteMap.Add(note, note);
            }
        }
        public IEnumerable<ISynthesisNote> Notes
        {
            get { return NoteMap.Keys; }
        }

        public OrderedMap<ISynthesisNote,ISynthesisNote> NoteMap = new OrderedMap<ISynthesisNote, ISynthesisNote>();

        public PropertyObject PartProperties => mData.PartProperties;

        public IAutomationValueGetter Pitch => mData.Pitch;

        public bool GetAutomation(string automationID, [MaybeNullWhen(false), NotNullWhen(true)] out IAutomationValueGetter? automation)
        {
            return mData.GetAutomation(automationID, out automation);
        }
    }
}
