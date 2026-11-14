using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Commands;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// CanvasConfig（插件自绘画布）的宿主侧封条：值字段但没有默认值（没设过 = 不存值、恢复默认 = 删掉存值，插件自己补默认值），
// 工厂不在声明时调用，命令面只读、不替插件解读值。画布的渲染、事件与写回需要窗口，不在此测。
public class CanvasConfigTests
{
    sealed class NullControl : ICanvasControl { }

    // 只记 Begin / End 的调用次数，验 EditScope 的配对语义。
    sealed class CountingHost : ICanvasHost
    {
        public int Begins, Ends;
        public double Width => 0;
        public double Height => 0;
        public double DevicePixelRatio => 1;
        public Rect VisibleBounds => default;
        public bool IsBackground => false;
        public void Invalidate() { }
        public void BeginEdit() => Begins++;
        public void EndEdit() => Ends++;
        public bool SetValue(PropertyValue value) => true;
        public void CommitValue() { }
        public void DiscardValue() { }
        public Task<string?> PopupTextBox(string text, Rect bounds, CanvasTextStyle? style = null, CanvasTextBoxOptions? options = null)
            => Task.FromResult<string?>(null);
        public Task<PropertyValue?> PopupMenu(Rect anchor, IReadOnlyList<ComboBoxItem> items)
            => Task.FromResult<PropertyValue?>(null);
        public IDialogService DialogService => throw new NotSupportedException();
    }

    [Fact]
    public void Create_ValidatesArguments()
    {
        Assert.Throws<ArgumentNullException>(() => CanvasConfig.Create(240, 80, null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasConfig.Create(240, 0, _ => new NullControl()));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasConfig.Create(240, double.NaN, _ => new NullControl()));
        // 首选宽度眼下没有位置取用，但同样是契约：一样要求是正的有限数。
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasConfig.Create(0, 80, _ => new NullControl()));
        Assert.Throws<ArgumentOutOfRangeException>(() => CanvasConfig.Create(double.PositiveInfinity, 80, _ => new NullControl()));
    }

    // 宿主不显示画布的值，用不着默认值：两种画布都不是 IValueConfig（不进"叶子自带默认值"那套递归）。
    [Fact]
    public void Canvases_HaveNoDefaultValue()
    {
        Assert.IsNotAssignableFrom<IValueConfig>(CanvasConfig.Create(240, 80, _ => new NullControl()));
        Assert.IsNotAssignableFrom<IValueConfig>(TimelineCanvasConfig.Create(_ => new NullControl()));
    }

    // 颜色默认全透明 = 不指定（宿主分配，同 AutomationConfig）。
    [Fact]
    public void TimelineCanvas_ColorUnassignedByDefault()
    {
        Assert.Throws<ArgumentNullException>(() => TimelineCanvasConfig.Create(null!));
        var config = TimelineCanvasConfig.Create(_ => new NullControl());
        Assert.True(TuneLab.Configs.TrackColors.IsUnassigned(config.Color));
        Assert.Equal("#F1595B", config.WithColor("#F1595B").Color);
        Assert.True(TuneLab.Configs.TrackColors.IsUnassigned(config.Color));   // With 不改原对象
    }

    [Fact]
    public void Create_DoesNotCallTheFactory()
    {
        int calls = 0;
        var config = CanvasConfig.Create(240, 80, _ => { calls++; return new NullControl(); });
        _ = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig> { { "curve", config } }).GetDefaultValue();
        Assert.Equal(0, calls);   // 声明（含 agent / CLI 只读声明）不建控件
    }

    [Fact]
    public void WithSummary_ReturnsACopy()
    {
        var plain = CanvasConfig.Create(240, 80, _ => new NullControl());
        var summarized = plain.WithSummary("Linear");
        Assert.Null(plain.Summary);
        Assert.Equal("Linear", summarized.Summary);
        Assert.Equal(plain.PreferredWidth, summarized.PreferredWidth);
        Assert.Equal(plain.PreferredHeight, summarized.PreferredHeight);
        Assert.Same(plain.CreateControl, summarized.CreateControl);
    }

    // 对象的默认值里不占键（没设过 = 不存）；数组 / 列表里的画布元素位置上是 Null（位置必须有值）。
    [Fact]
    public void DefaultValueOfAContainer_LeavesTheCanvasUnset()
    {
        var config = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
        {
            { "curve", CanvasConfig.Create(240, 80, _ => new NullControl()) },
            { "lane", TimelineCanvasConfig.Create(_ => new NullControl()) },
            { "takes", ArrayConfig.Create([TimelineCanvasConfig.Create(_ => new NullControl())]) },
        });

        Assert.True(config.GetDefaultValue().ToObject(out var value));
        Assert.False(value.Map.ContainsKey("curve"));
        Assert.False(value.Map.ContainsKey("lane"));
        Assert.True(value.Map["takes"].ToArray(out var takes));
        Assert.True(takes[0].IsNull());
    }

    [Fact]
    public void CommandSurface_PointsToTheIntroduction_AndTakesAnyJsonAsIs()
    {
        var canvas = CanvasConfig.Create(240, 80, _ => new NullControl());
        var text = ConfigText.Describe(canvas);
        Assert.Contains("canvas", text);
        Assert.Contains("introduction", text);

        // 宿主不知道形状，也就不校验：嵌套的数组 / 对象原样收下。
        using var raw = JsonDocument.Parse("{\"points\": [0, 0.5, 1], \"mode\": \"smooth\"}");
        var (value, error) = ExtensionSettingsText.Normalize(canvas, raw.RootElement, "curve");
        Assert.Null(error);
        Assert.True(value.ToObject(out var obj));
        Assert.True(obj.Map["points"].ToArray(out var points));
        Assert.Equal(3, points.Count);
        Assert.Equal("{\"points\": [0, 0.5, 1], \"mode\": \"smooth\"}", ConfigText.FormatValue(value));
        Assert.Equal(raw.RootElement.GetRawText().Replace(" ", ""), ConfigText.ToJson(value)!.ToJsonString());

        // 画布没有默认值：null = 没设过，写 null 就是清掉。
        using var none = JsonDocument.Parse("null");
        var cleared = ExtensionSettingsText.Normalize(canvas, none.RootElement, "curve");
        Assert.Null(cleared.Error);
        Assert.True(cleared.Value.IsNull());
    }

    // 一个插件控件只实现 Render：其余成员全走默认体（空），编译即证明 Render 也不是必须实现的。
    [Fact]
    public void Control_AllMembersHaveDefaults()
    {
        ICanvasControl control = new NullControl();
        control.Render(null!);
        control.OnScroll(null!);
    }

    // EditScope = BeginEdit + 句柄：Dispose 即 EndEdit，只生效一次（插件重复 Dispose 不会多减一次、误解别的操作的锁）。
    [Fact]
    public void EditScope_PairsBeginWithExactlyOneEnd()
    {
        var host = new CountingHost();
        var scope = host.EditScope();
        Assert.Equal((1, 0), (host.Begins, host.Ends));

        scope.Dispose();
        scope.Dispose();
        Assert.Equal((1, 1), (host.Begins, host.Ends));
    }
}
