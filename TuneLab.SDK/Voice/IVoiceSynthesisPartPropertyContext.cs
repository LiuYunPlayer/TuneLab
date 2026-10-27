using System.Collections.Generic;
using TuneLab.Foundation;

namespace TuneLab.SDK;

// voice 声明面（GetXxxConfig）的求值上下文族：调用级只读**值视图**（`*View` 后缀 = 声明读值投影，区别于会话裸名活视图
// IVoiceSynthesisContext/IVoiceSynthesisNote 与跨线程冻结 *Snapshot）。与 instrument 持平行副本（域专属、独立演进）。
//
// 为何是值（PropertyObject）而非会话活对象：声明多选要做三态合并（PropertyObjectExtensions.Merge——逐 key 比对各成员
// 值快照、不等给 Multiple），这是 PropertyObject 值操作；会话面活外观 IReadOnlyNotifiablePropertyObject 是导航式、
// 喂不进 Merge、也无三态 / 无值快照形。故声明面与会话面（IVoiceSynthesisContext 活视图）**不复用**，各取所需。
//
// 生命周期 = 调用级：GetConfig 是数据线程一次性同步只读求值（读完即返、不留存）。宿主就地包一层、调完即弃——
// 构造期 / 管线拆除后 / 无会话时照样能包（直读数据层、永远在）。引擎可读 part 当前真值（含非属性字段：note 时间 /
// 音高 / 歌词、已声明自动化曲线、note 集合本身）条件化 schema。线程契约：仅数据线程同步只读、不留存视图引用。

// part 级声明壳（多选 part；单选 = 1 个、无选中 = 空）。包一层壳抗迭代——以后加只读字段不动方法签名。
public interface IVoiceSynthesisPartPropertyContext
{
    IReadOnlyList<IVoiceSynthesisPartView> Parts { get; }
}

// note 级声明壳（单 part、多选其下 note）。VoiceId / part 当前值经 Part 取。
public interface IVoiceSynthesisNotePropertyContext
{
    IVoiceSynthesisPartView Part { get; }
    IReadOnlyList<IVoiceSynthesisNoteView> Notes { get; }
}

