using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.TestPlugins.V1Settings;

// 扩展设置（IExtensionSettings）演示夹具：一个 voice 引擎，仅用于验证「扩展级设置」全链路——
//   ① 宿主在 设置 > 扩展 渲染 GetSettingsConfig() 出的 schema；
//   ② 保存时普通字段明文落盘、IsPassword 字段加密落盘（Win=DPAPI 内联 / mac+linux=OS 凭据库）；
//   ③ 重启后宿主经 ApplySettings 把持久值回喂（此处打日志为可观测点）。
// 不参与实际合成（CreateSession 抛异常）——设置链路全程不触发 Init/合成。
// 按钮（ButtonConfig）+ 点击期弹窗服务（click.DialogService）演示：清理缓存（确认 → 可取消进度 → 提示，按钮文字随缓存大小刷新）、
// 测试连接（用窗口里尚未保存的 API Key）、导出预设（PromptInputs 索取输入）、故意抛异常；另有嵌套对象里与列表元素里的按钮。
// 插件自己的设置界面（独立进程）演示在 V1.CustomWindow。
// DisplayText 由插件自译（演示自带极简词典，未收录词回退原文）。

static class L
{
    static readonly Dictionary<string, Dictionary<string, string>> Dict = new()
    {
        ["zh-CN"] = new()
        {
            ["Model Path"] = "模型路径",
            ["Engine Executable"] = "引擎可执行文件",
            ["Select the engine executable"] = "选择引擎可执行文件",
            ["Voice Bank Folder"] = "音源库目录",
            ["Startup Notes"] = "启动备注",
            ["API Key"] = "API 密钥",
            ["Use GPU"] = "使用 GPU",
            ["GPU Device"] = "GPU 设备",
            ["Threads"] = "线程数",
            ["Precision"] = "精度",
            ["Advanced"] = "高级",
            ["Verbose Log"] = "详细日志",
            ["Cache"] = "缓存",
            ["Clear"] = "清理",
            ["Clear Cache"] = "清理缓存",
            ["Delete all cached files?"] = "删除全部缓存文件？",
            ["Clearing cache..."] = "正在清理缓存...",
            ["Cache cleared."] = "缓存已清理。",
            ["Writing cache index (can't be cancelled)..."] = "正在写入缓存索引（不可取消）...",
            ["Cancelled; part of the cache was kept."] = "已取消，保留了部分缓存。",
            ["Connection"] = "连接",
            ["Test"] = "测试",
            ["Test Connection"] = "测试连接",
            ["Fill in the API Key first."] = "请先填写 API 密钥。",
            ["Connecting..."] = "正在连接...",
            ["Connected."] = "连接成功。",
            ["Export Preset"] = "导出预设",
            ["Export..."] = "导出...",
            ["Preset Name"] = "预设名",
            ["Quality"] = "质量",
            ["Include API Key"] = "包含 API 密钥",
            ["Crash"] = "崩溃",
            ["Throw"] = "抛异常",
            ["Reset Advanced"] = "重置高级项",
            ["Servers"] = "服务器",
            ["URL"] = "地址",
            ["Reached "] = "已连通 ",
        },
    };

    public static string Tr(string en)
    {
        var lang = TuneLabContext.Global.Language;
        return Dict.TryGetValue(lang, out var m) && m.TryGetValue(en, out var v) ? v : en;
    }
}

public sealed class SettingsVoiceEngine : IVoiceSynthesisEngine, IExtensionSettings
{
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => sEmpty;

    public void Init() { }
    public void Destroy() { }

