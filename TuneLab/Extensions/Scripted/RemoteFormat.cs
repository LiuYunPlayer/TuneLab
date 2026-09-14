using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using TuneLab.SDK;

namespace TuneLab.Extensions.Scripted;

// 脚本 format 条目的进程内代理（设计文档 §5.4）：注册表眼里它与 in-proc IImportFormat/IExportFormat
// 不可分辨；每个方法体 = 一次 RPC（懒 spawn 由 SidecarHost 管）。
// 字节流不进消息体：导入把源字节落到暂存文件再传路径，导出让 sidecar 写暂存文件、宿主读回
// （FormatsManager 对输出流的"宿主拥有生命周期"约定因此原样成立）。
internal sealed class RemoteFormat : IImportFormat, IExportFormat
{
    readonly SidecarHost mHost;
    readonly string mEntryId;   // = sidecar 侧注册键（握手 provides 的 id；M1 = 包内 entry 路径）

    public RemoteFormat(SidecarHost host, string entryId)
    {
        mHost = host; mEntryId = entryId;
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
