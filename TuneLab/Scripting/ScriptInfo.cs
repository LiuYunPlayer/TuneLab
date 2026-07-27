using System;
using System.Collections.Generic;
using System.Globalization;
using Jint;
using Jint.Native;
using Jint.Native.Array;
using Jint.Native.Object;
using TuneLab.Data;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Scripting;

// 脚本面【info 层】的双向映射：JS 纯数据对象 ⇄ 数据层的 *Info。
//
// 为什么要有 info 层：数据层的创建一律【三段式】—— Info（纯数据，改它不进撤销栈）→ CreateX(info)（建游离
// 实体）→ InsertX(entity)（入树，这一步才进回退栈）。自由度落在"改 info"那一段：它是纯数据，怎么改都零心智
// 负担、零副作用。脚本侧若只收一小袋假想字段（旧 addNote 只收 4 个），等于宿主替使用者决定了"能表达什么"；
// 收完整 info 则把组合的自由还给使用者，且天然覆盖"整段复制"这类诉求（getInfo() → addX(info)）。
//
// 【契约独立于 SDK DTO】：本层是脚本面自己的 schema（camelCase + 绝对 tick），不是 SDK DataInfo 的直接投影。
// SDK 的 PartInfo/TrackInfo 是 PublicAPI.Shipped.txt 守着的冻结 ABI，演进节奏与脚本面相反；把它直接暴露会
// 让脚本面变成 ABI 的一部分。两边字段【一一对应】但各自命名，中间由本文件显式桥接——多一层映射，换来两边
// 各自能独立演进。
//
// 【坐标口径】位置一律【绝对（全局）tick】，与句柄面同一条铁律，故 note.pos 在句柄上读到的和在 info 里看到
// 的是同一个数。数据层里 part 内成员（note / vibrato / 曲线点）存的是【相对 part 锚点】的坐标，故一律经
// basePos 换算：读入 relative = abs - basePos，写出 abs = relative + basePos。part 自身的 pos 就是锚点的
// 绝对位置，不换算。
//
// 【校验的位置】info 阶段零校验（纯数据、随便乱来），【落地那一刻】才校验——即本文件的 Read* 系列在被
// addX/insertX 调用时校验。只校验"内部不变式"级的东西（dur>0、pitch 值域、part 类型判别），
// 【不做存在性校验】：音源 / effect 引擎装没装是环境问题，info 路要能忠实搬运孤儿数据（插件卸载后工程文件
// 照样能开、复制照样保真）。"按名字指定一个引擎"那类显式意图（part.setSoundSource / part.addEffect 的顶层
// type）才校验存在性——见各自方法。
internal static class ScriptInfo
{
    // ══════════════════════ JS → info（落地方向） ══════════════════════

    // 【刻意不含导出开关】：导出配置（工程级的路径/格式/采样率 + 逐轨的是否导出/声道数）不是可撤销的工程数据，
    // 而是"一次导出动作的参数"——它在数据层是普通属性、写它不入撤销栈，混进脚本面就会破掉脚本最值钱的性质
    // （整段脚本 = 一个可撤销单位；出错与 preview 原子回退）。agent 要改导出设置走【工具面】（export_project），
    // 那里不可撤销这件事能在授权闸门上让用户看见。故脚本复制一条轨时导出开关落默认值，不跟随源轨。
    public static TrackInfo ReadTrackInfo(JsValue value)
    {
        var o = ScriptArgs.Obj(value, "track info");
        var info = new TrackInfo
        {
            Name = ScriptArgs.OptStr(o, "name") ?? string.Empty,
            Gain = ScriptArgs.OptNum(o, "gain") ?? 0,
            Pan = Math.Clamp(ScriptArgs.OptNum(o, "pan") ?? 0, -1, 1),
            Mute = ScriptArgs.OptBool(o, "mute") ?? false,
            Solo = ScriptArgs.OptBool(o, "solo") ?? false,
            AsRefer = ScriptArgs.OptBool(o, "asRefer") ?? true,
            Color = ScriptArgs.OptStr(o, "color") ?? string.Empty,
        };
        if (ScriptArgs.Has(o, "parts", out var parts))
            info.Parts = ScriptArgs.ReadArray(parts, "parts", ReadPartInfo);
        return info;
    }

