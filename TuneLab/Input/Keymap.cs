using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia.Input;
using TuneLab.Foundation;
using TuneLab.GUI.Input;
using KeyBinding = TuneLab.GUI.Input.KeyBinding;   // 消歧：Avalonia.Input 也有 KeyBinding

namespace TuneLab.Input;

// 快捷键这一层：**哪些动作可绑 + 手势 + 分发 + override 持久化**。动作本身在 ActionRegistry（穷尽面），
// 这里只引用其 id 并补上手势与作用域——两层的判据不同（筛选 vs 穷尽，见 EditorAction）。
// 内置绑定启动期由各拥有者以"当前手势"作默认注册，分发（TryHandle）、菜单显示、设置页都从这里派生。
// 用户 override（差量）落 Configs/Keybindings.json。见 docs/keybinding-system.md §4、§7。
internal static class Keymap
{
    static readonly Dictionary<string, KeyBindingEntry> mBindings = new();    // 动作 id -> 可绑条目
    static readonly Dictionary<string, KeyBinding?> mOverrides = new();       // 差量：键存在即 override（值 null = 显式解绑）
    static Dictionary<(KeyScope, KeyBinding), KeyBindingEntry>? mIndex;       // (scope, 生效手势) -> 条目；惰性重建
    static string mPath = string.Empty;

    // 任何 override 变更后触发（供菜单显示 / 设置页刷新）。
    public static event Action? Changed;

