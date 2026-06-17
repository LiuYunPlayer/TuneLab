using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TuneLab.Foundation;

namespace TuneLab.Agent;

// agent 模型设置的本地持久化（Configs/AgentSettings.json）。非敏感字段明文存；敏感字段（调用方按
// TextBoxConfig.IsPassword 标出）按平台用 SecretStore 保护：Windows=DPAPI 内联 blob 存文件；
// macOS/Linux=存进 OS 凭据库、文件只留引用（Sec="os"，account=engine:key）。任一不可用则降级明文并告警。
internal static class AgentSettingsStore
{
    sealed class Field
    {
        public string Kind { get; set; } = "s";   // s=string, n=number, b=bool
        public string Sec { get; set; } = "none";  // none=明文 / dpapi=Windows 内联 / os=外部凭据库
        public string? Str { get; set; }
        public double Num { get; set; }
        public bool Bool { get; set; }
    }

    sealed class StoreFile
    {
        public string? Engine { get; set; }
        public Dictionary<string, Field> Values { get; set; } = new();
    }

    static string FilePath => Path.Combine(PathManager.ConfigsFolder, "AgentSettings.json");

    public static (string? engine, Dictionary<string, PropertyValue> values) Load()
    {
        var result = new Dictionary<string, PropertyValue>();
        try
        {
            if (!File.Exists(FilePath))
                return (null, result);

            var store = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(FilePath));
            if (store == null)
                return (null, result);

            foreach (var (key, field) in store.Values)
            {
                switch (field.Kind)
                {
                    case "n": result[key] = field.Num; break;
                    case "b": result[key] = field.Bool; break;
                    default: result[key] = LoadSecretOrPlain(store.Engine, key, field); break;
                }
            }
            return (store.Engine, result);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to load agent settings: " + ex);
            return (null, result);
        }
    }

    static string LoadSecretOrPlain(string? engine, string key, Field field)
    {
        return field.Sec switch
        {
            "dpapi" => OperatingSystem.IsWindows() && field.Str != null ? SecretStore.DpapiUnprotect(field.Str) : string.Empty,
            "os" => SecretStore.OsRetrieve(Account(engine, key)) ?? string.Empty,
            _ => field.Str ?? string.Empty,
        };
    }

    public static void Save(string engine, PropertyObject values, IReadOnlySet<string> secretKeys)
    {
        try
        {
            var store = new StoreFile { Engine = engine };
            foreach (var kvp in values.Map)
            {
                var pv = kvp.Value;
                var field = new Field();
                switch (pv.Type)
                {
                    case PropertyType.Number:
                        pv.ToDouble(out var d); field.Kind = "n"; field.Num = d; break;
                    case PropertyType.Boolean:
                        pv.ToBool(out var b); field.Kind = "b"; field.Bool = b; break;
                    case PropertyType.String:
                        pv.ToString(out var s);
                        field.Kind = "s";
                        if (secretKeys.Contains(kvp.Key) && !string.IsNullOrEmpty(s))
                            SaveSecret(engine, kvp.Key, s, field);
                        else
                            field.Str = s;
                        break;
                    default:
                        continue;
                }
                store.Values[kvp.Key] = field;
            }

            PathManager.MakeSureExist(PathManager.ConfigsFolder);
            SaveFile.WriteAllText(FilePath, JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save agent settings: " + ex);
        }
    }

    static void SaveSecret(string engine, string key, string secret, Field field)
    {
        if (OperatingSystem.IsWindows())
        {
            field.Str = SecretStore.DpapiProtect(secret);
            field.Sec = "dpapi";
            return;
        }

        if (SecretStore.OsStore(Account(engine, key), secret))
        {
            field.Str = null; // 实际密钥在 OS 凭据库，文件不留
            field.Sec = "os";
            return;
        }

        // OS 凭据库不可用（如 Linux 未装 libsecret-tools / headless）：降级明文并告警。
        Log.Warning(string.Format("Secret '{0}' stored in plaintext: no OS keychain available on this platform.", key));
        field.Str = secret;
        field.Sec = "none";
    }

    static string Account(string? engine, string key) => string.Format("{0}:{1}", engine ?? "default", key);
}