    // type 判别 midi / audio（缺省 midi）。PartInfo 是抽象类，两个子型字段不交叠，故必须先定型再读。
    public static PartInfo ReadPartInfo(JsValue value)
    {
        var o = ScriptArgs.Obj(value, "part info");
        string type = ScriptArgs.OptStr(o, "type") ?? "midi";
        PartInfo info;
        if (string.Equals(type, "midi", StringComparison.OrdinalIgnoreCase))
            info = ReadMidiPartBody(o);
        else if (string.Equals(type, "audio", StringComparison.OrdinalIgnoreCase))
            info = new AudioPartInfo { Path = ScriptArgs.OptStr(o, "path") ?? string.Empty };
        else
            throw new ScriptApiException("part info field \"type\" must be \"midi\" or \"audio\".");

        info.Name = ScriptArgs.OptStr(o, "name") ?? string.Empty;
        info.Pos = ScriptArgs.OptNum(o, "pos") ?? 0;
        info.StartOffset = ScriptArgs.OptNum(o, "startOffset") ?? 0;
        info.EndOffset = ScriptArgs.OptNum(o, "endOffset") ?? 0;
        if (info.EndOffset <= info.StartOffset)
            throw new ScriptApiException("part info endOffset must be greater than startOffset (the part would have zero or negative length).");
        return info;
    }

    // part 内成员的绝对 tick 以本 part 的锚点（info.pos）为基准换算，故先读出 pos 再读内容。
    static MidiPartInfo ReadMidiPartBody(ObjectInstance o)
    {
        double basePos = ScriptArgs.OptNum(o, "pos") ?? 0;
        var info = new MidiPartInfo
        {
            Gain = ScriptArgs.OptNum(o, "gain") ?? 0,
            Properties = ReadProperties(o, "properties"),
        };
        if (ScriptArgs.Has(o, "soundSource", out var source))
            info.SoundSource = ReadSoundSourceInfo(source);
        if (ScriptArgs.Has(o, "notes", out var notes))
            info.Notes = ScriptArgs.ReadArray(notes, "notes", v => ReadNoteInfo(v, basePos));
        if (ScriptArgs.Has(o, "vibratos", out var vibratos))
            info.Vibratos = ScriptArgs.ReadArray(vibratos, "vibratos", v => ReadVibratoInfo(v, basePos));
        if (ScriptArgs.Has(o, "effects", out var effects))
            info.Effects = ScriptArgs.ReadArray(effects, "effects", v => ReadEffectInfo(v, basePos));
        if (ScriptArgs.Has(o, "automations", out var automations))
            info.Automations = ReadMap(automations, "automations", v => ReadAutomationInfo(v, basePos));
        if (ScriptArgs.Has(o, "piecewiseAutomations", out var piecewise))
            info.PiecewiseAutomations = ReadMap(piecewise, "piecewiseAutomations", v => new PiecewiseAutomationInfo { Segments = ReadSegments(v, basePos) });
        if (ScriptArgs.Has(o, "pitch", out var pitch))
            info.Pitch = new PitchInfo { Segments = ReadSegments(ScriptArgs.Obj(pitch, "pitch").Get("segments"), basePos) };
        return info;
    }

    public static SoundSourceInfo ReadSoundSourceInfo(JsValue value)
    {
        var o = ScriptArgs.Obj(value, "soundSource");
        return new SoundSourceInfo
        {
            Kind = ReadSourceKind(ScriptArgs.OptStr(o, "kind") ?? "voice"),
            Type = ScriptArgs.OptStr(o, "type") ?? string.Empty,
            Id = ScriptArgs.OptStr(o, "id") ?? string.Empty,
        };
    }

    public static SourceKind ReadSourceKind(string kind)
    {
        if (string.Equals(kind, "voice", StringComparison.OrdinalIgnoreCase)) return SourceKind.Voice;
        if (string.Equals(kind, "instrument", StringComparison.OrdinalIgnoreCase)) return SourceKind.Instrument;
        throw new ScriptApiException("kind must be \"voice\" or \"instrument\".");
    }

