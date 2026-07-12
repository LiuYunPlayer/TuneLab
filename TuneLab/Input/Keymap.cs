using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Avalonia.Input;
using TuneLab.Foundation;
using TuneLab.GUI.Input;
using KeyBinding = TuneLab.GUI.Input.KeyBinding;   // 消歧：Avalonia.Input 也有 KeyBinding

namespace TuneLab.Input;

// 快捷键命令注册表 + 分发器 + override 持久化。唯一真相源：内置命令启动期由各拥有者以"当前手势"作默认注册，
// 分发（TryHandle）与将来的菜单显示、设置页都从这里派生。用户 override（差量）落 Configs/Keymap.json。
// 见 docs/keybinding-system.md §4、§7。
internal static class Keymap
{
    static readonly Dictionary<string, KeyCommand> mCommands = new();
    static readonly Dictionary<string, int> mOrder = new();               // id -> 首次注册序（设置页按此排序，反映逻辑注册顺序而非字母序）
    static int mNextOrder;
    static readonly Dictionary<string, KeyBinding?> mOverrides = new();   // 差量：键存在即 override（值 null = 显式解绑）
    static Dictionary<(KeyScope, KeyBinding), KeyCommand>? mIndex;        // (scope, 生效手势) -> 命令；惰性重建
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

    public static void Register(KeyCommand command)
    {
        mCommands[command.Id] = command;
        if (!mOrder.ContainsKey(command.Id))
            mOrder[command.Id] = mNextOrder++;   // 首次注册序固定，重注册（脚本重建）不改次序
        mIndex = null;
    }

    // 首次注册序（设置页排序用）；未注册返回 int.MaxValue。
    public static int OrderOf(string id) => mOrder.TryGetValue(id, out var o) ? o : int.MaxValue;

    public static void Unregister(string id)
    {
        if (mCommands.Remove(id))
            mIndex = null;
    }

    public static IReadOnlyCollection<KeyCommand> Commands => mCommands.Values;

    public static bool TryGet(string id, out KeyCommand command) => mCommands.TryGetValue(id, out command!);

    // 该命令是否有用户 override（值即使为 null 解绑也算 override）。供设置页显示「重置」。
    public static bool HasOverride(string id) => mOverrides.ContainsKey(id);

    // 同一 scope 内已占用该手势的其它命令 id（用于设置页录制时的冲突提示）；无冲突返回 null。
    public static string? FindConflict(string id, KeyBinding binding)
    {
        if (!mCommands.TryGetValue(id, out var self))
            return null;
        foreach (var cmd in mCommands.Values)
        {
            if (cmd.Id == id || cmd.Scope != self.Scope)
                continue;
            if (Effective(cmd.Id) is { } g && g.Equals(binding))
                return cmd.Id;
        }
        return null;
    }

    // 生效手势 = override（若该 id 有 override 条目，含 null 解绑）否则默认。
    public static KeyBinding? Effective(string id)
    {
        if (mOverrides.TryGetValue(id, out var o))
            return o;
        return mCommands.TryGetValue(id, out var cmd) ? cmd.DefaultGesture : null;
    }

    // 按 id 执行命令（供菜单点击等非按键触发路径）；未注册则静默忽略。
    public static void Execute(string id)
    {
        if (mCommands.TryGetValue(id, out var cmd))
            cmd.Execute();
    }

    // 在 scope 下按 e 的手势找命中命令并执行；命中返回 true（调用方据此置 e.Handled）。
    public static bool TryHandle(KeyScope scope, KeyEventArgs e)
    {
        if (Index().TryGetValue((scope, new KeyBinding(e.Key, e.KeyModifiers & KeyBinding.ModifierMask)), out var cmd))
        {
            cmd.Execute();
            return true;
        }
        return false;
    }

    // 重绑 / 解绑（gesture==null 解绑）；即时落盘并广播 Changed。与默认相同则回落为无 override（保持差量最小）。
    public static void Rebind(string id, KeyBinding? gesture)
    {
        if (mCommands.TryGetValue(id, out var cmd) && Nullable.Equals(cmd.DefaultGesture, gesture))
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

    static Dictionary<(KeyScope, KeyBinding), KeyCommand> Index()
    {
        if (mIndex != null)
            return mIndex;

        var index = new Dictionary<(KeyScope, KeyBinding), KeyCommand>();
        foreach (var cmd in mCommands.Values)
        {
            var gesture = Effective(cmd.Id);
            if (gesture == null)
                continue;
            index[(cmd.Scope, gesture.Value)] = cmd;   // 默认集内同 scope 无同手势冲突；UI 层负责用户重绑时的冲突提示
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
