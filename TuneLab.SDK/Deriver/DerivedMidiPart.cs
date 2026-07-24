namespace TuneLab.SDK;

// 派生 MIDI part：音符 + 音高两槽，各 null = 不产。
// Pitch 与音符分两槽：Pitch 是固定专属音高通道（落 MidiPart.Pitch）。
// v1 不含 automation 产物（可编辑 automation 与「只读参考曲线」是两种东西、需各自设计，推迟、将来纯加性补）。
public sealed class DerivedMidiPart : DerivedPart
{
    // 音符列表；时间序、按 StartTime 升序。空 = 不产音符。
    public IReadOnlyList<DerivedNote> Notes { get; init; } = [];
    // 音高曲线（具名封装，见 DerivedPitch）。Segments 空 = 不产音高——非空默认，无需外层可空。
    public DerivedPitch Pitch { get; init; } = new();
}