    // 加载 override 差量。命令注册与本调用先后无关：Effective 每次实时合成。
    public static void Init(string path)
    {
        mPath = path;
        mOverrides.Clear();
        if (File.Exists(path))
        {
            try
            {
                var dto = JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(path));
                if (dto != null)
                {
                    foreach (var kvp in dto)
                    {
                        if (kvp.Value == null)
                        {
                            mOverrides[kvp.Key] = null;   // 显式解绑：覆盖默认
                            continue;
                        }
                        if (KeyCodec.TryParse(kvp.Value, out var binding))
                            mOverrides[kvp.Key] = binding;
                        else
                            Log.Error("Invalid keymap gesture for '" + kvp.Key + "': " + kvp.Value);   // 跳过、继承默认
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Failed to deserialize keymap: " + ex);
            }
        }
        mIndex = null;
    }

    // 注册一条**可绑动作**：动作本体进 ActionRegistry（穷尽面），可绑那一半（手势 + 作用域）留在这里。
    // 两件事一次做完，故不会出现"注册了动作却忘了让它可绑"或反之的半截状态；二期按面补动作时，
    // 只进穷尽面、不给手势的那些直接调 ActionRegistry.Register。
    public static void Register(EditorAction action, KeyScope scope, KeyBinding? defaultGesture = null)
    {
        ActionRegistry.Register(action);
        mBindings[action.Id] = new() { ActionId = action.Id, Scope = scope, DefaultGesture = defaultGesture };
        mIndex = null;
    }

    // 注销一条可绑动作（动作与绑定条目一并消失——脚本没了，用户就够不着它了）。
    // 用户 override 存在 mOverrides 里、与注册独立，注销不丢：脚本回归即复活。
    public static void Unregister(string id)
    {
        ActionRegistry.Unregister(id);
        if (mBindings.Remove(id))
            mIndex = null;
    }

    public static IReadOnlyCollection<KeyBindingEntry> Bindings => mBindings.Values;

    public static bool TryGet(string id, out KeyBindingEntry binding) => mBindings.TryGetValue(id, out binding!);

    // 该命令是否有用户 override（值即使为 null 解绑也算 override）。供设置页显示「重置」。
    public static bool HasOverride(string id) => mOverrides.ContainsKey(id);

    // 同一 scope 内已占用该手势的其它命令 id（用于设置页录制时的冲突提示）；无冲突返回 null。
    public static string? FindConflict(string id, KeyBinding binding)
    {
        if (!mBindings.TryGetValue(id, out var self))
            return null;
        foreach (var entry in mBindings.Values)
        {
            if (entry.ActionId == id || entry.Scope != self.Scope)
                continue;
            if (Effective(entry.ActionId) is { } g && g.Equals(binding))
                return entry.ActionId;
        }
        return null;
    }

    // 与 id 同作用域、同生效手势的其它命令 id（持久同域冲突组，供设置页警示展示）。空=无冲突。
    // 冲突来源不限交互绑定：手改 Keymap.json、多脚本声明同默认手势等都会形成，故须能随时检测、非仅绑定当时。
    // 分发的生效者由注册序决定（见 Index：注册序最小者胜，内建先注册故恒胜）。
    public static IReadOnlyList<string> SameScopeConflictPeers(string id)
    {
        if (!mBindings.TryGetValue(id, out var self) || Effective(id) is not { } g)
            return Array.Empty<string>();
        var peers = new List<string>();
        foreach (var entry in mBindings.Values)
            if (entry.ActionId != id && entry.Scope == self.Scope && Effective(entry.ActionId) is { } og && og.Equals(g))
                peers.Add(entry.ActionId);
        return peers;
    }

    // 生效手势 = override（若该 id 有 override 条目，含 null 解绑）否则默认。
    public static KeyBinding? Effective(string id)
    {
        if (mOverrides.TryGetValue(id, out var o))
            return o;
        return mBindings.TryGetValue(id, out var entry) ? entry.DefaultGesture : null;
    }

    // 在 scope 下按 e 的手势找命中的绑定并触发那条动作；命中返回 true（调用方据此置 e.Handled）。
    // 【手势命中即吞键】哪怕动作此刻不可用（撤销栈空、两个编辑面都没焦点…）：与从前"Execute 内部守卫
    // 直接 return、键仍算处理过"逐字等价，只是判据搬到了 ActionRegistry 一处。
    public static bool TryHandle(KeyScope scope, KeyEventArgs e)
    {
        if (Index().TryGetValue((scope, new KeyBinding(e.Key, e.KeyModifiers & KeyBinding.ModifierMask)), out var entry))
        {
            ActionRegistry.Execute(entry.ActionId);
            return true;
        }
        return false;
    }

    // 重绑 / 解绑（gesture==null 解绑）；即时落盘并广播 Changed。与默认相同则回落为无 override（保持差量最小）。
    public static void Rebind(string id, KeyBinding? gesture)
    {
        if (mBindings.TryGetValue(id, out var entry) && Nullable.Equals(entry.DefaultGesture, gesture))
            mOverrides.Remove(id);
        else
            mOverrides[id] = gesture;
        mIndex = null;
        Save();
        Changed?.Invoke();
    }

    public static void ResetToDefault(string id)
    {
        if (mOverrides.Remove(id))
        {
            mIndex = null;
            Save();
            Changed?.Invoke();
        }
    }

    public static void ResetAll()
    {
        if (mOverrides.Count == 0)
            return;
        mOverrides.Clear();
        mIndex = null;
        Save();
        Changed?.Invoke();
    }

    static Dictionary<(KeyScope, KeyBinding), KeyBindingEntry> Index()
    {
        if (mIndex != null)
            return mIndex;

        var index = new Dictionary<(KeyScope, KeyBinding), KeyBindingEntry>();
        foreach (var entry in mBindings.Values)
        {
            var gesture = Effective(entry.ActionId);
            if (gesture == null)
                continue;
            var key = (entry.Scope, gesture.Value);
            // 同作用域撞键（可来自手改 JSON、多脚本同默认等）时确定性取胜：注册序最小者（内建启动期先注册故恒胜、
            // 不被第三方脚本夺走）。冲突不隐藏——设置页 SameScopeConflictPeers 持久警示，由用户消解。
            if (index.TryGetValue(key, out var existing)
                && ActionRegistry.OrderOf(existing.ActionId) <= ActionRegistry.OrderOf(entry.ActionId))
                continue;
            index[key] = entry;
        }
        mIndex = index;
        return index;
    }

    static void Save()
    {
        try
        {
            var folder = Path.GetDirectoryName(mPath);
            if (!string.IsNullOrEmpty(folder))
                Directory.CreateDirectory(folder);

            var dto = new Dictionary<string, string?>();
            foreach (var kvp in mOverrides)
            {
                if (kvp.Value == null)
                    dto[kvp.Key] = null;                         // 显式解绑
                else if (KeyCodec.Serialize(kvp.Value.Value) is { } s)
                    dto[kvp.Key] = s;                            // 手势字符串（未收录键序列化为 null 则跳过）
            }
            File.WriteAllText(mPath, JsonSerializer.Serialize(dto, JsonSerializerOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save keymap: " + ex);
        }
    }

    static readonly JsonSerializerOptions JsonSerializerOptions = new() { WriteIndented = true };
}
