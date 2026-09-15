using System;
using System.Collections.Generic;
using System.Linq;

namespace TuneLab.Extensions.Scripted;

// 一个可下载的运行时发行单元：候选镜像（按首选序，index0 优先）+ 校验和 + 归档布局。
// urls 是同一逻辑文件的镜像副本；sha256 空 = 未钉（fetcher 会下载但记警告，release 工具负责补钉）。
internal readonly record struct RuntimeFeedCell(
    string Flavor,
    Version Version,
    string Rid,
    IReadOnlyList<string> Urls,
    string Sha256,
    string Layout);          // "root"：归档根即 exe；"prefix"：归档裹一个顶层目录

// 内置镜像表（设计文档 §8.6）：脚本运行时的自动部署源。表是数据——release 工程补 sha256、
// 增版本/格子都不动 fetch 逻辑。镜像按 index0=国内优先排（npmmirror/华为云），官方源兜底；
// 真正用哪个由 RuntimeFetcher 的延迟探测临场裁决（网络环境无关的"自动判路"）。
internal static class RuntimeFeed
{
    // 已知格子（flavor/版本/rid）。sha256 为【已钉】值（境内镜像与官方源双下比对一致，见 §8.6 与
    // tools/build-runtime-package/README）；空串 = 未钉，fetcher 会下载但记警告，release 工具补钉。
    // Windows 的 python embeddable 与 node dist 先钉；unix standalone 布局随需要加。
    static readonly RuntimeFeedCell[] sCells = BuildCells();

    // python-3.13.3-embed-amd64.zip / node-v20.17.0-win-x64.zip（huawei & python.org、npmmirror & nodejs.org 交叉核对）
    const string Sha3133Amd64 = "59ff76e16e6597de47474fb22be69e7191a89116910d728ab735079b078e52db";
    const string ShaNode2017WinX64 = "e323fff0aba197090faabd29c4c23f334557ff24454324f0c83faa7e399dbb74";

    public static IReadOnlyList<RuntimeFeedCell> Cells => sCells;

    // 取满足 spec 的最高版本格子（同 rid）。无则 null。
    public static RuntimeFeedCell? Match(RuntimeSpec spec)
    {
        // 注意：struct 的 FirstOrDefault 会给出 default 值（HasValue=true 的空格子）——先转可空再取。
        return sCells
            .Where(c => string.Equals(c.Flavor, spec.Flavor, StringComparison.OrdinalIgnoreCase)
                     && RidMatches(c.Rid)
                     && spec.SatisfiedBy(c.Version.ToString(3)))
            .OrderByDescending(c => c.Version)
            .Select(c => (RuntimeFeedCell?)c)
            .FirstOrDefault();
    }

    static RuntimeFeedCell[] BuildCells()
    {
        var list = new List<RuntimeFeedCell>();

        // —— Python embeddable（Windows，无需安装器，解包即用；ARM64 自 3.13 起官方有产物）——
        foreach (var (ver, pyArch, rid, sha) in new (string, string, string, string)[]
                 { ("3.12.7", "amd64", "win-x64", ""), ("3.11.9", "amd64", "win-x64", ""),
                   ("3.13.3", "amd64", "win-x64", Sha3133Amd64), ("3.13.3", "arm64", "win-arm64", "") })
        {
            var file = $"python-{ver}-embed-{pyArch}.zip";
            list.Add(new RuntimeFeedCell("python", Parse(ver), rid, new[]
            {
                $"https://mirrors.huaweicloud.com/python/{ver}/{file}",
                $"https://registry.npmmirror.com/-/binary/python/{ver}/{file}",
                $"https://www.python.org/ftp/python/{ver}/{file}",
            }, sha, "root"));
        }

        // —— Node.js 发行（Windows zip，解包得 node.exe）——
        foreach (var (ver, nodeArch, rid, sha) in new (string, string, string, string)[]
                 { ("20.17.0", "win-x64", "win-x64", ShaNode2017WinX64), ("20.17.0", "win-arm64", "win-arm64", ""), ("18.20.4", "win-x64", "win-x64", "") })
        {
            var file = $"node-v{ver}-{nodeArch}.zip";
            list.Add(new RuntimeFeedCell("node", Parse(ver), rid, new[]
            {
                $"https://registry.npmmirror.com/-/binary/node/v{ver}/{file}",
                $"https://mirrors.huaweicloud.com/nodejs/v{ver}/{file}",
                $"https://npmmirror.com/mirrors/node/v{ver}/{file}",
                $"https://nodejs.org/dist/v{ver}/{file}",
            }, sha, "prefix"));
        }

        return list.ToArray();
    }

    static Version Parse(string s) => Version.Parse(s);

    // 当前进程 rid 归类：feed 里 win-x64/win-arm64 精确匹配；osx/linux 目前无格子。
    static bool RidMatches(string cellRid)
    {
        var cur = Utils.PlatformHelper.GetPlatform();      // "win-x64" / "osx-arm64" / ...
        return string.Equals(cellRid, cur, StringComparison.OrdinalIgnoreCase);
    }
}