    // 仅设置演示，不参与实际合成。
    public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context)
        => throw new NotSupportedException("V1.Settings is a settings-only demo fixture.");

    // 声明面（全空）：本夹具不暴露轨/面板，仅验证扩展设置链路。
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => sEmptyAutomations;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => sEmptyAutomations;
    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => sEmptyConfig;
    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => sEmptyConfig;
    public IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context) => [];

    // 设置 schema：普通文本（模型路径）+ 密钥（API Key：掩码显示 + 加密落盘）+ 一个开关。
    // 动态演示：勾选「使用 GPU」后才暴露「GPU 设备」字段（据 context 当前值条件显隐）。
    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var props = new OrderedMap<PropertyKey, IControllerConfig>();
        props.Add(("model_path", L.Tr("Model Path")), TextBoxConfig.Create(string.Empty));
        // 路径选择两种模式各一个：选文件（带类型过滤 + 自定义对话框标题）/ 选文件夹。
        props.Add(("engine_path", L.Tr("Engine Executable")),
            PathPickerConfig.CreateFile(string.Empty)
                .AppendFileType(L.Tr("Engine Executable"), "*.exe", "*.bat")
                .WithPickerTitle(L.Tr("Select the engine executable")));
        props.Add(("voice_bank_dir", L.Tr("Voice Bank Folder")), PathPickerConfig.CreateFolder(string.Empty));
        // 多行文本：值仍是一个 string（换行在里面），框贴着内容长、最多 4 行、再多则框内滚动。
        props.Add(("startup_notes", L.Tr("Startup Notes")), TextBoxConfig.Create(string.Empty).WithMultiline(4));
        props.Add(("api_key", L.Tr("API Key")), TextBoxConfig.Create(string.Empty).WithPassword());
        props.Add(("use_gpu", L.Tr("Use GPU")), CheckBoxConfig.Create(false));
        if (context.Settings.GetBoolean("use_gpu", false))
            props.Add(("gpu_device", L.Tr("GPU Device")), TextBoxConfig.Create(string.Empty));
        // 各控件类型各来一个，覆盖「按声明校验取值」的全部分支（范围 / 下拉成员 / 分组不可直接设）：
        props.Add(("threads", L.Tr("Threads")), SliderConfig.Integer(4, 1, 16));
        props.Add(("precision", L.Tr("Precision")), ComboBoxConfig.Create(["fp32", "fp16", "int8"]));
        var advanced = new OrderedMap<PropertyKey, IControllerConfig>();
        advanced.Add(("verbose", L.Tr("Verbose Log")), CheckBoxConfig.Create(false));
        // 嵌套对象里的按钮（路径 = advanced/reset）。
        advanced.Add(("reset", L.Tr("Reset Advanced")), ButtonConfig.Create(L.Tr("Clear"), _ =>
        {
            TuneLabContext.Global.GetLogger().Info("[V1.Settings] advanced reset clicked");
            return Task.CompletedTask;
        }));
        props.Add(("advanced", L.Tr("Advanced")), ObjectConfig.Create(advanced));

        // —— 按钮（弹窗服务只在点击期间经 click.DialogService 可用）——
        // 按钮文字带当前缓存大小：点完后宿主自动重新询问本函数，文字随之刷新——验证"点击结束即刷新所在面板"。
        props.Add(("cache", L.Tr("Cache")), ButtonConfig.Create(string.Format("{0} ({1} MB)", L.Tr("Clear"), sCacheMB), ClearCache));
        // 闭包只捕获值：在声明时把（可能尚未保存的）API Key 读出来，而不是捕获 context。
        string apiKey = context.Settings.GetString("api_key", string.Empty);
        props.Add(("connection", L.Tr("Connection")), ButtonConfig.Create(L.Tr("Test"), click => TestConnection(click, apiKey)));
        // 向用户索取输入（PromptInputs）：宿主按 config 画表单；初始值写在默认值里（这里用上次的结果）。
        props.Add(("export", L.Tr("Export Preset")), ButtonConfig.Create(L.Tr("Export..."), ExportPreset));
        props.Add(("crash", L.Tr("Crash")), ButtonConfig.Create(L.Tr("Throw"), _ => throw new InvalidOperationException("V1.Settings: deliberate failure from a button.")));

        // 列表元素里的按钮：每个服务器一行（地址 + Ping），按钮路径带元素 token，各行独立置灰。
        var servers = new List<IControllerConfig>();
        int count = context.Settings.Map.TryGetValue("servers", out var serversValue) && serversValue.ToArray(out var array) ? array.Count : 2;
        for (int i = 0; i < count; i++)
        {
            string url = ServerUrl(context.Settings, i);
            var row = new OrderedMap<PropertyKey, IControllerConfig>
            {
                { ("url", L.Tr("URL")), TextBoxConfig.Create("https://server" + (i + 1) + ".example") },
                { ("ping", "Ping"), ButtonConfig.Create("Ping", click => Ping(click, url)) },
            };
            servers.Add(ObjectConfig.Create(row));
        }
        var serverTemplate = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig> { { ("url", L.Tr("URL")), TextBoxConfig.Create("https://") } });
        props.Add(("servers", L.Tr("Servers")), ListConfig.Create(servers, [new AddableElement(serverTemplate)]));
        return ObjectConfig.Create(props);
    }

    static string ServerUrl(PropertyObject settings, int index)
    {
        if (settings.Map.TryGetValue("servers", out var value) && value.ToArray(out var array) && index < array.Count && array[index].ToObject(out var row))
            return row.GetString("url", string.Empty);
        return "https://server" + (index + 1) + ".example";
    }

    // 模拟的缓存大小（进程内状态）：按钮只能动插件自己的状态，不改任何设置值。
    static int sCacheMB = 2300;

    static async Task ClearCache(IButtonClickContext click)
    {
        if (!await click.DialogService.Confirm(L.Tr("Clear Cache"), L.Tr("Delete all cached files?")))
            return;

        bool cancelled = false;
        await click.DialogService.RunWithProgress(L.Tr("Clearing cache..."), async (progress, cancel) =>
        {
            const int steps = 30;
            int start = sCacheMB;
            for (int i = 1; i <= steps; i++)
            {
                if (cancel.IsCancellationRequested) { cancelled = true; return; }
                // await 是交错点：此刻宿主可能调用插件的其他方法（合成调度、声明函数）来读缓存。所以每一步之后
                // sCacheMB 都必须是一个有效状态（逐步减小的真实大小），不能停在"清了一半、大小未定"的中间态。
                await Task.Delay(100);
                sCacheMB = start - start * i / steps;
                progress.Report((double)i / steps, string.Format("{0} MB", sCacheMB));
            }

            // 关键段：重写缓存索引，写到一半被打断会留下读不出来的索引——这一段不让用户取消（按钮变灰）。
            // 只更新文字时 fraction 传当前进度（1.0），不能传 null（那会切成滚动条）。
            progress.SetCancellable(false);
            progress.Report(1.0, L.Tr("Writing cache index (can't be cancelled)..."));
            await Task.Delay(1500);
            progress.SetCancellable(true);
        });
        await click.DialogService.ShowMessage(L.Tr("Clear Cache"), cancelled ? L.Tr("Cancelled; part of the cache was kept.") : L.Tr("Cache cleared."));
    }

    static async Task TestConnection(IButtonClickContext click, string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey))
        {
            await click.DialogService.ShowMessage(L.Tr("Test Connection"), L.Tr("Fill in the API Key first."));
            return;
        }
        // 不确定进度（fraction = null）：只显示滚动条。
        await click.DialogService.RunWithProgress(L.Tr("Connecting..."), async (progress, cancel) =>
        {
            progress.Report(null, "api.example.com");
            await Task.Delay(1500, cancel);
        });
        await click.DialogService.ShowMessage(L.Tr("Test Connection"), L.Tr("Connected."));
    }

    // 点按钮后要用户填几个参数：PromptInputs 返回完整值（没改的字段为默认值），取消返回 null。
    static async Task ExportPreset(IButtonClickContext click)
    {
        var form = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
        {
            { ("name", L.Tr("Preset Name")), TextBoxConfig.Create(sLastExport.GetString("name", "My Preset")) },
            { ("quality", L.Tr("Quality")), SliderConfig.Integer(sLastExport.GetDouble("quality", 5), 0, 10) },
            { ("include_keys", L.Tr("Include API Key")), CheckBoxConfig.Create(sLastExport.GetBoolean("include_keys", false)) },
        });
        var inputs = await click.DialogService.PromptInputs(L.Tr("Export Preset"), form);
        if (inputs == null)
            return;
        sLastExport = inputs;
        await click.DialogService.ShowMessage(L.Tr("Export Preset"), string.Format("name={0}, quality={1}, include_keys={2}",
            inputs.GetString("name", ""), inputs.GetDouble("quality", 0), inputs.GetBoolean("include_keys", false)));
    }

    static PropertyObject sLastExport = PropertyObject.Empty;

    static async Task Ping(IButtonClickContext click, string url)
    {
        await Task.Delay(1200);   // 各行独立置灰：执行期间只有本行按钮不可点
        await click.DialogService.ShowMessage("Ping", L.Tr("Reached ") + url);
    }

    // 回喂可观测点：把收到的设置打日志，供测试核对"重启后引擎确实收到了持久化的值"。密钥只记录是否非空、不打印明文。
    public void ApplySettings(PropertyObject settings)
    {
        var path = settings.GetString("model_path", string.Empty);
        var hasKey = !string.IsNullOrEmpty(settings.GetString("api_key", string.Empty));
        var gpu = settings.GetBoolean("use_gpu", false);
        TuneLabContext.Global.GetLogger().Info(string.Format(
            "[V1.Settings] ApplySettings: model_path='{0}', api_key={1}, use_gpu={2}",
            path, hasKey ? "<set>" : "<empty>", gpu));
        TuneLabContext.Global.GetLogger().Info(string.Format(
            "[V1.Settings] ApplySettings: engine_path='{0}', voice_bank_dir='{1}'",
            settings.GetString("engine_path", string.Empty), settings.GetString("voice_bank_dir", string.Empty)));
        // 换行改写成两字符的转义字面量再打：日志是逐行的，真换行会把一条拆成多条、看不出存进去的到底是什么。
        TuneLabContext.Global.GetLogger().Info(string.Format(
            "[V1.Settings] ApplySettings: startup_notes='{0}'",
            settings.GetString("startup_notes", string.Empty).Replace("\r", string.Empty).Replace("\n", "\\n")));
    }

    static readonly OrderedMap<string, VoiceSourceInfo> sEmpty = new();
    static readonly OrderedMap<PropertyKey, AutomationConfig> sEmptyAutomations = new();
    static readonly ObjectConfig sEmptyConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
}
