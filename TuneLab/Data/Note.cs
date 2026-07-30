using System.Collections.Generic;
using System.Linq;
using System.Text;
using TuneLab.Configs;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.Data;

internal class Note : DataObject, INote
{
    public IActionEvent SelectionChanged => mSelectionChanged;
    public IMidiPart Part => mPart;
    public INote? Next => ((ILinkedNode<INote>)this).Next;
    public INote? Previous => ((ILinkedNode<INote>)this).Previous;
    public DataStruct<double> Pos { get; }
    public DataStruct<double> Dur { get; }
    public DataStruct<int> Pitch { get; }
    DataLyric Lyric { get; }
    DataPronunciation Pronunciation { get; }
    public DataPropertyObject Properties { get; }
    // 钉死音素结构化双列表：引导（核前前置辅音）/ 主体（核 + 尾辅音）。分类即列表成员（抗抖）。
    public DataObjectList<IPhoneme> LeadingPhonemes { get; } = new();
    public DataObjectList<IPhoneme> BodyPhonemes { get; } = new();
    // 主体起点（= 两列表结合线）相对 note 头的有符号偏移：junction = noteStart + BodyOffset（左负右正）。
    public DataStruct<double> BodyOffset { get; }
    public bool IsSelected { get => mIsSelected; set { if (mIsSelected == value) return; mIsSelected = value; mSelectionChanged.Invoke(); } }

    public double StartPos => Pos.Value;
    public double EndPos => Pos.Value + Dur.Value;

    // 合成产物壳（引导/主体双列表 + BodyOffset，见 SynthesizedSyllable）；引擎回填、宿主 LockPhonemes 固化。
    public SynthesizedSyllable? SynthesizedSyllable { get; set; }
    public IReadOnlyCollection<string> Pronunciations => Lyric.Pronunciations;

    IDataProperty<double> INote.Pos => Pos;
    IDataProperty<double> INote.Dur => Dur;
    IDataProperty<int> INote.Pitch => Pitch;
    IDataProperty<string> INote.Lyric => Lyric;
    IDataProperty<string> INote.Pronunciation => Pronunciation;
    IDataObjectList<IPhoneme> INote.LeadingPhonemes => LeadingPhonemes;
    IDataObjectList<IPhoneme> INote.BodyPhonemes => BodyPhonemes;
    IDataProperty<double> INote.BodyOffset => BodyOffset;

    INote? ILinkedNode<INote>.Next { get; set; } = null;
    INote? ILinkedNode<INote>.Previous { get; set; } = null;
    ILinkedList<INote>? ILinkedNode<INote>.LinkedList { get; set; }

    public Note(IMidiPart part, NoteInfo info)
    {
        Pos = new(this);
        Dur = new(this);
        Pitch = new(this);
        Lyric = new(this);
        Pronunciation = new(this);
        Properties = new(this);
        LeadingPhonemes.Attach(this);
        BodyPhonemes.Attach(this);
        BodyOffset = new(this);
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
            LeadingPhonemes = LeadingPhonemes.GetInfo().ToInfo().ToList(),
            BodyPhonemes = BodyPhonemes.GetInfo().ToInfo().ToList(),
            BodyOffset = BodyOffset,
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
        LeadingPhonemes.SetInfo(info.LeadingPhonemes.Convert(Phoneme.Create).ToArray());
        BodyPhonemes.SetInfo(info.BodyPhonemes.Convert(Phoneme.Create).ToArray());
        BodyOffset.SetInfo(info.BodyOffset);
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
            mNote.LeadingPhonemes.Clear();
            mNote.BodyPhonemes.Clear();
            // 改歌词一律清掉旧发音覆盖（覆盖是"给这个字指定的读音"，字换了就不再成立）。
            // 编辑器层 G2P 只在开关开启时补填：拼音 / 罗马音是**一种**音系的猜测，方言等其它音系下它是错的，
            // 故关闭时把原文留给引擎自己 G2P（喂插件的口径见 INoteExtension.FinalPronunciation）。
            // 判据挂在这里而非各 UI 入口：所有写歌词的路径（录词框 / 单 note 输入 / 脚本 / agent）由此统一。
            mNote.Pronunciation.Set(Settings.AutoGeneratePronunciation.Value ? LyricUtils.GetPreferredPronunciation(Value) : string.Empty);
        }

        readonly Note mNote;
    }

    class DataPronunciation(Note note) : DataString(note)
    {
        public override void Set(string value)
        {
            base.Set(value);
            note.LeadingPhonemes.Clear();
            note.BodyPhonemes.Clear();
        }
    }

    readonly IMidiPart mPart;
    readonly ActionEvent mSelectionChanged = new();

    bool mIsSelected = false;
}
