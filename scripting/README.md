# scripting — TuneLab 脚本插件 façade 与线协议规范

脚本插件（Python / Node.js / Lua）以 **跨进程 sidecar** 接入 V1 扩展系统；
宿主侧设计见 [docs/scripted-plugin-sidecar-design.md](../docs/scripted-plugin-sidecar-design.md)。
本目录放各语言的 façade 包与**语言无关**的线协议规范（一份规范、三薄层实现）。

## 目录

| 路径 | 内容 | 状态 |
|---|---|---|
| `wire/v1.json` | 线协议规范（帧、方法表、值编码、shm 纹理） | M4b ✅（47 方法 + 音频面协商） |
| `python/tunelab/` | Python façade（纯 stdlib，PyPI 名 `tunelab`；`audio_plane: shm`） | M4b ✅（format/effect/voice + shm 音频面） |
| `node/tunelab/` | Node façade（零依赖 CommonJS，包名 `tunelab`，发布名 `@tunelab/plugin`；inline） | M3d ✅（format/effect/voice） |
| `lua/tunelab.lua` | Lua façade（纯 Lua 单文件，5.1–5.4；inline；内嵌 JSON codec 与 f32 打包双路） | M3d ✅（CI posix 真跑） |

## 构建与测试（不进 .sln；与 dotnet 管线平行）

```
python -m unittest discover -s scripting/python/tests -v      # Python façade 单测（无第三方依赖）
node --test scripting/node/test                                # Node façade 单测（需 node>=18）
cd scripting/lua && lua tests/test_codec.lua                   # Lua façade 单测（需 lua5.1+；win 本机无 = CI 跑）
# pip install -e scripting/python                              # 本地联调时给样例插件供包
```

真解释器 E2E（C# 侧）默认 vacuous；置 `TUNELAB_REQUIRE_PYTHON/NODE/LUA=1` 变强制。
Node 供包用 `NODE_PATH=<repo>/scripting/node`，Lua 用 `LUA_PATH=<repo>/scripting/lua/?.lua;;`。

线协议改动纪律（设计文档 §1.2）：不兼容改动 = `WireProtocol.Version` + 本目录规范文件**同批提档**；
加性新方法不提档。宿主 `tests/TuneLab.Tests/Scripted*` 与 `tests/ScriptedFakeSidecar` 是封条的另一端。
