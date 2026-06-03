using System;

namespace TuneLab.SDK.Effect;

// 标注一个 effect 引擎实现类，type 是它在工程里登记的效果器类型标识。
// 宿主按此 attribute 发现并实例化引擎（与 voice 的 [VoiceEngine]、format 的 [ImportFormat] 同范式）。
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class EffectEngineAttribute(string type) : Attribute
{
    public string Type { get; } = type;
}
