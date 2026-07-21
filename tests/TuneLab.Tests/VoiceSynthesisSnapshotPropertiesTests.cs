using TuneLab.Foundation;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// SDK 冻结面回归保护：合成期「读音素/note 自定义属性」的文档正路——VoiceSynthesisNoteSnapshot.Properties 与
// VoiceSynthesisPhonemeSnapshot.Properties（引擎经 GetXxxPropertyConfigs 声明、快照冻结其值，合成时用 GetDouble
// 等按 key 读、未设回退默认）。docs 强调此路，但样例插件只读快照几何字段、从不行使属性读——冻结面零覆盖，此处补上。
public class VoiceSynthesisSnapshotPropertiesTests
{
    static PropertyObject Obj(params (string key, PropertyValue value)[] entries)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var (key, value) in entries)
            map.Add(key, value);
        return new PropertyObject(map);
    }

    [Fact]
    public void NoteSnapshot_CarriesProperties_ReadViaTypedGetter()
    {
        var snapshot = new VoiceSynthesisNoteSnapshot
        {
            StartTime = 0,
            EndTime = 1,
            Pitch = 60,
            Lyric = "a",
            LeadingPhonemes = [],
            BodyPhonemes = [],
            Properties = Obj(("tension", 0.7), ("breathy", PropertyValue.Create(true))),
        };

        Assert.Equal(0.7, snapshot.Properties.GetDouble("tension", 0));
        Assert.True(snapshot.Properties.GetBoolean("breathy"));
        Assert.Equal(-1.0, snapshot.Properties.GetDouble("missing", -1.0));  // 未设 → 回退默认
    }

    [Fact]
    public void PhonemeSnapshot_CarriesGeometryAndProperties()
    {
        var phoneme = new VoiceSynthesisPhonemeSnapshot("i", 120, 1.5, Obj(("stress", 0.9)));

        Assert.Equal("i", phoneme.Symbol);
        Assert.Equal(120, phoneme.Duration);
        Assert.Equal(1.5, phoneme.StretchWeight);
        Assert.Equal(0.9, phoneme.Properties.GetDouble("stress", 0));
    }

    [Fact]
    public void PhonemeSnapshot_EmptyProperties_FallsBackToDefault()
    {
        // 非钉死（引擎 G2P）音素无属性：Properties = Empty，读不到回退声明默认。
        var phoneme = new VoiceSynthesisPhonemeSnapshot("a", 80, 0, PropertyObject.Empty);
        Assert.Equal(-1.0, phoneme.Properties.GetDouble("stress", -1.0));
    }
}