    public static NoteInfo ReadNoteInfo(JsValue value, double basePos)
    {
        var o = ScriptArgs.Obj(value, "note info");
        double dur = ScriptArgs.ReqNum(o, "dur");
        if (dur <= 0) throw new ScriptApiException("dur must be positive.");
        var info = new NoteInfo
        {
            Pos = ScriptArgs.ReqNum(o, "pos") - basePos,
            Dur = dur,
            Pitch = Math.Clamp(ScriptArgs.ReqInt(o, "pitch"), MusicTheory.MIN_PITCH, MusicTheory.MAX_PITCH),
            Lyric = ScriptArgs.OptStr(o, "lyric") ?? string.Empty,
            Pronunciation = ScriptArgs.OptStr(o, "pronunciation") ?? string.Empty,
            Properties = ReadProperties(o, "properties"),
            BodyOffset = ScriptArgs.OptNum(o, "bodyOffset") ?? 0,
        };
        if (ScriptArgs.Has(o, "leadingPhonemes", out var leading))
            info.LeadingPhonemes = ScriptArgs.ReadArray(leading, "leadingPhonemes", ReadPhonemeInfo);
        if (ScriptArgs.Has(o, "bodyPhonemes", out var body))
            info.BodyPhonemes = ScriptArgs.ReadArray(body, "bodyPhonemes", ReadPhonemeInfo);
        return info;
    }

    public static PhonemeInfo ReadPhonemeInfo(JsValue value)
    {
        var o = ScriptArgs.Obj(value, "phoneme info");
        var properties = ReadProperties(o, "properties");
        return new PhonemeInfo
        {
            Symbol = ScriptArgs.OptStr(o, "symbol") ?? throw new ScriptApiException("phoneme info field \"symbol\" is required."),
            Duration = Math.Max(0, ScriptArgs.OptNum(o, "duration") ?? 0),
            StretchWeight = Math.Max(0, ScriptArgs.OptNum(o, "stretchWeight") ?? 0),
            // 空属性存 null 而非空容器（pay-as-you-go，与 Phoneme.GetInfo 一致）。
            Properties = properties.Map.Count > 0 ? properties : null,
        };
    }

    // 颤音的 frequency/amplitude/attack/release 缺省值是【编辑器口径的可听默认】（6Hz / 1 半音 / 0.2s 起收），
    // 非 SDK DTO 的零值——零频率的颤音等于不存在，那不是有用的默认。
    public static VibratoInfo ReadVibratoInfo(JsValue value, double basePos)
    {
        var o = ScriptArgs.Obj(value, "vibrato info");
        double dur = ScriptArgs.ReqNum(o, "dur");
        if (dur <= 0) throw new ScriptApiException("dur must be positive.");
        var info = new VibratoInfo
        {
            Pos = ScriptArgs.ReqNum(o, "pos") - basePos,
            Dur = dur,
            Frequency = ScriptArgs.OptNum(o, "frequency") ?? 6,
            Amplitude = ScriptArgs.OptNum(o, "amplitude") ?? 1,
            Phase = ScriptArgs.OptNum(o, "phase") ?? 0,
            Attack = ScriptArgs.OptNum(o, "attack") ?? 0.2,
            Release = ScriptArgs.OptNum(o, "release") ?? 0.2,
        };
        if (ScriptArgs.Has(o, "affectedAutomations", out var affected))
            info.AffectedAutomations = ReadMap(affected, "affectedAutomations", ReadAmplitude);
        if (ScriptArgs.Has(o, "affectedEffectAutomations", out var affectedEffect))
            info.AffectedEffectAutomations = ReadMap(affectedEffect, "affectedEffectAutomations",
                v => ReadMap(v, "affectedEffectAutomations entry", ReadAmplitude));
        return info;
    }

    static double ReadAmplitude(JsValue v)
        => v.IsNumber() ? v.AsNumber() : throw new ScriptApiException("a vibrato affected-automation amplitude must be a number.");

