using TuneLab.Foundation;

namespace TuneLab.SDK;

// instrument 声明面值视图族的一员（族的说明见 IInstrumentSynthesisPartPropertyContext.cs）。
// instrument part 数据 note 的只读值视图（声明面；满末；【无 Lyric】）。
public interface IInstrumentSynthesisNoteView
{
    // note 运行期身份（= IInstrumentSynthesisNote.Id）。
    string Id { get; }
    double StartTime { get; }
    double EndTime { get; }
    int Pitch { get; }
    PropertyObject Properties { get; }
}
