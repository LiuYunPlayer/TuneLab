using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using TuneLab.Foundation;

namespace TuneLab.Bridge;

// 命令桥的凭据文件：宿主把【管道名 + 一次性 token】写进用户数据目录，外部进程读它连回来。
//
// 为什么这样就够：文件在用户数据目录里，"谁能读它"由文件系统权限决定——那是操作系统已经做好的事，
// 比我们自己发明一套认证可靠得多。token 每次开桥现生成、关桥即删，故一份泄漏出去的旧文件连不上。
// pid 只用来给人排障（"这个文件是哪个进程留下的"），不参与鉴权。
internal readonly record struct BridgeCredentials(string PipeName, string Token, int ProcessId)
{
    public static string FilePath => Path.Combine(PathManager.ConfigsFolder, "CommandBridge.json");

    public static BridgeCredentials Create()
        => new("TuneLab.Command." + RandomToken(8), RandomToken(32), Environment.ProcessId);

    public void Write()
    {
        PathManager.MakeSureExist(PathManager.ConfigsFolder);
        var json = new JsonObject
        {
            ["version"] = BridgeProtocol.Version,
            ["pipe"] = PipeName,
            ["token"] = Token,
            ["pid"] = ProcessId,
        };
        File.WriteAllText(FilePath, json.ToJsonString());
    }

    // 关桥/退出时删掉。删不掉不是致命错（下次开桥会覆盖，且旧 token 的管道已经不在了）。
    public static void Delete()
    {
        try { if (File.Exists(FilePath)) File.Delete(FilePath); }
        catch (Exception ex) { Log.Info("Failed to delete the command bridge credentials: " + ex.Message); }
    }

    // 客户端侧：读凭据文件。文件不在 / 读不动 / 内容缺字段 → null（调用方据此说"宿主没开桥"）。
    public static BridgeCredentials? Read()
    {
        try
        {
            if (!File.Exists(FilePath))
                return null;
            if (JsonNode.Parse(File.ReadAllText(FilePath)) is not JsonObject json)
                return null;
            var pipe = json["pipe"]?.GetValue<string>();
            var token = json["token"]?.GetValue<string>();
            if (string.IsNullOrEmpty(pipe) || string.IsNullOrEmpty(token))
                return null;
            return new BridgeCredentials(pipe, token, json["pid"]?.GetValue<int>() ?? 0);
        }
        catch (Exception ex)
        {
            Log.Info("Failed to read the command bridge credentials: " + ex.Message);
            return null;
        }
    }

    // 写这份文件的进程还在不在。留着的目的只有一个：连不上时能分清"宿主关了但文件没删"与
    // "宿主在、但桥出了别的问题"——两者的下一步完全不同。
    public bool IsHostRunning()
    {
        if (ProcessId <= 0)
            return false;
        try { return !Process.GetProcessById(ProcessId).HasExited; }
        catch (ArgumentException) { return false; }   // 进程已不存在
        catch (Exception) { return true; }            // 查不了（权限等）就别下"它没了"的结论
    }

    static string RandomToken(int bytes) => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();
}
