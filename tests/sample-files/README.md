# 测试输入文件

供测试时**导入**用的样例文件，按扩展名路由到对应 format 插件。voice 插件是引擎、不导入文件，故无对应样例。

| 文件 | 对应插件包 | 导入器行为 | 导入后预期 |
|---|---|---|---|
| `sample.tltest` | `v1-format` | **真解析 JSON** | 轨「tltest sample (parsed)」、bpm 128、5 个 note(do re mi fa so)——与插件内置 fallback(4 note/bpm120)不同，证明走了解析 |
| `sample.tloldfmt` | `legacy-format` | **真解析 JSON**(经 Compat) | 轨「tloldfmt sample (parsed)」、bpm 90、3 个 note(sol la ti) |
| `sample.tlsuite` | `v1-suite` | 忽略内容、出固定样例 | 轨名含 `[v1-suite]`(用到共享 Common) |
| `sample.tlconfa` | `v1-conflict-a` | 忽略内容、出固定样例 | 轨名含 `ConflictHelper v1.0.0.0 (pkg A)` |
| `sample.tlconfb` | `v1-conflict-b` | 忽略内容、出固定样例 | 轨名含 `ConflictHelper v2.0.0.0 (pkg B)` |
| `sample.tlm1` | `legacy-multi` | 忽略内容、出固定样例 | 轨名 `legacy-multi #1` |
| `sample.tlm2` | `legacy-multi` | 忽略内容、出固定样例 | 轨名 `legacy-multi #2` |
| `sample.tlnoasm` | `v1-no-assemblies` | 忽略内容、出固定样例 | 轨名含 `scanned (no assemblies declared)` |

**往返(round-trip)测试**：用 `sample.tltest` / `sample.tloldfmt` 导入后，再「导出」回同扩展名 → 重新导入 → 轨/note(pos/dur/pitch/lyric) 应一致。

导出测试无需预置文件（导出当前工程即可）。voice 合成测试也无需文件（新建音轨选对应声库写 note 即可）。
