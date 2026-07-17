using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TuneLab.Agent;
using TuneLab.Foundation;
using TuneLab.SDK;
using TuneLab.Utils;

namespace TuneLab.Extensions;

// 扩展能力级设置的通用本地持久化（Configs/ExtensionSettings.json）。
// 值用【原生 JSON 存法】（与工程文件 .tlp 的 PropertyObject 存法一致），值转换走全仓唯一共用的
// PropertyJsonUtils（string/number/bool 直落原生 JSON、array/object 递归，类型靠 JSON token 本身判定）。
// 【两级分桶】顶层按 packageId（插件包反向域名 id）分桶，桶内再按 extensionKey(="kind:extensionId")。
//   理由：不同插件包可能实现相同类型且 id 相同（如两个包都做 format "svp" / voice 同名引擎），仅按 extensionKey
//   分桶会令其设置互相覆盖、串味。加一层 packageId 使各包设置物理隔离。用包 id 作【JSON key】而非文件名——
//   避开包 id 含非法文件名字符 / 保留名 / 大小写 / 超长等文件系统坑。
//   packageId 取值：ExtensionManager.BuiltInPackageId（"(built-in)"）= 编进宿主的内置能力；legacy 老包 = 其目录名（无 V1 id）；真实反向域名 id = V1 安装包。
// 密钥字段（调用方按 schema 的 TextBoxConfig.IsPassword 标出）经 SecretStore 保护：
//   Windows = DPAPI 密文就地存为字符串（仅原用户原机可解）；macOS = 进钥匙串(Keychain)、文件只留空串。
//   无安全存储可用时（如官方未支持的 Linux / headless）：【不保存该密钥字段】+ 告警——绝不明文落盘。
// 官方支持 Windows / macOS。文件【不存】"是否密钥/加密方式"标记——该信息来自 schema(IsPassword) + 当前平台，
// 故文件保持纯净原生。读时调用方传入 secretKeys（哪些字段需解密/从凭据库取回）。
// agent 模型 provider 的设置也走这里（extensionKey="agent-model:<id>"），但其 UI 在 agent 侧边栏、不在设置窗口扩展页。
internal static class ExtensionSettingsStore
{
    static string FilePath => Path.Combine(PathManager.ConfigsFolder, "ExtensionSettings.json");

