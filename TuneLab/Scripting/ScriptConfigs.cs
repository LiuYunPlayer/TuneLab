using System;
using System.Collections.Generic;
using System.Globalization;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Scripting;

// 脚本入参的【config 构造门面】。脚本用一套与冻结的 TuneLab.SDK.ControllerConfigs 类【同构】的 API 造 config
// （SliderConfig.integer(...)、ComboBoxConfig.create([...]).withDefault(...)…），而非另立"描述对象"词汇。
// 见 docs/script-inputs-and-action-surface.md §2.2：
//  · 同构调用，非声明式描述对象（scale/format 是可组合行为接口，闭包/装饰器天然是调用式）；
//  · 只同构"人体工学表面"、藏掉管道类型（PropertyValue/PropertyKey/IReadOnlyOrderedMap 不暴进 JS，裸值直写）；
//  · 只镜像输入相关的 value config 子集（Automation/ExtensibleObject/Addable 等不给）。
//
// 门面对象经 Register 注入为脚本全局（名字=类名）；工厂方法返回【句柄】(IScriptConfig)——不透明地持有真实
// IControllerConfig，暴露流式 With/Append。getInputConfig 返回"键→句柄"的 map，宿主经 BuildInputConfig 取出内部
// config 拼成 ObjectConfig（复用属性面板渲染）。camelCase↔PascalCase 由引擎既有 TypeResolver 桥接。
//
// 注：自定义 scale/format 回调（NormalizedScale.custom / NumberFormat.custom，收 JS 闭包）属阶段 3b——
// 那些回调在关窗前的 UI 线程逐拖拽触发、需引擎存续至关窗，故本文件只实现内置工厂（linear/integer/decimals…）。
internal static class ScriptConfigs
{
    // 把全部 config 门面注入为脚本全局。运行与 getInputConfig 枚举共用（对 getScriptInfo 无害）。
    public static void Register(Engine engine)
    {
        engine.SetValue("SliderConfig", new SliderConfigFacade());
        engine.SetValue("DraggableNumberBoxConfig", new DraggableNumberBoxConfigFacade());
        engine.SetValue("ComboBoxConfig", new ComboBoxConfigFacade());
        engine.SetValue("CheckBoxConfig", new CheckBoxConfigFacade());
        engine.SetValue("TextBoxConfig", new TextBoxConfigFacade());
        engine.SetValue("NormalizedScale", new NormalizedScaleFacade());
        engine.SetValue("NumberFormat", new NumberFormatFacade());
    }

    // 读 getInputConfig() 的返回（键→config 句柄的对象），按声明序拼成 ObjectConfig。键即入参名(=PropertyKey.Id=标签)。
    // 非法项（值不是 config 句柄）抛 ScriptApiException，点名出错的键，供脚本作者（含 agent）纠错。
    public static ObjectConfig BuildInputConfig(JsValue getInputConfigResult)
    {
        if (getInputConfigResult is null || !getInputConfigResult.IsObject())
            throw new ScriptApiException("getInputConfig() must return an object mapping input names to config values (e.g. { semitones: SliderConfig.integer(12, -24, 24) }).");

        var obj = getInputConfigResult.AsObject();
        var map = new OrderedMap<PropertyKey, IControllerConfig>();
        // GetOwnPropertyKeys 按 ES 规范序返回：非整数字符串键保持插入序（入参名皆非整数键）。
        foreach (var keyVal in obj.GetOwnPropertyKeys(Jint.Runtime.Types.String))
        {
            string key = keyVal.AsString();
            var value = obj.Get(keyVal);
            if (value.ToObject() is not IScriptConfig handle)
                throw new ScriptApiException(string.Format(
                    "getInputConfig() field \"{0}\" is not a config; build it with SliderConfig/ComboBoxConfig/CheckBoxConfig/TextBoxConfig/DraggableNumberBoxConfig.", key));
            if (map.ContainsKey(key))
                continue;   // 防御重复键（对象键本唯一）
            map.Add(key, handle.Build());
        }
        return ObjectConfig.Create(map);
    }

