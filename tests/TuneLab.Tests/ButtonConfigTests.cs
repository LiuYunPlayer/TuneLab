using System;
using System.Text.Json;
using System.Threading.Tasks;
using TuneLab.Commands;
using TuneLab.Extensions;
using TuneLab.Foundation;
using TuneLab.GUI.Controllers;
using TuneLab.SDK;
using Xunit;

namespace TuneLab.Tests;

// ButtonConfig（config 族里第一个没有值的成员）与按钮点击期弹窗服务的宿主侧封条：
// 按钮不占值（默认值递归 / 命令面都跳过它）、点击调度防重入且结束后通知面板、
// 弹窗服务只在点击期间有效且无窗口时按约降级。GUI 渲染与真弹窗需要窗口，不在此测。
[Collection("ButtonActions")]   // ButtonActions.Invoke 是进程级静态钩子，改它的用例不能并行
public class ButtonConfigTests
{
    static ButtonConfig Button(string text = "Run") => ButtonConfig.Create(text, _ => Task.CompletedTask);

    // —— 按钮没有值 ——

    [Fact]
    public void DefaultValueOfAnObject_SkipsButtons()
    {
        var config = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
        {
            { "gain", SliderConfig.Linear(0.5, 0, 1) },
            { "clear", Button() },
        });

        Assert.True(config.GetDefaultValue().ToObject(out var value));
        Assert.True(value.Map.ContainsKey("gain"));
        Assert.False(value.Map.ContainsKey("clear"));   // 按钮不占键：恢复默认 / preset 不会往数据里写出一个空值
    }

    [Fact]
    public void CommandSurface_DescribesButtonAsUnsettable_AndRefusesToSetIt()
    {
        var button = Button("Clear Cache");
        var text = ConfigText.Describe(button);
        Assert.Contains("button", text);
        Assert.Contains("Clear Cache", text);
        Assert.Contains("can't be set", text);

        using var raw = JsonDocument.Parse("true");
        var (_, error) = ExtensionSettingsText.Normalize(button, raw.RootElement, "clear");
        Assert.NotNull(error);
        Assert.Contains("button", error);
    }

    [Fact]
    public void Create_RejectsNullHandler()
    {
        Assert.Throws<ArgumentNullException>(() => ButtonConfig.Create("x", null!));
    }

    // —— 点击调度 ——

    [Fact]
    public void Click_IsNotReentrant_AndNotifiesThePanelWhenDone()
    {
        var previous = ButtonActions.Invoke;
        var gate = new TaskCompletionSource();
        int invoked = 0;
        ButtonActions.Invoke = (_, _) => { invoked++; return gate.Task; };
        try
        {
            var root = new ButtonRoot { Scope = "test/" + Guid.NewGuid() };
            int completed = 0;
            root.ActionCompleted += () => completed++;
            var key = root.Scope + "/clear";

            ButtonActions.Click(key, Button(), root, null);
            Assert.True(ButtonActions.IsRunning(key));
            ButtonActions.Click(key, Button(), root, null);    // 执行中再点：不重入
            Assert.Equal(1, invoked);
            Assert.Equal(0, completed);

            gate.SetResult();                                  // 续体在本线程同步跑完（无同步上下文）
            Assert.False(ButtonActions.IsRunning(key));
            Assert.Equal(1, completed);

            ButtonActions.Click(key, Button(), root, null);    // 结束后可以再点
            Assert.Equal(2, invoked);
        }
        finally
        {
            ButtonActions.Invoke = previous;
        }
    }

    // —— 宿主侧执行与弹窗服务 ——

    [Fact]
    public async Task PluginInvoker_SwallowsHandlerFailures()
    {
        // 无窗口：错误提示降级为日志，调用本身不抛——按钮失败绝不能把异常甩进 UI 事件循环。
        await PluginButtonInvoker.Invoke(ButtonConfig.Create("x", _ => throw new InvalidOperationException("boom")), null);
        await PluginButtonInvoker.Invoke(ButtonConfig.Create("x", async _ => { await Task.Yield(); throw new InvalidOperationException("boom"); }), null);
    }

    [Fact]
    public async Task Dialogs_WithoutAWindow_DegradeByContract()
    {
        bool confirmed = true, ran = false;
        PropertyObject? inputs = PropertyObject.Empty;
        await PluginButtonInvoker.Invoke(ButtonConfig.Create("x", async click =>
        {
            await click.DialogService.ShowMessage("t", "m");
            confirmed = await click.DialogService.Confirm("t", "m");
            inputs = await click.DialogService.PromptInputs("t", ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig> { { "n", SliderConfig.Integer(3, 0, 9) } }));
            await click.DialogService.RunWithProgress("t", (progress, token) => { progress.Report(0.5); progress.Report(null); ran = true; return Task.CompletedTask; });
        }), null);

        Assert.False(confirmed);   // 确认：无窗口即"没同意"
        Assert.Null(inputs);       // 输入：无窗口即"取消"
        Assert.True(ran);          // 进度：不显示界面、照样执行 work
    }

    [Fact]
    public void PromptInputs_HandsBackCompleteValues()
    {
        // 表单交回稀疏值（只含改过的键）；插件拿到的要是完整值：没改的字段落声明的默认值，嵌套对象逐层叠加。
        var config = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
        {
            { "count", SliderConfig.Integer(3, 0, 9) },
            { "name", TextBoxConfig.Create("default") },
            { "advanced", ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
                {
                    { "verbose", CheckBoxConfig.Create(false) },
                    { "level", SliderConfig.Integer(1, 0, 5) },
                }) },
            { "run", Button() },
        });
        var sparse = new PropertyObject(new Map<string, PropertyValue>
        {
            { "count", PropertyValue.Create(7.0) },
            { "advanced", PropertyValue.Create(new PropertyObject(new Map<string, PropertyValue> { { "verbose", PropertyValue.Create(true) } })) },
        });

        var full = HostDialogService.Complete(config, sparse);
        Assert.Equal(7, full.GetDouble("count", -1));
        Assert.Equal("default", full.GetString("name", "?"));
        Assert.True(full.Map["advanced"].ToObject(out var advanced));
        Assert.True(advanced.GetBoolean("verbose", false));
        Assert.Equal(1, advanced.GetDouble("level", -1));
        Assert.False(full.Map.ContainsKey("run"));   // 按钮不占值
    }

    [Fact]
    public async Task Dialogs_AreUnusableAfterTheClickFinishes()
    {
        IDialogService? kept = null;
        await PluginButtonInvoker.Invoke(ButtonConfig.Create("x", click => { kept = click.DialogService; return Task.CompletedTask; }), null);

        // 插件把服务存起来、点击结束后再用：直接抛，让它在开发期就炸响而不是静默失效。
        Assert.NotNull(kept);
        await Assert.ThrowsAsync<InvalidOperationException>(() => kept!.ShowMessage("t", "m"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => kept!.Confirm("t", "m"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => kept!.RunWithProgress("t", (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task RunWithProgress_PassesWorkFailuresBack()
    {
        Exception? seen = null;
        await PluginButtonInvoker.Invoke(ButtonConfig.Create("x", async click =>
        {
            try { await click.DialogService.RunWithProgress("t", (_, _) => throw new InvalidOperationException("boom")); }
            catch (InvalidOperationException ex) { seen = ex; }
        }), null);
        Assert.NotNull(seen);
    }
}

[CollectionDefinition("ButtonActions")]
public class ButtonActionsCollection { }
