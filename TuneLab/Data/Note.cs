using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TuneLab.Base.Data;
using TuneLab.Base.Event;
using TuneLab.Base.Properties;
using TuneLab.Base.Structures;
using TuneLab.Utils;
using TuneLab.Extensions.Formats.DataInfo;
using TuneLab.Extensions.Voices;
using TuneLab.Base.Utils;

namespace TuneLab.Data;

internal class Note : DataObject, INote
{
    public IActionEvent SelectionChanged => mSelectionChanged;
    public IMidiPart Part => mPart;
    public INote? Next => ((ILinkedNode<INote>)this).Next;
    public INote? Last => ((ILinkedNode<INote>)this).Last;
    public DataStruct<double> Pos { get; }
    public DataStruct<double> Dur { get; }
    public DataStruct<int> Pitch { get; }
    DataLyric Lyric { get; }
    DataPronunciation Pronunciation { get; }
    public DataPropertyObject Properties { get; }
    public DataObjectList<IPhoneme> Phonemes { get; } = new();
    public bool IsSelected { get => mIsSelected; set { if (mIsSelected == value) return; mIsSelected = value; mSelectionChanged.Invoke(); } }

    public double StartPos => Pos.Value;
    public double EndPos => Pos.Value + Dur.Value;

    public SynthesizedPhoneme[]? SynthesizedPhonemes { get; set; }
    public IReadOnlyCollection<string> Pronunciations => Lyric.Pronunciations;

    IDataProperty<double> INote.Pos => Pos;
    IDataProperty<double> INote.Dur => Dur;
    IDataProperty<int> INote.Pitch => Pitch;
    IDataProperty<string> INote.Lyric => Lyric;
    IDataProperty<string> INote.Pronunciation => Pronunciation;
    IDataObjectList<IPhoneme> INote.Phonemes => Phonemes;

    INote? ILinkedNode<INote>.Next { get; set; } = null;
    INote? ILinkedNode<INote>.Last { get; set; } = null;
    ILinkedList<INote>? ILinkedNode<INote>.LinkedList { get; set; }
    public INote? NextInSegment { get; set; } = null;
    public INote? LastInSegment { get; set; } = null;

    public Note(IMidiPart part, NoteInfo info)
    {
        Pos = new(this);
        Dur = new(this);
        Pitch = new(this);
        Lyric = new(this);
        Pronunciation = new(this);
        Properties = new(this);
        Phonemes.Attach(this);
        mPart = part;
        IDataObject<NoteInfo>.SetInfo(this, info);
    }

    public NoteInfo GetInfo()
    {
        var info = new NoteInfo()
        {
            Pos = Pos,
            Dur = Dur,
            Pitch = Pitch,
            Lyric = Lyric,
            Pronunciation = Pronunciation,
            Properties = Properties.GetInfo(),
            Phonemes = Phonemes.GetInfo().ToInfo(),
        };

        return info;
    }

    void IDataObject<NoteInfo>.SetInfo(NoteInfo info)
    {
        IDataObject<NoteInfo>.SetInfo(Pos, info.Pos);
        IDataObject<NoteInfo>.SetInfo(Dur, info.Dur);
        IDataObject<NoteInfo>.SetInfo(Pitch, info.Pitch);
        IDataObject<NoteInfo>.SetInfo(Lyric, info.Lyric);
        IDataObject<NoteInfo>.SetInfo(Pronunciation, info.Pronunciation);
        IDataObject<NoteInfo>.SetInfo(Properties, info.Properties);
        IDataObject<NoteInfo>.SetInfo(Phonemes, info.Phonemes.Convert(Phoneme.Create).ToArray());
    }

    class DataLyric : DataString
    {
        public IReadOnlyCollection<string> Pronunciations { get; private set; } = [];

        public DataLyric(Note note) : base(note)
        {
            mNote = note;
            Modified.Subscribe(() =>
            {
                Pronunciations = LyricUtils.GetPronunciations(Value);
            });
        }

        protected override void Set(string value)
        {
            base.Set(value);
            mNote.Phonemes.Clear();
            mNote.Pronunciation.Set(string.Empty);
        }

        Note mNote;
    }

    class DataPronunciation(Note note) : DataString(note)
    {
        protected override void Set(string value)
        {
            base.Set(value);
            note.Phonemes.Clear();
        }
    }

    readonly IMidiPart mPart;
    readonly ActionEvent mSelectionChanged = new();

    bool mIsSelected = false;
}