    // 读某 extension 的设置值（先定位 packageId 桶、再定位 extensionKey 子桶）。
    // secretKeys（来自 schema 的 IsPassword）标出哪些字段需解密/从凭据库取回。
    public static Dictionary<string, PropertyValue> Load(string packageId, string extensionKey, IReadOnlySet<string> secretKeys)
    {
        var result = new Dictionary<string, PropertyValue>();
        try
        {
            if (ReadRoot()?[packageId] is not JObject pkg || pkg[extensionKey] is not JObject bucket)
                return result;

            foreach (var property in bucket.Properties())
            {
                if (secretKeys.Contains(property.Name))
                    result[property.Name] = LoadSecret(packageId, extensionKey, property.Name, property.Value);
                else
                    result[property.Name] = PropertyJsonUtils.ToPropertyValue(property.Value);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load extension settings for " + packageId + "/" + extensionKey + ": " + ex);
        }
        return result;
    }

    // 两遍读：先原生读出值供 schema 求值得 IsPassword 集（密钥是否为字段不依赖其自身值），再据此读取并解密密钥。
    // schemaOf 给定当前值返回该 extension 的 config——这样存储层不必存"是否密钥"标记，文件保持纯净原生。
    public static Dictionary<string, PropertyValue> Load(string packageId, string extensionKey, Func<PropertyObject, ObjectConfig> schemaOf)
    {
        var raw = Load(packageId, extensionKey, sNoSecrets);
        var secrets = PasswordKeys(schemaOf(ToPropertyObject(raw)));
        return secrets.Count == 0 ? raw : Load(packageId, extensionKey, secrets);
    }

    // schema 里标了 IsPassword 的字段键（= 须加密/解密的密钥字段）。
    public static HashSet<string> PasswordKeys(ObjectConfig config)
    {
        var set = new HashSet<string>();
        foreach (var kv in config.Properties)
            if (kv.Value is TextBoxConfig tb && tb.IsPassword)
                set.Add(kv.Key.Id);
        return set;
    }

    public static PropertyObject ToPropertyObject(IReadOnlyDictionary<string, PropertyValue> values)
    {
        var map = new Map<string, PropertyValue>();
        foreach (var kv in values)
            map.Add(kv.Key, kv.Value);
        return new PropertyObject(map);
    }

    static readonly IReadOnlySet<string> sNoSecrets = new HashSet<string>();

    static string LoadSecret(string packageId, string extensionKey, string key, JToken? node)
    {
        if (OperatingSystem.IsWindows())
        {
            var blob = node?.Type == JTokenType.String ? (string?)node ?? string.Empty : string.Empty;
            return string.IsNullOrEmpty(blob) ? string.Empty : SecretStore.DpapiUnprotect(blob); // DPAPI 密文 → 明文
        }
        // macOS：真密钥在钥匙串，文件只存空串；取不到则空（不存在明文回退）。
        return SecretStore.OsRetrieve(Account(packageId, extensionKey, key)) ?? string.Empty;
    }

    // 落盘某 extension 的设置值（原生 JSON）；secretKeys 标出按 IsPassword 须保护的字段。
    // 其余包 / 同包其余 extension 的桶原样保留（只替换 root[packageId][extensionKey] 这一格）。
    public static void Save(string packageId, string extensionKey, PropertyObject values, IReadOnlySet<string> secretKeys)
    {
        try
        {
            var root = ReadRoot() ?? new JObject();
            var pkg = root[packageId] as JObject ?? new JObject();
            var bucket = new JObject();
            foreach (var kvp in values.Map)
            {
                if (secretKeys.Contains(kvp.Key))
                {
                    kvp.Value.ToString(out var secret);
                    if (string.IsNullOrEmpty(secret))
                    {
                        SecretStore.OsDelete(Account(packageId, extensionKey, kvp.Key)); // 清空：删凭据库旧值；文件也不写该字段
                        continue;
                    }
                    var node = SaveSecret(packageId, extensionKey, kvp.Key, secret);
                    if (node != null)                                          // 无安全存储则跳过该字段，绝不明文
                        bucket[kvp.Key] = node;
                    continue;
                }
                // 稀疏语义同 PropertyJsonUtils.ToJson(PropertyObject)：哨兵不写键（absent = 默认）。
                if (kvp.Value.IsNull() || kvp.Value.IsMultiple())
                    continue;
                bucket[kvp.Key] = PropertyJsonUtils.ToJson(kvp.Value);
            }
            pkg[extensionKey] = bucket;
            root[packageId] = pkg;

            PathManager.MakeSureExist(PathManager.ConfigsFolder);
            SaveFile.WriteAllText(FilePath, root.ToString(Formatting.Indented));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save extension settings for " + packageId + "/" + extensionKey + ": " + ex);
        }
    }

    // 密钥应写入文件的节点：Windows=DPAPI 密文；macOS=进钥匙串后存空串。
    // 无安全存储可用（官方未支持的 Linux / headless）→ 返回 null，调用方跳过该字段，绝不明文落盘。
    static JToken? SaveSecret(string packageId, string extensionKey, string key, string secret)
    {
        if (OperatingSystem.IsWindows())
            return new JValue(SecretStore.DpapiProtect(secret)); // DPAPI 密文，仅原用户原机可解
        if (SecretStore.OsStore(Account(packageId, extensionKey, key), secret))
            return new JValue(string.Empty);                     // 真密钥进钥匙串，文件留空
        Log.Warning(string.Format("Extension secret '{0}' not saved: no secure store available on this platform.", key));
        return null;
    }

    static JObject? ReadRoot()
    {
        if (!File.Exists(FilePath))
            return null;
        return JToken.Parse(File.ReadAllText(FilePath)) as JObject;
    }

    // SecretStore account（macOS 钥匙串账户名 / DPAPI 仅作文件内 blob）：含 packageId 确保跨包唯一。
    static string Account(string packageId, string extensionKey, string key) => string.Format("{0}:{1}:{2}", packageId, extensionKey, key);
}
