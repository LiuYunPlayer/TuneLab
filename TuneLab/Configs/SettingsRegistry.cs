using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Avalonia.Media;
using TuneLab.Audio;
using TuneLab.Foundation;
using TuneLab.I18N;
using TuneLab.SDK;

namespace TuneLab.Configs;

// 设置窗的 tab 分组（仅承载「用户可调、有设置窗 UI」的设置；孤儿设置 Tab=null，只存储不渲染）。
internal enum SettingTab { General, Audio, Appearance, Editing }

// 一个设置的【声明】（单一真源）：把它的值 holder + 元数据（tab/标签/控件 config/即时生效·重启标记/描述）+ JSON 读写
// 全绑在一处。设置窗按此自动生成行、Settings.Init/Save 遍历此读写磁盘、agent 经此枚举与读写——「声明一次，处处派生」。
// 值 holder 仍是 NotifiableProperty<T>（活值 + Modified 事件），故既有 ~11 处实时生效订阅、~60 处读点零改动。
internal abstract class SettingItem
{
    protected SettingItem(string key, SettingTab? tab, string label, IControllerConfig config)
    {
        Key = key; Tab = tab; Label = label; Config = config;
    }

    public string Key { get; }                 // = settings.json 里的键 / 稳定 id
    public SettingTab? Tab { get; }            // null = 仅存储、不在设置窗渲染（如 AutoScrollTarget / Agent* 由别处设）
    public string Label { get; }               // 翻译键：设置窗行标 + agent 显示名
    public IControllerConfig Config { get; }   // 控件 config（与脚本入参同一套；范围/默认/选项在内）
    public string? Description { get; init; }   // 供 agent list_settings + 设置窗 tooltip
    public bool RestartRequired { get; init; }  // 改后需重启才完全生效（统一挂提示）
    public bool ImmediateApply { get; init; }   // 拖动即生效（对应旧 Bind 的 syncWhileModifying）
    public string[]? FilePatterns { get; init; }         // 非空 → 设置窗用路径选择器（PathInput）而非普通控件
    public string? FilePickerName { get; init; }         // 路径选择器的文件类型显示名
    public Func<IReadOnlyList<ComboBoxItem>>? DynamicOptions { get; init; }   // 运行时选项（音频驱动/设备、系统字体、语言）
    // agent 可否经 set_setting 改它。false = 只能用户自己在 UI 改（agent 只能读+建议）：
    // 授权档位（防自我提权）、以及活值由别处 UI 拥有的项（写文件不生效/会被覆盖，改了只会误导）。
    public bool AgentWritable { get; init; } = true;

    // 设置窗行标 / agent 呈现用的本地化标签。译文键历史上归设置窗上下文（Resources/Translations 的 [SettingsWindow] 段），
    // 页名（SettingTab）同段，故 agent 侧文本化也用这个上下文。
    // 注意必须是 TranslationContext（而非裸 string）——裸 string 会绑到 Tr(string, object) 重载、上下文键退化成 "String"。
    public string DisplayLabel => Label.Tr(LabelTranslationContext);
    public static readonly TranslationContext LabelTranslationContext = "SettingsWindow";

    // 通用值访问（agent set_setting / 序列化外通用）：以 PropertyValue 为公共货币。
    public abstract PropertyValue GetValue();
    public abstract bool TrySetValue(PropertyValue value);
    public abstract PropertyValue GetDefaultValue();

    // 磁盘读写：缺键 / 类型不符一律退回默认（容错，绝不因单个坏键丢整份设置）。
    public abstract void Load(JsonObject json);
    public abstract void Save(JsonObject json);
}

// 强类型条目：T ∈ {string, int, double, bool}。转换器由工厂注入（PropertyValue ↔ T、JSON ↔ T）。
internal sealed class SettingItem<T> : SettingItem where T : notnull
{
    public NotifiableProperty<T> Property { get; }
    public T DefaultValue { get; }

    readonly Func<T, PropertyValue> mToPv;
    readonly Func<PropertyValue, (bool ok, T val)> mFromPv;
    readonly Func<JsonNode?, T, T> mRead;
    readonly Func<T, JsonNode?> mWrite;

