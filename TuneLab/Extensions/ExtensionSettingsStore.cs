using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuneLab.Agent;
using TuneLab.Foundation;
using TuneLab.Utils;

namespace TuneLab.Extensions;

// 扩展能力级设置的通用本地持久化（Configs/ExtensionSettings.json）。
// 值用【原生 JSON 存法】（与工程文件 .tlp 的 PropertyObject 存法一致）：string/number/bool 直接落原生 JSON，
// 不逐字段包 {Kind,Sec,...}——类型靠 JSON token 本身判定。顶层按 extensionKey(="kind:extensionId") 分桶。
// 密钥字段（调用方按 schema 的 TextBoxConfig.IsPassword 标出）经 SecretStore 保护：
//   Windows = DPAPI 密文就地存为字符串（仅原用户原机可解）；macOS = 进钥匙串(Keychain)、文件只留空串。
//   无安全存储可用时（如官方未支持的 Linux / headless）：【不保存该密钥字段】+ 告警——绝不明文落盘。
// 官方支持 Windows / macOS。文件【不存】"是否密钥/加密方式"标记——该信息来自 schema(IsPassword) + 当前平台，
// 故文件保持纯净原生。读时调用方传入 secretKeys（哪些字段需解密/从凭据库取回）。
// agent 自有侧边栏设置与 AgentSettingsStore，不走这里。
internal static class ExtensionSettingsStore
{
    static string FilePath => Path.Combine(PathManager.ConfigsFolder, "ExtensionSettings.json");
    static readonly JsonSerializerOptions sJsonOptions = new() { WriteIndented = true };

    // 读某 extension 的设置值。secretKeys（来自 schema 的 IsPassword）标出哪些字段需解密/从凭据库取回。
    public static Dictionary<string, PropertyValue> Load(string extensionKey, IReadOnlySet<string> secretKeys)
    {
        var result = new Dictionary<string, PropertyValue>();
        try
        {
            if (ReadRoot()?[extensionKey] is not JsonObject bucket)
                return result;

            foreach (var (key, node) in bucket)
            {
                if (secretKeys.Contains(key))
                    result[key] = LoadSecret(extensionKey, key, node);
                else if (ToPropertyValue(node, out var pv))
                    result[key] = pv;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load extension settings for " + extensionKey + ": " + ex);
        }
        return result;
    }

    static string LoadSecret(string extensionKey, string key, JsonNode? node)
    {
        if (OperatingSystem.IsWindows())
        {
            var blob = (node as JsonValue)?.TryGetValue<string>(out var s) == true ? s : string.Empty;
            return string.IsNullOrEmpty(blob) ? string.Empty : SecretStore.DpapiUnprotect(blob); // DPAPI 密文 → 明文
        }
        // macOS：真密钥在钥匙串，文件只存空串；取不到则空（不存在明文回退）。
        return SecretStore.OsRetrieve(Account(extensionKey, key)) ?? string.Empty;
    }

    // 落盘某 extension 的设置值（原生 JSON）；secretKeys 标出按 IsPassword 须保护的字段。其余 extension 的桶原样保留。
    public static void Save(string extensionKey, PropertyObject values, IReadOnlySet<string> secretKeys)
    {
        try
        {
            var root = ReadRoot() ?? new JsonObject();
            var bucket = new JsonObject();
            foreach (var kvp in values.Map)
            {
                if (secretKeys.Contains(kvp.Key))
                {
                    kvp.Value.ToString(out var secret);
                    if (string.IsNullOrEmpty(secret))
                    {
                        SecretStore.OsDelete(Account(extensionKey, kvp.Key)); // 清空：删凭据库旧值；文件也不写该字段
                        continue;
                    }
                    var node = SaveSecret(extensionKey, kvp.Key, secret);
                    if (node != null)                                          // 无安全存储则跳过该字段，绝不明文
                        bucket[kvp.Key] = node;
                    continue;
                }
                if (ToNode(kvp.Value, out var plain))
                    bucket[kvp.Key] = plain;
            }
            root[extensionKey] = bucket;

            PathManager.MakeSureExist(PathManager.ConfigsFolder);
            SaveFile.WriteAllText(FilePath, root.ToJsonString(sJsonOptions));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save extension settings for " + extensionKey + ": " + ex);
        }
    }

    // 密钥应写入文件的节点：Windows=DPAPI 密文；macOS=进钥匙串后存空串。
    // 无安全存储可用（官方未支持的 Linux / headless）→ 返回 null，调用方跳过该字段，绝不明文落盘。
    static JsonNode? SaveSecret(string extensionKey, string key, string secret)
    {
        if (OperatingSystem.IsWindows())
            return JsonValue.Create(SecretStore.DpapiProtect(secret)); // DPAPI 密文，仅原用户原机可解
        if (SecretStore.OsStore(Account(extensionKey, key), secret))
            return JsonValue.Create(string.Empty);                     // 真密钥进钥匙串，文件留空
        Log.Warning(string.Format("Extension secret '{0}' not saved: no secure store available on this platform.", key));
        return null;
    }

    // PropertyValue → 原生 JSON node（bool/number/string）。嵌套对象暂不支持（设置项均为标量），返回 false 跳过。
    static bool ToNode(PropertyValue value, out JsonNode? node)
    {
        if (value.ToBool(out var b)) { node = JsonValue.Create(b); return true; }
        if (value.ToDouble(out var d)) { node = JsonValue.Create(d); return true; }
        if (value.ToString(out var s)) { node = JsonValue.Create(s ?? string.Empty); return true; }
        node = null;
        return false;
    }

    // 原生 JSON node → PropertyValue（按 token 类型：bool→number→string）。
    static bool ToPropertyValue(JsonNode? node, out PropertyValue value)
    {
        value = default;
        if (node is not JsonValue jv)
            return false;
        if (jv.TryGetValue<bool>(out var b)) { value = b; return true; }
        if (jv.TryGetValue<double>(out var d)) { value = d; return true; }
        if (jv.TryGetValue<string>(out var s)) { value = s; return true; }
        return false;
    }

    static JsonObject? ReadRoot()
    {
        if (!File.Exists(FilePath))
            return null;
        return JsonNode.Parse(File.ReadAllText(FilePath)) as JsonObject;
    }

    static string Account(string extensionKey, string key) => string.Format("{0}:{1}", extensionKey, key);
}