    public static EffectInfo ReadEffectInfo(JsValue value, double basePos)
    {
        var o = ScriptArgs.Obj(value, "effect info");
        var info = new EffectInfo
        {
            // 空 id = 让宿主发号；非空则沿用（复制 / undo / 装载都靠它保持实例身份，颤音影响表按它做外键）。
            Id = ScriptArgs.OptStr(o, "id") ?? string.Empty,
            Type = ScriptArgs.OptStr(o, "type") ?? string.Empty,
            IsEnabled = ScriptArgs.OptBool(o, "isEnabled") ?? true,
            Properties = ReadProperties(o, "properties"),
        };
        if (ScriptArgs.Has(o, "automations", out var automations))
            info.Automations = ReadMap(automations, "automations", v => ReadAutomationInfo(v, basePos));
        if (ScriptArgs.Has(o, "piecewiseAutomations", out var piecewise))
            info.PiecewiseAutomations = ReadMap(piecewise, "piecewiseAutomations", v => new PiecewiseAutomationInfo { Segments = ReadSegments(v, basePos) });
        return info;
    }

    static AutomationInfo ReadAutomationInfo(JsValue value, double basePos)
    {
        var o = ScriptArgs.Obj(value, "automation info");
        return new AutomationInfo
        {
            DefaultValue = ScriptArgs.OptNum(o, "defaultValue") ?? 0,
            Points = ScriptArgs.Has(o, "points", out var points) ? ReadPoints(points, basePos) : new List<Point>(),
        };
    }

    // 分段曲线（pitch / 分段自动化轨）：段数组的数组，段间为关断（无值）。
    static List<List<Point>> ReadSegments(JsValue value, double basePos)
    {
        if (value is null || value.IsUndefined() || value.IsNull())
            return new List<List<Point>>();
        return ScriptArgs.ReadArray(value, "segments", v => ReadPoints(v, basePos));
    }

    static List<Point> ReadPoints(JsValue value, double basePos)
        => ScriptArgs.ReadArray(value, "points", v =>
        {
            var p = ScriptArgs.Obj(v, "point");
            return new Point(ScriptArgs.ReqNum(p, "tick") - basePos, ScriptArgs.ReqNum(p, "value"));
        });

    // ── 自定义属性容器（part / note / phoneme / effect 的 Properties）：全保真（含嵌套对象/数组），
    //    因为 info 路的职责是忠实搬运；逐字段的 getProperty/setProperty 只认标量，那是另一件事。 ──

    static PropertyObject ReadProperties(ObjectInstance o, string name)
    {
        if (!ScriptArgs.Has(o, name, out var value))
            return PropertyObject.Empty;
        return ReadPropertyObject(value, name);
    }

    static PropertyObject ReadPropertyObject(JsValue value, string what)
    {
        var o = ScriptArgs.Obj(value, what);
        var map = new Map<string, PropertyValue>();
        foreach (var key in o.GetOwnPropertyKeys(Jint.Runtime.Types.String))
            map.Add(key.AsString(), ReadPropertyValue(o.Get(key)));
        return new PropertyObject(map);
    }

    static PropertyValue ReadPropertyValue(JsValue v)
    {
        if (v is null || v.IsUndefined() || v.IsNull()) return PropertyValue.Null;
        if (v.IsNumber()) return PropertyValue.Create(v.AsNumber());
        if (v.IsBoolean()) return PropertyValue.Create(v.AsBoolean());
        if (v.IsString()) return PropertyValue.Create(v.AsString());
        if (v.IsArray())
        {
            var items = ScriptArgs.ReadArray(v, "property array", ReadPropertyValue);
            return PropertyValue.Create(new PropertyArray(items));
        }
        if (v.IsObject()) return PropertyValue.Create(ReadPropertyObject(v, "property object"));
        throw new ScriptApiException("a property value must be a number, boolean, string, array, object or null.");
    }

    // 任意字符串键的 map（automations / properties / 颤音影响表都是这个形状）。
    static Map<string, T> ReadMap<T>(JsValue value, string what, Func<JsValue, T> readValue)
    {
        var o = ScriptArgs.Obj(value, what);
        var map = new Map<string, T>();
        foreach (var key in o.GetOwnPropertyKeys(Jint.Runtime.Types.String))
            map.Add(key.AsString(), readValue(o.Get(key)));
        return map;
    }