    public SettingItem(string key, SettingTab? tab, string label, IControllerConfig config, T defaultValue,
        Func<T, PropertyValue> toPv, Func<PropertyValue, (bool, T)> fromPv,
        Func<JsonNode?, T, T> read, Func<T, JsonNode?> write)
        : base(key, tab, label, config)
    {
        DefaultValue = defaultValue;
        Property = new NotifiableProperty<T>(defaultValue);
        mToPv = toPv; mFromPv = fromPv; mRead = read; mWrite = write;
    }

    public override PropertyValue GetValue() => mToPv(Property.Value);
    public override PropertyValue GetDefaultValue() => mToPv(DefaultValue);
    public override bool TrySetValue(PropertyValue value)
    {
        var (ok, val) = mFromPv(value);
        if (ok) Property.Value = val;
        return ok;
    }
    public override void Load(JsonObject json)
        => Property.Value = mRead(json.TryGetPropertyValue(Key, out var node) ? node : null, DefaultValue);
    public override void Save(JsonObject json) => json[Key] = mWrite(Property.Value);
}

// 全部设置的声明表。默认值仍取自 Settings.DefaultSettings（SettingsFile，单一默认源；阶段②重写设置窗后可并入这里）。
internal static class SettingsRegistry
{
    static SettingsFile D => Settings.DefaultSettings;

    // ── General ──
    public static readonly SettingItem<string> Language = Str("Language", SettingTab.General, "Language",
        ComboBoxConfig.Create(), D.Language, restart: true, dynamicOptions: LanguageOptions);
    public static readonly SettingItem<int> AutoSaveInterval = Int("AutoSaveInterval", SettingTab.General,
        "Auto Save Interval (second)", SliderConfig.Integer(D.AutoSaveInterval, 10, 60), D.AutoSaveInterval);
    public static readonly SettingItem<int> AutoSaveMaxCount = Int("AutoSaveMaxCount", SettingTab.General,
        "Auto Save Max Count", SliderConfig.Integer(D.AutoSaveMaxCount, 1, 20), D.AutoSaveMaxCount);
    public static readonly SettingItem<int> MaxParallelSynthesisTasks = Int("MaxParallelSynthesisTasks", SettingTab.General,
        "Max Parallel Synthesis Tasks (0 = auto)", SliderConfig.Integer(D.MaxParallelSynthesisTasks, 0, 32), D.MaxParallelSynthesisTasks);
    public static readonly SettingItem<int> AgentMaxToolResultChars = Int("AgentMaxToolResultChars", SettingTab.General,
        "AI Agent max tool result (characters)", SliderConfig.Integer(D.AgentMaxToolResultChars, 0, 200000), D.AgentMaxToolResultChars);

    // ── Audio ──
    public static readonly SettingItem<double> MasterGain = Dbl("MasterGain", SettingTab.Audio,
        "Master Gain (dB)", SliderConfig.Linear(D.MasterGain, -24, 24), D.MasterGain, immediate: true);
    public static readonly SettingItem<string> AudioDriver = Str("AudioDriver", SettingTab.Audio,
        "Audio Driver", ComboBoxConfig.Create(), D.AudioDriver, dynamicOptions: () => AudioEngine.GetAllDrivers().Select(o => (ComboBoxItem)o).ToList());
    public static readonly SettingItem<string> AudioDevice = Str("AudioDevice", SettingTab.Audio,
        "Audio Device", ComboBoxConfig.Create(), D.AudioDevice, dynamicOptions: () => AudioEngine.GetAllDevices().Select(o => (ComboBoxItem)o).ToList());
    public static readonly SettingItem<int> SampleRate = Int("SampleRate", SettingTab.Audio,
        "Sample Rate", IntCombo(D.SampleRate, 32000, 44100, 48000, 96000, 192000), D.SampleRate);
    public static readonly SettingItem<int> BufferSize = Int("BufferSize", SettingTab.Audio,
        "Buffer Size", IntCombo(D.BufferSize, 64, 128, 256, 512, 1024, 2048, 4096, 8192), D.BufferSize);
    public static readonly SettingItem<string> PianoKeySamplesPath = Str("PianoKeySamplesPath", SettingTab.Audio,
        "Piano Key Samples", TextBoxConfig.Create(), D.PianoKeySamplesPath, patterns: ["*.sf2"], pickerName: "SoundFont");

