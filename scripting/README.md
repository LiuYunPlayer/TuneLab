# scripting — TuneLab 脚本插件 façade 与线协议规范

脚本插件（Python / Node.js / Lua）以 **跨进程 sidecar** 接入 V1 扩展系统；
宿主侧设计见 [docs/scripted-plugin-sidecar-design.md](../docs/scripted-plugin-sidecar-design.md)。
本目录放各语言的 façade 包与**语言无关**的线协议规范（一份规范、三薄层实现）。

## 目录

| 路径 | 内容 | 状态 |
|---|---|---|
| `wire/v1.json` | 线协议规范（帧、方法表、值编码） | M1 ✅（format 面） |
| `python/tunelab/` | Python façade（纯 stdlib，PyPI 名 `tunelab`） | M1 ✅ |
| `node/` | Node façade `@tunelab/plugin` | M2 |
| `lua/` | Lua façade（纯 Lua 单文件 + LuaJIT FFI 能力探测） | M2 |

## 构建与测试（不进 .sln；与 dotnet 管线平行）

```
python -m unittest discover -s scripting/python/tests -v      # façade 单测（无第三方依赖）
# pip install -e scripting/python                             # 本地联调时给样例插件供包
```

线协议改动纪律（设计文档 §1.2）：不兼容改动 = `WireProtocol.Version` + 本目录规范文件**同批提档**；
加性新方法不提档。宿主 `tests/TuneLab.Tests/Scripted*` 与 `tests/ScriptedFakeSidecar` 是封条的另一端。