    // 把【稀疏】用户值按【当刻 schema】补全为喂给 main 的【全量】入参：只含当前生效字段（条件隐藏的不含），
    // 用户设过用其值、否则用 config 默认（ComboBox 取 DefaultOption.Value）。让 main 直接读到所有声明字段、免判断在场，
    // 且不冻结默认（每次用当刻 schema 现补）。持久化仍存稀疏（本函数只作用于运行入参，不回写存储）。见 docs §2.4。
    public static PropertyObject FillDefaults(ObjectConfig schema, PropertyObject values)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var kvp in schema.Properties)
        {
            var id = kvp.Key.Id;
            if (values.Map.TryGetValue(id, out var v) && !v.IsNull())
                map.Add(id, v);
            else if (kvp.Value is IValueConfig leaf)
                map.Add(id, leaf.DefaultValue);
        }
        return new PropertyObject(map);
    }

    // PropertyObject（入参值，键=入参名）→ JS 对象，喂 main(inputs) 与 getInputConfig 的 ctx.values。
    public static JsValue ToJsObject(Engine engine, PropertyObject values)
    {
        var obj = new JsObject(engine);
        foreach (var kvp in values.Map)
            obj.Set(kvp.Key, ToJsValue(engine, kvp.Value));
        return obj;
    }

    static JsValue ToJsValue(Engine engine, PropertyValue value)
    {
        if (value.ToDouble(out var d)) return d;
        if (value.ToBoolean(out var b)) return b;
        if (value.ToString(out var s)) return s;
        return JsValue.Null;
    }

    // ── JS 值 → SDK 原语（藏掉管道类型，脚本只写裸值） ──
    static PropertyValue ToPropertyValue(JsValue v)
    {
        if (v is null || v.IsUndefined() || v.IsNull()) return PropertyValue.Null;
        if (v.IsNumber()) return PropertyValue.Create(v.AsNumber());
        if (v.IsBoolean()) return PropertyValue.Create(v.AsBoolean());
        if (v.IsString()) return PropertyValue.Create(v.AsString());
        return PropertyValue.Create(v.ToString());
    }

    // 一个下拉项：裸值（'a' / 1 / true）或 { value, text } 对象（值/显示分离）。
    static ComboBoxItem ToComboBoxItem(JsValue v)
    {
        if (v is not null && v.IsObject() && !v.IsArray())
        {
            var o = v.AsObject();
            var valField = o.Get("value");
            if (!valField.IsUndefined())
            {
                string? text = ScriptArgs.OptStr(o, "text");
                return new ComboBoxItem(ToPropertyValue(valField), text);
            }
        }
        return new ComboBoxItem(ToPropertyValue(v));
    }

    static List<ComboBoxItem> ReadItems(JsValue options)
    {
        var o = ScriptArgs.Obj(options, "options");
        var lenVal = o.Get("length");
        if (!lenVal.IsNumber())
            throw new ScriptApiException("ComboBoxConfig options must be an array (e.g. ['a', 'b'] or [{value, text}]).");
        int len = (int)lenVal.AsNumber();
        var items = new List<ComboBoxItem>(len);
        for (int i = 0; i < len; i++)
            items.Add(ToComboBoxItem(o.Get(i.ToString(CultureInfo.InvariantCulture))));
        return items;
    }

    // ── 句柄：不透明地持有真实 config，暴露流式 With/Append ──

    internal interface IScriptConfig { IControllerConfig Build(); }

    internal sealed class ScriptSliderConfig(SliderConfig config) : IScriptConfig
    {
        public IControllerConfig Build() => config;
        public ScriptSliderConfig WithFormat(ScriptNumberFormat format) => new(config.WithFormat(format.Inner));
        public ScriptSliderConfig WithRandomizable(JsValue value) => new(config.WithRandomizable(ScriptArgs.AsBoolOrNull(value) ?? true));
        public ScriptSliderConfig WithMinLabel(string label) => new(config.WithMinLabel(label));
        public ScriptSliderConfig WithMaxLabel(string label) => new(config.WithMaxLabel(label));
    }

    internal sealed class ScriptDraggableNumberBoxConfig(DraggableNumberBoxConfig config) : IScriptConfig
    {
        public IControllerConfig Build() => config;
        public ScriptDraggableNumberBoxConfig WithMin(double min) => new(config.WithMin(min));
        public ScriptDraggableNumberBoxConfig WithMax(double max) => new(config.WithMax(max));
        public ScriptDraggableNumberBoxConfig WithRange(double min, double max) => new(config.WithRange(min, max));
        public ScriptDraggableNumberBoxConfig WithStep(double step) => new(config.WithStep(step));
        public ScriptDraggableNumberBoxConfig WithSensitivity(double sensitivity) => new(config.WithSensitivity(sensitivity));
        public ScriptDraggableNumberBoxConfig WithFormat(ScriptNumberFormat format) => new(config.WithFormat(format.Inner));
    }

    internal sealed class ScriptComboBoxConfig(ComboBoxConfig config) : IScriptConfig
    {
        public IControllerConfig Build() => config;
        public ScriptComboBoxConfig Append(JsValue item) => new(config.Append(ToComboBoxItem(item)));
        public ScriptComboBoxConfig AppendSeparator(JsValue label) => new(config.AppendSeparator(ScriptArgs.AsStrOrNull(label)));
        public ScriptComboBoxConfig WithDefault(JsValue value) => new(config.WithDefault(ToComboBoxItem(value)));
    }

    internal sealed class ScriptCheckBoxConfig(CheckBoxConfig config) : IScriptConfig
    {
        public IControllerConfig Build() => config;
    }

    internal sealed class ScriptTextBoxConfig(TextBoxConfig config) : IScriptConfig
    {
        public IControllerConfig Build() => config;
        public ScriptTextBoxConfig WithPassword(JsValue value) => new(config.WithPassword(ScriptArgs.AsBoolOrNull(value) ?? true));
    }

    internal sealed class ScriptNormalizedScale(INormalizedScale scale)
    {
        public INormalizedScale Inner => scale;
    }

    internal sealed class ScriptNumberFormat(INumberFormat format)
    {
        public INumberFormat Inner => format;
    }

    // ── 工厂（脚本全局，名=类名；方法=各类静态工厂） ──

    internal sealed class SliderConfigFacade
    {
        public ScriptSliderConfig Linear(double defaultValue, double minValue, double maxValue) => new(SliderConfig.Linear(defaultValue, minValue, maxValue));
        public ScriptSliderConfig Integer(double defaultValue, double minValue, double maxValue) => new(SliderConfig.Integer(defaultValue, minValue, maxValue));
        public ScriptSliderConfig Create(double defaultValue, ScriptNormalizedScale scale) => new(SliderConfig.Create(defaultValue, scale.Inner));
    }

    internal sealed class DraggableNumberBoxConfigFacade
    {
        public ScriptDraggableNumberBoxConfig Create() => new(DraggableNumberBoxConfig.Create());
        public ScriptDraggableNumberBoxConfig Create(double defaultValue) => new(DraggableNumberBoxConfig.Create(defaultValue));
        public ScriptDraggableNumberBoxConfig Integer() => new(DraggableNumberBoxConfig.Integer());
        public ScriptDraggableNumberBoxConfig Integer(double defaultValue) => new(DraggableNumberBoxConfig.Integer(defaultValue));
    }

    internal sealed class ComboBoxConfigFacade
    {
        public ScriptComboBoxConfig Create() => new(ComboBoxConfig.Create());
        public ScriptComboBoxConfig Create(JsValue options) => new(ComboBoxConfig.Create(ReadItems(options)));
    }

    internal sealed class CheckBoxConfigFacade
    {
        public ScriptCheckBoxConfig Create() => new(CheckBoxConfig.Create());
        public ScriptCheckBoxConfig Create(JsValue defaultValue) => new(CheckBoxConfig.Create(ScriptArgs.AsBoolOrNull(defaultValue) ?? false));
    }

    internal sealed class TextBoxConfigFacade
    {
        public ScriptTextBoxConfig Create() => new(TextBoxConfig.Create());
        public ScriptTextBoxConfig Create(JsValue defaultValue) => new(TextBoxConfig.Create(ScriptArgs.AsStrOrNull(defaultValue) ?? ""));
    }

    internal sealed class NormalizedScaleFacade
    {
        public ScriptNormalizedScale Linear(double min, double max) => new(NormalizedScale.Linear(min, max));
        public ScriptNormalizedScale Integer(double min, double max) => new(NormalizedScale.Integer(min, max));
        public ScriptNormalizedScale Rounded(ScriptNormalizedScale scale) => new(NormalizedScale.Rounded(scale.Inner));
        public ScriptNormalizedScale Floor(ScriptNormalizedScale scale) => new(NormalizedScale.Floor(scale.Inner));
        public ScriptNormalizedScale Ceil(ScriptNormalizedScale scale) => new(NormalizedScale.Ceil(scale.Inner));
    }

    internal sealed class NumberFormatFacade
    {
        public ScriptNumberFormat Decimals(int digits) => new(NumberFormat.Decimals(digits));
    }
}
