using System.Collections.Generic;
using System.Linq;
using System.Text;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

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
    public DataStruct<double> Preutterance { get; }
    public bool IsSelected { get => mIsSelected; set { if (mIsSelected == value) return; mIsSelected = value; mSelectionChanged.Invoke(); } }

    public double StartPos => Pos.Value;
    public double EndPos => Pos.Value + Dur.Value;

    public SynthesizedPhoneme[]? SynthesizedPhonemes { get; set; }
    public double SynthesizedPreutterance { get; set; }
    public IReadOnlyCollection<string> Pronunciations => Lyric.Pronunciations;

    IDataProperty<double> INote.Pos => Pos;
    IDataProperty<double> INote.Dur => Dur;
    IDataProperty<int> INote.Pitch => Pitch;
    IDataProperty<string> INote.Lyric => Lyric;
    IDataProperty<string> INote.Pronunciation => Pronunciation;
    IDataObjectList<IPhoneme> INote.Phonemes => Phonemes;
    IDataProperty<double> INote.Preutterance => Preutterance;

    INote? ILinkedNode<INote>.Next { get; set; } = null;
    INote? ILinkedNode<INote>.Last { get; set; } = null;
    ILinkedList<INote>? ILinkedNode<INote>.LinkedList { get; set; }

    public Note(IMidiPart part, NoteInfo info)
    {
        Pos = new(this);
        Dur = new(this);
        Pitch = new(this);
        Lyric = new(this);
        Pronunciation = new(this);
        Properties = new(this);
        Phonemes.Attach(this);
        Preutterance = new(this);
        mPart = part;
        SetInfo(info);
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
            Preutterance = Preutterance,
        };

        return info;
    }

    public void SetInfo(NoteInfo info)
    {
        using var _ = MergeNotify();
        Pos.SetInfo(info.Pos);
        Dur.SetInfo(info.Dur);
        Pitch.SetInfo(info.Pitch);
        Lyric.SetInfo(info.Lyric);
        Pronunciation.SetInfo(info.Pronunciation);
        Properties.SetInfo(info.Properties);
        Phonemes.SetInfo(info.Phonemes.Convert(Phoneme.Create).ToArray());
        Preutterance.SetInfo(info.Preutterance);
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

        public override void Set(string value)
        {
            base.Set(value);
            mNote.Phonemes.Clear();
            mNote.Pronunciation.Set(LyricUtils.GetPreferredPronunciation(Value));
        }

        readonly Note mNote;
    }

    class DataPronunciation(Note note) : DataString(note)
    {
        public override void Set(string value)
        {
            base.Set(value);
            note.Phonemes.Clear();
        }
    }

    readonly IMidiPart mPart;
    readonly ActionEvent mSelectionChanged = new();

    bool mIsSelected = false;
}
