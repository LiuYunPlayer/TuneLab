using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuneLab.Foundation;
using TuneLab.SDK;

namespace TuneLab.Extensions.Scripted;

// 脚本 format 条目的进程内代理（设计文档 §5.4）：注册表眼里它与 in-proc IImportFormat/IExportFormat
// 不可分辨；每个方法体 = 一次 RPC（懒 spawn 由 SidecarHost 管）。
// 字节流不进消息体：导入把源字节落到暂存文件再传路径，导出让 sidecar 写暂存文件、宿主读回
// （FormatsManager 对输出流的"宿主拥有生命周期"约定因此原样成立）。
// 设置面（§5.7）：manifest `settings: true` 的条目才接 GetSettingsConfig/ApplySettings RPC；
// 探测式消费链（GetSettingsEntries 用工厂建 probe 实例 + as IExtensionSettings）与 .NET 插件共用。
internal sealed class RemoteFormat : IImportFormat, IExportFormat, IExtensionSettings
{
    readonly SidecarHost mHost;
    readonly string mEntryId;   // = sidecar 侧注册键（握手 provides 的 id；M1 = 包内 entry 路径）
    readonly bool mHasSettings;

    public RemoteFormat(SidecarHost host, string entryId, bool hasSettings = false)
    {
        mHost = host; mEntryId = entryId; mHasSettings = hasSettings;
    }

    ObjectConfig IExtensionSettings.GetSettingsConfig(IExtensionSettingsContext context)
    {
        if (!mHasSettings)
            return ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
        // schema 必须可先于 Init 调（设计 §5.7）：settings.* 方法不触发 spawn 预热之外的语义，
        // sidecar 的 initialize 本就先发生——EnsureRunning 天然满足次序。
        var reply = mHost.CallAsync("settings.get_config", new JsonObject { ["entry"] = mEntryId })
            .GetAwaiter().GetResult();
        return (IControllerConfig?)ScriptedConfigWire.ReadConfig(reply?["config"]) as ObjectConfig
            ?? ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>());
    }

    void IExtensionSettings.ApplySettings(PropertyObject settings)
    {
        if (!mHasSettings)
            return;
        // 未运行 = 值存宿主侧、下次握手随 entries[].settings 补发；运行中 = 即时通知。
        // （两条路径都在 SetSettings 内——ApplyPersisted 早于任何 spawn 的时序被天然接住。）
        mHost.SetSettings(mEntryId, ScriptedWire.SerializeNode(settings));
    }

    public ProjectInfo Deserialize(Stream stream)
    {
        var file = WriteTempPayload(stream);
        try
        {
            var reply = mHost.CallAsync("format.import", new JsonObject
            {
                ["entry"] = mEntryId,
                ["stream_ref"] = file,
            }).GetAwaiter().GetResult();
            var project = reply?["project"]?.Deserialize<ProjectInfo>(ScriptedWire.Options)
                ?? throw new InvalidDataException("sidecar format.import reply carries no 'project'");
            return project;
        }
        finally
        {
            TryDelete(file);
        }
    }

    public void Serialize(Stream output, ProjectInfo info)
    {
        var file = NextTempPath(".out");
        try
        {
            var projectNode = JsonSerializer.SerializeToNode(info, ScriptedWire.Options);
            mHost.CallAsync("format.export", new JsonObject
            {
                ["entry"] = mEntryId,
                ["stream_ref"] = file,
                ["project"] = projectNode,
            }).GetAwaiter().GetResult();
            using var input = File.OpenRead(file);
            input.CopyTo(output);
        }
        finally
        {
            TryDelete(file);
        }
    }

    string WriteTempPayload(Stream stream)
    {
        var file = NextTempPath(".in");
        using (var fs = File.Create(file))
            stream.CopyTo(fs);
        return file;
    }

    static string NextTempPath(string suffix)
    {
        var dir = Path.Combine(PathManager.TuneLabFolder, "Temp", "io-" + SidecarIo.Nonce);
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, Guid.NewGuid().ToString("N") + suffix);
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}

internal static class SidecarIo
{
    // 本次宿主启动的随机串：暂存目录/（M3 起的）shm 区域名都嵌它，上次崩溃的残留可整体识别。
    public static readonly string Nonce = Guid.NewGuid().ToString("N")[..12];
}