    // ══════════════════════ info → JS（读出方向） ══════════════════════

    public static JsValue ToJs(Engine engine, TrackInfo info)
    {
        var o = new JsObject(engine);
        o.Set("name", info.Name);
        o.Set("gain", info.Gain);
        o.Set("pan", info.Pan);
        o.Set("mute", info.Mute);
        o.Set("solo", info.Solo);
        o.Set("asRefer", info.AsRefer);
        o.Set("color", info.Color);
        o.Set("parts", Array(engine, info.Parts, p => ToJs(engine, p)));
        return o;
    }

    public static JsValue ToJs(Engine engine, PartInfo info)
    {
        var o = new JsObject(engine);
        o.Set("type", info is AudioPartInfo ? "audio" : "midi");
        o.Set("name", info.Name);
        o.Set("pos", info.Pos);
        o.Set("startOffset", info.StartOffset);
        o.Set("endOffset", info.EndOffset);
        if (info is AudioPartInfo audio)
        {
            o.Set("path", audio.Path);
        }
        else if (info is MidiPartInfo midi)
        {
            double basePos = midi.Pos;
            o.Set("gain", midi.Gain);
            o.Set("soundSource", ToJs(engine, midi.SoundSource));
            o.Set("notes", Array(engine, midi.Notes, n => ToJs(engine, n, basePos)));
            o.Set("vibratos", Array(engine, midi.Vibratos, v => ToJs(engine, v, basePos)));
            o.Set("effects", Array(engine, midi.Effects, e => ToJs(engine, e, basePos)));
            o.Set("automations", MapToJs(engine, midi.Automations, a => ToJs(engine, a, basePos)));
            o.Set("piecewiseAutomations", MapToJs(engine, midi.PiecewiseAutomations, p => SegmentsHolder(engine, p.Segments, basePos)));
            o.Set("pitch", SegmentsHolder(engine, midi.Pitch.Segments, basePos));
            o.Set("properties", ToJs(engine, midi.Properties));
        }
        return o;
    }

    public static JsValue ToJs(Engine engine, SoundSourceInfo info)
    {
        var o = new JsObject(engine);
        o.Set("kind", info.Kind == SourceKind.Voice ? "voice" : "instrument");
        o.Set("type", info.Type);
        o.Set("id", info.Id);
        return o;
    }

    public static JsValue ToJs(Engine engine, NoteInfo info, double basePos)
    {
        var o = new JsObject(engine);
        o.Set("pos", info.Pos + basePos);
        o.Set("dur", info.Dur);
        o.Set("pitch", info.Pitch);
        o.Set("lyric", info.Lyric);
        o.Set("pronunciation", info.Pronunciation);
        o.Set("properties", ToJs(engine, info.Properties));
        o.Set("leadingPhonemes", Array(engine, info.LeadingPhonemes, p => ToJs(engine, p)));
        o.Set("bodyPhonemes", Array(engine, info.BodyPhonemes, p => ToJs(engine, p)));
        o.Set("bodyOffset", info.BodyOffset);
        return o;
    }

    public static JsValue ToJs(Engine engine, PhonemeInfo info)
    {
        var o = new JsObject(engine);
        o.Set("symbol", info.Symbol);
        o.Set("duration", info.Duration);
        o.Set("stretchWeight", info.StretchWeight);
        // 无属性的音素写 null（不是空对象）：与存储侧的 pay-as-you-go 一致，往返不凭空造出容器。
        o.Set("properties", info.Properties == null ? JsValue.Null : ToJs(engine, info.Properties));
        return o;
    }

    public static JsValue ToJs(Engine engine, VibratoInfo info, double basePos)
    {
        var o = new JsObject(engine);
        o.Set("pos", info.Pos + basePos);
        o.Set("dur", info.Dur);
        o.Set("frequency", info.Frequency);
        o.Set("amplitude", info.Amplitude);
        o.Set("phase", info.Phase);
        o.Set("attack", info.Attack);
        o.Set("release", info.Release);
        o.Set("affectedAutomations", MapToJs(engine, info.AffectedAutomations, a => (JsValue)a));
        o.Set("affectedEffectAutomations", MapToJs(engine, info.AffectedEffectAutomations,
            tracks => MapToJs(engine, tracks, a => (JsValue)a)));
        return o;
    }

