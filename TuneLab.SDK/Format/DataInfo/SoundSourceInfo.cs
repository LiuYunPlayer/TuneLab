namespace TuneLab.SDK;

// part 音源的种类判别：voice（去重叠单声部歌声）或 instrument（重叠多声部音源）。
// 缺省 Voice——既有工程 / 未标种类的数据按 voice 解释（语义与旧 VoiceInfo 一致）。
public enum SourceKind
{
    Voice,
    Instrument,
}

// part 音源的序列化身份（取代旧 VoiceInfo，单字段承载种类 + 引擎身份）：
// Kind 区分 voice / instrument，Type 是引擎注册身份 id（跨包可重名），Id 是该引擎下的具体音源 id。
public class SoundSourceInfo
{
    public SourceKind Kind { get; set; } = SourceKind.Voice;
    public string Type { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}
