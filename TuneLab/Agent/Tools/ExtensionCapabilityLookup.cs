using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TuneLab.Extensions;

namespace TuneLab.Agent;

// 「一句话定位某个能力位」的共用查找：agent 侧多个工具都要把模型给的一个串落到具体的
// (包, manifest 条目) 上，而它们认的写法必须**完全一致**——那是对模型的契约，一处多认一种写法、
// 另一处不认，模型就会在工具间来回试错。故收口在此，不各写各的。
//
// 认三种写法（与工具描述里承诺的一致）：
//   "kind:identity" 全形（如 "voice:my.engine"）/ 裸 identity（"mid"）/ 条目显示名。
// 多身份条目（多后缀 format）任一身份命中即算命中——它们共用同一份实现与说明。
internal static class ExtensionCapabilityLookup
{
    internal readonly record struct Match(ExtensionLoadResult Package, ExtensionEntryInfo Entry)
    {
        // "kind:id1,id2"：回报里给模型看的规范标签（多身份条目列全，免得它以为只关到一个）。
        public string Label => Entry.Kind + ":" + string.Join(",", Entry.Identities);
    }

    // packageId 非空则只在该包内找（同一身份跨包并存时的消歧手段）。
    public static IReadOnlyList<Match> Find(string query, string packageId = "")
    {
        var matches = new List<Match>();
        if (string.IsNullOrEmpty(query))
            return matches;

        foreach (var package in ExtensionManager.LoadResults)
        {
            if (packageId.Length > 0 && !string.Equals(package.Id, packageId, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var entry in package.Entries)
                if (Matches(entry, query))
                    matches.Add(new Match(package, entry));
        }
        return matches;
    }

    static bool Matches(ExtensionEntryInfo entry, string query)
    {
        if (string.Equals(entry.DisplayName, query, StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var id in entry.Identities)
        {
            if (string.Equals(entry.Kind + ":" + id, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(id, query, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    // 同一写法命中多个条目时【不猜】：列出候选要求模型带 packageId 再来（同 list_extension_settings 的规矩）。
    public static string AmbiguousError(string query, IReadOnlyList<Match> matches)
    {
        var sb = new StringBuilder();
        sb.Append('"').Append(query).Append("\" is ambiguous — ").Append(matches.Count)
          .Append(" capabilities match. Call again with packageId:");
        foreach (var (package, entry) in matches)
            sb.Append("\n- ").Append(entry.Kind).Append(':').Append(string.Join(",", entry.Identities))
              .Append(" \"").Append(entry.DisplayName).Append("\" — packageId=")
              .Append(string.IsNullOrEmpty(package.Id) ? "(legacy, no id)" : package.Id);
        return sb.ToString();
    }

    public static string NotFoundError(string query)
        => "Error: no installed capability matches \"" + query + "\". Call list_extensions to see what each package provides.";
}