    public static JsValue ToJs(Engine engine, EffectInfo info, double basePos)
    {
        var o = new JsObject(engine);
        o.Set("id", info.Id);
        o.Set("type", info.Type);
        o.Set("isEnabled", info.IsEnabled);
        o.Set("automations", MapToJs(engine, info.Automations, a => ToJs(engine, a, basePos)));
        o.Set("piecewiseAutomations", MapToJs(engine, info.PiecewiseAutomations, p => SegmentsHolder(engine, p.Segments, basePos)));
        o.Set("properties", ToJs(engine, info.Properties));
        return o;
    }

    static JsValue ToJs(Engine engine, AutomationInfo info, double basePos)
    {
        var o = new JsObject(engine);
        o.Set("defaultValue", info.DefaultValue);
        o.Set("points", PointsToJs(engine, info.Points, basePos));
        return o;
    }

    static JsValue SegmentsHolder(Engine engine, List<List<Point>> segments, double basePos)
    {
        var o = new JsObject(engine);
        o.Set("segments", Array(engine, segments, s => PointsToJs(engine, s, basePos)));
        return o;
    }

    static JsValue PointsToJs(Engine engine, IReadOnlyList<Point> points, double basePos)
        => Array(engine, points, p =>
        {
            var o = new JsObject(engine);
            o.Set("tick", p.X + basePos);
            o.Set("value", p.Y);
            return o;
        });

    public static JsValue ToJs(Engine engine, PropertyObject properties)
    {
        var o = new JsObject(engine);
        foreach (var kvp in properties.Map)
            o.Set(kvp.Key, ToJs(engine, kvp.Value));
        return o;
    }

    static JsValue ToJs(Engine engine, PropertyValue value)
    {
        if (value.ToBoolean(out var b)) return b;
        if (value.ToDouble(out var d)) return d;
        if (value.ToString(out var s)) return s;
        if (value.ToObject(out var obj)) return ToJs(engine, obj);
        if (value.ToArray(out var arr)) return Array(engine, arr, v => ToJs(engine, v));
        return JsValue.Null;
    }

    // 颤音影响表的读出（句柄面 vibrato.affectedAutomations() / affectedEffectAutomations() 用）：
    // 音源级是 { 轨 id: 振幅 }；effect 级在数据层是扁平的 (effect 实例 id, 轨 id) → 振幅，读出时按 effect
    // 归拢成两层，与持久形（VibratoInfo.AffectedEffectAutomations）同形。
    public static JsValue AmplitudesToJs(Engine engine, IReadOnlyMap<string, double> map)
        => MapToJs(engine, map, a => (JsValue)a);

    public static JsValue EffectAmplitudesToJs(Engine engine, IReadOnlyMap<EffectAutomationRef, double> map)
    {
        var byEffect = new Dictionary<string, JsObject>();
        var outer = new JsObject(engine);
        foreach (var kvp in map)
        {
            if (!byEffect.TryGetValue(kvp.Key.EffectId, out var tracks))
            {
                byEffect[kvp.Key.EffectId] = tracks = new JsObject(engine);
                outer.Set(kvp.Key.EffectId, tracks);
            }
            tracks.Set(kvp.Key.Id, kvp.Value);
        }
        return outer;
    }

    static JsValue MapToJs<T>(Engine engine, IReadOnlyMap<string, T> map, Func<T, JsValue> toJs)
    {
        var o = new JsObject(engine);
        foreach (var kvp in map)
            o.Set(kvp.Key, toJs(kvp.Value));
        return o;
    }

    // 脚本拿到的集合一律是【普通 JS 数组】（for-of / 下标 / .length / JSON.stringify 都照常），不是 CLR 包装对象。
    static JsValue Array<T>(Engine engine, IReadOnlyList<T> items, Func<T, JsValue> toJs)
    {
        var values = new JsValue[items.Count];
        for (int i = 0; i < items.Count; i++)
            values[i] = toJs(items[i]);
        return new JsArray(engine, values);
    }
}
