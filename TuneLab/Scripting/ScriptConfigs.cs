using System;
using System.Collections.Generic;
using System.Globalization;
using Jint;
using Jint.Native;
using Jint.Native.Function;
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
// 自定义 scale/format 回调（NormalizedScale.custom / NumberFormat.custom，收 JS 闭包）= 门面第 3 层：
// 把两个 JS 函数包成 INormalizedScale/INumberFormat 适配器（见 JsNormalizedScale/JsNumberFormat）。这些回调
// 【不在 main 里触发】，而在入参窗存续期、UI 线程、每次拖滑块/重绘/编辑文本时被调——故产出 config 的引擎须活到
// 关窗。无需显式 retain：适配器持 Engine 引用，config 被控件树引用 → 引擎随之保活；关窗 config 失引即一并 GC。
// engine.Invoke 每次自重置约束（超时/语句数不跨调用累积，已实测），故长开窗反复回调安全。回调抛错/返非法值不崩 UI。
internal static class ScriptConfigs
{
    // 把全部 config 门面注入为脚本全局。运行与 getInputConfig 枚举共用（对 getScriptInfo 无害）。
    // NormalizedScale/NumberFormat 门面持有本引擎，供 .custom 把 JS 闭包包成适配器（回调经它 Invoke）。
    public static void Register(Engine engine)
    {
        engine.SetValue("SliderConfig", new SliderConfigFacade());
        engine.SetValue("DraggableNumberBoxConfig", new DraggableNumberBoxConfigFacade());
        engine.SetValue("ComboBoxConfig", new ComboBoxConfigFacade());
        engine.SetValue("CheckBoxConfig", new CheckBoxConfigFacade());
        engine.SetValue("TextBoxConfig", new TextBoxConfigFacade());
        engine.SetValue("NormalizedScale", new NormalizedScaleFacade(engine));
        engine.SetValue("NumberFormat", new NumberFormatFacade(engine));
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
        public ScriptDraggableNumberBoxConfig WithRandomizable(JsValue value) => new(config.WithRandomizable(ScriptArgs.AsBoolOrNull(value) ?? true));
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

    // ── 自定义回调适配器：把 JS 闭包包成 SDK 行为接口。持 Engine 引用保活 + UI 线程逐拖拽调（见类头注释）。 ──

    // 归一化标度：p↔value 两个 JS 函数（对数轴、2^n 等）。抛错/返非数一律降级 NaN，绝不让脚本 bug 冒泡崩 UI（拖拽/重绘期触发）。
    sealed class JsNormalizedScale(Engine engine, JsValue toValue, JsValue toNormalized) : INormalizedScale
    {
        public double ToValue(double normalized) => Call(toValue, normalized);
        public double ToNormalized(double value) => Call(toNormalized, value);
        double Call(JsValue fn, double x)
        {
            try { var r = engine.Invoke(fn, x); return r.IsNumber() ? r.AsNumber() : double.NaN; }
            catch { return double.NaN; }
        }
    }

    // 数字格式：format(value)->string、parse(text)->number|null。format 返非串则回退不变式字面量；parse 返非数/NaN=解析失败(null)。
    sealed class JsNumberFormat(Engine engine, JsValue format, JsValue parse) : INumberFormat
    {
        public string Format(double value)
        {
            try { var r = engine.Invoke(format, value); return r.IsString() ? r.AsString() : value.ToString(CultureInfo.InvariantCulture); }
            catch { return value.ToString(CultureInfo.InvariantCulture); }
        }
        public double? Parse(string text)
        {
            try
            {
                var r = engine.Invoke(parse, text);
                if (r.IsNumber()) { var d = r.AsNumber(); return double.IsNaN(d) ? null : d; }
                return null;   // null/undefined/非数 = 解析失败（与 NumberFormat.Custom 约定一致）
            }
            catch { return null; }
        }
    }

    // .custom 的 JS 参数须是函数，否则回报清晰错误（消息回灌脚本作者/agent）。
    static JsValue RequireFunction(JsValue value, string owner, string param)
    {
        if (value is not Function)
            throw new ScriptApiException(string.Format("{0}.custom argument \"{1}\" must be a function.", owner, param));
        return value;
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

    internal sealed class NormalizedScaleFacade(Engine engine)
    {
        public ScriptNormalizedScale Linear(double min, double max) => new(NormalizedScale.Linear(min, max));
        public ScriptNormalizedScale Integer(double min, double max) => new(NormalizedScale.Integer(min, max));
        public ScriptNormalizedScale Rounded(ScriptNormalizedScale scale) => new(NormalizedScale.Rounded(scale.Inner));
        public ScriptNormalizedScale Floor(ScriptNormalizedScale scale) => new(NormalizedScale.Floor(scale.Inner));
        public ScriptNormalizedScale Ceil(ScriptNormalizedScale scale) => new(NormalizedScale.Ceil(scale.Inner));
        // 逃生口：p->value 与 value->p 两个 JS 函数（对数轴等任意映射）。
        public ScriptNormalizedScale Custom(JsValue toValue, JsValue toNormalized)
            => new(new JsNormalizedScale(engine, RequireFunction(toValue, "NormalizedScale", "toValue"), RequireFunction(toNormalized, "NormalizedScale", "toNormalized")));
    }

    internal sealed class NumberFormatFacade(Engine engine)
    {
        public ScriptNumberFormat Decimals(int digits) => new(NumberFormat.Decimals(digits));
        // 逃生口：format(value)->string 与 parse(text)->number|null（带单位/本地化等）。
        public ScriptNumberFormat Custom(JsValue format, JsValue parse)
            => new(new JsNumberFormat(engine, RequireFunction(format, "NumberFormat", "format"), RequireFunction(parse, "NumberFormat", "parse")));
    }
}