    // ── Appearance ──
    public static readonly SettingItem<string> InterfaceFontFamily = Str("InterfaceFontFamily", SettingTab.Appearance,
        "Interface Font", ComboBoxConfig.Create(), D.InterfaceFontFamily, dynamicOptions: FontOptions);
    public static readonly SettingItem<string> BackgroundImagePath = Str("BackgroundImagePath", SettingTab.Appearance,
        "Custom Background Image", TextBoxConfig.Create(), D.BackgroundImagePath, patterns: ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp"], pickerName: "Image");
    public static readonly SettingItem<double> BackgroundImageOpacity = Dbl("BackgroundImageOpacity", SettingTab.Appearance,
        "Background Image Opacity", SliderConfig.Linear(D.BackgroundImageOpacity, 0, 1), D.BackgroundImageOpacity, immediate: true);
    public static readonly SettingItem<double> TrackHueChangeRate = Dbl("TrackHueChangeRate", SettingTab.Appearance,
        "Track Hue Change Rate (degree/second)", SliderConfig.Integer(D.TrackHueChangeRate, -720, 720), D.TrackHueChangeRate, immediate: true);

    // ── Editing ──
    // 值为 double（消费者按 double 读），但设置窗用整数滑条。
    public static readonly SettingItem<double> ParameterBoundaryExtension = Dbl("ParameterBoundaryExtension", SettingTab.Editing,
        "Parameter Boundary Extension (tick)", SliderConfig.Integer(D.ParameterBoundaryExtension, 1, 60), D.ParameterBoundaryExtension);
    public static readonly SettingItem<bool> ParameterSyncMode = Bool("ParameterSyncMode", SettingTab.Editing,
        "Parameter Sync Mode", CheckBoxConfig.Create(D.ParameterSyncMode), D.ParameterSyncMode);

    // ── 仅存储（无设置窗行；由别处设定，但仍随本注册表读写磁盘、可被 agent 枚举） ──
    // 三者的【活值都由别处的 UI 拥有】（视图菜单 / agent 侧栏），只单向落盘：agent 写文件既不即时生效、又会被那处 UI
    // 覆盖，改了只会误导 → 一律 agentWritable: false（授权档位另有防自我提权的理由）。Description 供 agent 转告用户去哪改。
    public static readonly SettingItem<string> AutoScrollTarget = Str("AutoScrollTarget", null,
        "Auto Scroll Target", ComboBoxConfig.Create([new ComboBoxItem((PropertyValue)"None", "None"), new ComboBoxItem((PropertyValue)"Playhead", "Playhead")]), D.AutoScrollTarget,
        agentWritable: false, description: "Whether the editor auto-scrolls to follow playback. Owned by the View menu's auto-scroll option, not the Settings window.");
    public static readonly SettingItem<string> AgentModelProvider = Str("AgentModelProvider", null,
        "AI Agent Model Provider", TextBoxConfig.Create(), D.AgentModelProvider,
        agentWritable: false, description: "Which model provider the AI agent last connected to. Owned by the AI Agent side panel's settings (switching there also reconnects).");
    public static readonly SettingItem<string> AgentAuthorization = Str("AgentAuthorization", null,
        "AI Agent Authorization", ComboBoxConfig.Create([new ComboBoxItem((PropertyValue)"ReadOnlyAdvice", "ReadOnlyAdvice"), new ComboBoxItem((PropertyValue)"Confirm", "Confirm"), new ComboBoxItem((PropertyValue)"Auto", "Auto")]), D.AgentAuthorization,
        agentWritable: false, description: "How much the AI agent is allowed to change (ReadOnlyAdvice/Confirm/Auto). Only the user can change it, in the AI Agent panel header — the agent must never raise its own permissions.");

    // 全部条目——顺序 = 【设置窗行序】（tab 分组、组内重要项在前），是单一受控顺序源：
    // 设置窗 All.Where(Tab==tab) 渲染、agent list_settings 同序。末尾是仅存储的孤儿设置（无 tab、不渲染）。
    // 磁盘 JSON 键随此顺序写出（分组排列）——键顺序无语义、按键名加载，不影响兼容。
    public static readonly IReadOnlyList<SettingItem> All =
    [
        // General
        Language, AutoSaveInterval, AutoSaveMaxCount, MaxParallelSynthesisTasks, AgentMaxToolResultChars,
        // Audio
        MasterGain, AudioDriver, AudioDevice, SampleRate, BufferSize, PianoKeySamplesPath,
        // Appearance
        InterfaceFontFamily, BackgroundImagePath, BackgroundImageOpacity, TrackHueChangeRate,
        // Editing
        ParameterBoundaryExtension, ParameterSyncMode,
        // 仅存储（无设置窗行）
        AutoScrollTarget, AgentModelProvider, AgentAuthorization,
    ];

    // ── 工厂 + 转换器 ──

    static SettingItem<string> Str(string key, SettingTab? tab, string label, IControllerConfig config, string def,
        bool restart = false, string[]? patterns = null, string? pickerName = null,
        Func<IReadOnlyList<ComboBoxItem>>? dynamicOptions = null, bool agentWritable = true, string? description = null)
        => new(key, tab, label, config, def,
            toPv: v => PropertyValue.Create(v),
            fromPv: v => v.ToString(out var s) ? (true, s) : (false, def),
            read: ReadStr, write: v => JsonValue.Create(v))
        {
            RestartRequired = restart, FilePatterns = patterns, FilePickerName = pickerName,
            DynamicOptions = dynamicOptions, AgentWritable = agentWritable, Description = description,
        };

    static SettingItem<int> Int(string key, SettingTab? tab, string label, IControllerConfig config, int def)
        => new(key, tab, label, config, def,
            toPv: v => PropertyValue.Create((double)v),
            fromPv: v => v.ToDouble(out var d) ? (true, (int)Math.Round(d)) : (false, def),
            read: ReadInt, write: v => JsonValue.Create(v));

    static SettingItem<double> Dbl(string key, SettingTab? tab, string label, IControllerConfig config, double def,
        bool immediate = false)
        => new(key, tab, label, config, def,
            toPv: v => PropertyValue.Create(v),
            fromPv: v => v.ToDouble(out var d) ? (true, d) : (false, def),
            read: ReadDbl, write: v => JsonValue.Create(v))
        { ImmediateApply = immediate };

    static SettingItem<bool> Bool(string key, SettingTab? tab, string label, IControllerConfig config, bool def)
        => new(key, tab, label, config, def,
            toPv: v => PropertyValue.Create(v),
            fromPv: v => v.ToBoolean(out var b) ? (true, b) : (false, def),
            read: ReadBool, write: v => JsonValue.Create(v));

    // 数值选择框（供 SampleRate / BufferSize）：项的【值】是数字的字符串形（如 "44100"），
    // 设置窗绑定时经 .Select(int.Parse, i=>i.ToString()) 桥到 int 属性（与旧窗口一致）。
    static ComboBoxConfig IntCombo(int def, params int[] options)
    {
        var items = new List<ComboBoxItem>(options.Length);
        foreach (var o in options)
            items.Add((ComboBoxItem)o.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return ComboBoxConfig.Create(items);
    }

    // ── 运行时选项（config 静态选项之外按需现取；设置窗与 agent list_settings/set_setting 共用同一份，故挂在声明上） ──

    static IReadOnlyList<ComboBoxItem> LanguageOptions()
        => TranslationManager.Languages.Select(o => new ComboBoxItem(o, TranslationManager.GetDisplayName(o))).ToList();

    static IReadOnlyList<ComboBoxItem> FontOptions()
    {
        var options = new List<ComboBoxItem> { new(PropertyValue.Create(string.Empty), "System Default".Tr(SettingItem.LabelTranslationContext)) };
        options.AddRange(FontManager.Current.SystemFonts
            .Select(f => f.Name).Distinct().OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
            .Select(n => (ComboBoxItem)n));
        return options;
    }

    static string ReadStr(JsonNode? n, string d) { try { return n is null ? d : (n.GetValue<string>() ?? d); } catch { return d; } }
    static int ReadInt(JsonNode? n, int d) { try { return n is null ? d : (int)Math.Round(n.GetValue<double>()); } catch { return d; } }
    static double ReadDbl(JsonNode? n, double d) { try { return n is null ? d : n.GetValue<double>(); } catch { return d; } }
    static bool ReadBool(JsonNode? n, bool d) { try { return n is null ? d : n.GetValue<bool>(); } catch { return d; } }
}
