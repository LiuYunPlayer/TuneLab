using System;
using System.Collections.Generic;
using System.Linq;
using TuneLab.Commands;
using Xunit;

namespace TuneLab.Tests;

// 授权卡片文案的封条。
//
// 【为什么值得一条，而且是遍历式的】2.1.0 开发期真出过这件事：`WriteKind` 从 9 档涨到 23 档，而卡片
// 那个内联 switch 只有 9 个 case，新增的 14 档全部落进 `_` 兜底——它们传的 Count 都是 0，于是用户
// 看到的是「Agent 想对工程应用 0 处改动」，底下三个按钮是 允许一次 / 始终允许 / 拒绝。覆盖工程文件、
// 丢弃当前文档、删预设、卸扩展、往任意路径渲染音频，全都以那句话请求同意；点「始终允许」还会把整个
// 闸门抬到 Auto。正确的措辞当时其实存在，但只在 ActionPhrase() 里——那是给**模型**看的。
//
// 逐条写用例挡不住这个（漏的那档本来就没人想到要写），故这条封条**遍历 WriteKind 的每个值**：
// 新增一档而忘了卡片文案，这里当场红。
public class AuthorizationCardTextTests
{
    static string Id(string s) => s;

    // 每一档配一个"像那么回事"的请求：文案会引用 Target / NewValue / SecondaryTarget，给空的话
    // 断言就成了空对空。
    static AuthorizationRequest Sample(WriteKind kind) => kind switch
    {
        WriteKind.ProjectEdit => new(kind, 3, null),
        WriteKind.KeybindingChange => new(kind, 0, "edit.undo", "Ctrl+Z"),
        WriteKind.ExtensionActivationChange => new(kind, 0, "com.example.voice", "disable"),
        WriteKind.ExtensionUninstall => new(kind, 0, "com.example.voice"),
        WriteKind.PresetApply => new(kind, 0, "Bright", null, "track 1 part 2"),
        WriteKind.PresetRename => new(kind, 0, "Bright", "Brighter"),
        WriteKind.RoutingChange => new(kind, 0, "voice:Foo", "com.example.voice"),
        WriteKind.ExtensionSettingChange => new(kind, 0, "apiKey", "***"),
        WriteKind.SettingChange => new(kind, 0, "AutoSaveInterval", "60"),
        _ => new(kind, 0, @"C:\songs\mix.wav", "WAV"),
    };

    [Fact]
    public void EveryWriteKindHasItsOwnSentence()
    {
        var missing = new List<string>();
        foreach (WriteKind kind in Enum.GetValues<WriteKind>())
        {
            // 结构判定而不是比对文案：有几档的正确句子与兜底逐字相同（见 Localized 的注释）。
            if (AuthorizationCardText.Localized(Sample(kind), Id) == null)
                missing.Add(kind.ToString());
        }

        Assert.True(missing.Count == 0,
            "these WriteKind values have no sentence of their own on the authorization card, so the user would be asked "
            + "to approve them without being told what they are:\n  " + string.Join("\n  ", missing));
    }

    // 每一档都要**说出它作用在什么上**。一句不带目标的话（"想跑一个动作"）等于没说，
    // 而用户正要据它点允许。
    [Fact]
    public void EverySentenceNamesWhatItActsOn()
    {
        foreach (WriteKind kind in Enum.GetValues<WriteKind>())
        {
            var request = Sample(kind);
            var text = AuthorizationCardText.For(request, Id);
            if (request.Target is not { Length: > 0 } target)
                continue;

            // 设置项与动作会被换成本地化显示名（查不到时退回键本身），故这两档只要求非空。
            if (kind is WriteKind.SettingChange or WriteKind.EditorAction or WriteKind.EditorActionDestructive)
            {
                Assert.NotEmpty(text);
                continue;
            }

            Assert.True(text.Contains(target, StringComparison.Ordinal),
                kind + " does not name its target on the card: " + text);
        }
    }

    // 会替换掉已有文件的三档，必须把"替换"说出来——那是用户唯一能据此喊停的信息。
    [Theory]
    [InlineData(WriteKind.ProjectExportOverwrite)]
    [InlineData(WriteKind.ProjectSaveAsOverwrite)]
    [InlineData(WriteKind.ProjectExportAudioOverwrite)]
    internal void OverwritingSaysAFileWillBeReplaced(WriteKind kind)
    {
        var text = AuthorizationCardText.For(Sample(kind), Id);
        Assert.Contains("already exists there and will be replaced", text);
        Assert.Contains("can't be undone", text);
    }

    // 保存与导出的分野要在卡片上：export 只写一份副本，save 是真的保存。
    [Fact]
    public void SavingSaysItIsARealSaveAndSaveAsSaysThePathMoves()
    {
        Assert.Contains("really saves", AuthorizationCardText.For(Sample(WriteKind.ProjectSave), Id));
        Assert.Contains("that is the file the project saves to", AuthorizationCardText.For(Sample(WriteKind.ProjectSaveAs), Id));
    }

    // 渲染音频要占住机器几分钟，那句代价必须在卡片上——用户是据它决定现在放不放行的。
    [Fact]
    public void RenderingAudioSaysItTakesMinutesAndLocksTheApp()
    {
        var text = AuthorizationCardText.For(Sample(WriteKind.ProjectExportAudio), Id);
        Assert.Contains("several minutes", text);
        Assert.Contains("unusable until it finishes", text);
    }

    // 换工程要说清丢的是什么（当前文档 + 它的撤销历史）。
    [Fact]
    public void OpeningAnotherProjectSaysTheCurrentOneIsClosed()
    {
        var text = AuthorizationCardText.For(Sample(WriteKind.ProjectOpen), Id);
        Assert.Contains("closing the one you have open now", text);
        Assert.Contains("undo history", text);
    }

    // 兜底那句本身也要是【正确】的（万一将来真有一档漏了，用户看到的至少是实话，只是英文）。
    [Fact]
    public void TheFallbackIsAtLeastTrueEvenThoughItIsNotLocalized()
    {
        var request = Sample(WriteKind.ProjectSaveAsOverwrite);
        var fallback = AuthorizationCardText.Fallback(request);

        Assert.StartsWith("The agent wants to ", fallback);
        Assert.Contains(request.Target!, fallback);
        Assert.DoesNotContain("0 change(s)", fallback);
    }
}
