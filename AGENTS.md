# TuneLab — notes for AI coding assistants

## Build & test

- Build: `dotnet build TuneLab.sln -c Debug`
- Tests: `dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj` and `dotnet test legacy/compat/TuneLab.Hosting.Compat.Legacy.Tests/TuneLab.Hosting.Compat.Legacy.Tests.csproj`
- Sample plugins under `tests/plugins/*/` are NOT in the solution — build them individually after
  changing any SDK surface. After an SDK-surface change, redeploying them takes **three steps**, not
  one — skipping the last two leaves the running app loading a stale binary (symptom:
  `MissingMethodException` for an SDK member that was renamed/removed):
  1. **Build** with `-t:Rebuild` (plain `dotnet build` may judge a plugin "up to date" and skip it,
     re-deploying the old dll). Each csproj's `OutputPath` writes into `tests/packages/<slug>/`.
  2. **Pack**: `pwsh tests/pack-tlx.ps1` → bundles `tests/packages/*` into `tests/tlx/*.tlx`.
  3. **Install**: `pwsh tests/install-tlx.ps1 [names...]` → extracts each `.tlx` into
     `%APPDATA%\TuneLab\Extensions\<manifest-name>\`, which is where the app actually loads from.
      **TuneLab must be closed** first — a running instance locks the extension dlls.

## Scripted plugins (sidecar) — see docs/scripted-plugin-sidecar-design.md

- Python/Node/Lua plugins run **out of process** (JSON-RPC over stdio); M0–M3d landed
  (format/effect/voice, all three languages).
  Façades live under `scripting/` (not in the .sln); wire spec: `scripting/wire/v1.json`.
- Build & test (parallel to the dotnet pipeline):
  - fake sidecar (protocol seal, no interpreter needed): built with the solution; the .NET test
    project launches it via `dotnet exec`. Manual: `dotnet build tests/ScriptedFakeSidecar -c Debug`.
  - Python façade: `python -m unittest discover -s scripting/python/tests` (pure stdlib).
  - Node façade: `node --test scripting/node/test` (needs node>=18; zero-dependency package at
    `scripting/node/tunelab/` — samples resolve it via `NODE_PATH=<repo>/scripting/node`).
  - Lua façade: `lua tests/test_codec.lua` in `scripting/lua/` (pure Lua 5.1–5.4; no packaged
    lua on windows — CI posix runs it; samples resolve via `LUA_PATH=<repo>/scripting/lua/?.lua;;`).
  - Real-interpreter E2E: `dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj --filter "runtime=needs-interpreter"`
    (set `TUNELAB_REQUIRE_PYTHON=1` / `TUNELAB_REQUIRE_NODE=1` / `TUNELAB_REQUIRE_LUA=1` to
    fail-fast when the interpreter is not discoverable, else it passes vacuously;
    `TUNELAB_PYTHON` / `TUNELAB_NODE` / `TUNELAB_LUA` pin the path — probing order: settings → env →
    runtime packages → PATH).
- Scripted test plugins (e.g. `tests/plugins/PyFormat/`) are manifest + sources only — **no csproj,
  no build step**; `pack-tlx.ps1` picks csproj-less folders under `tests/plugins/` up directly.
- `ProtocolVersion` (wire protocol, `Scripted/WireProtocol.cs`) is a third, independent version axis —
  do not align it with `SdkVersion` or `AssemblyVersion`. Bump it together with a new
  `scripting/wire/v<N>.json` only on incompatible frame/method changes (design doc §1.2).

## ⚠️ Frozen public ABI: TuneLab.SDK & TuneLab.Foundation

These two assemblies are the plugin contract. Their public API is frozen and guarded by
PublicApiAnalyzers: every public signature is declared in each project's
`PublicAPI.Shipped.txt`, and **RS0016 / RS0017 build errors fire on any change**.

When you hit RS0016/RS0017, it is an alarm, not an obstacle. Do **not** mechanically edit
the txt files just to make the build green:

1. Accidental change (refactor spillover) → revert the code, leave the txt files alone.
2. Intentional **additive** API (new member/type) → plugin-implemented interfaces need a
   DIM default body; declare the signature in `PublicAPI.Unshipped.txt`
   (`dotnet format analyzers <csproj> --diagnostics RS0016`), and call out the new API
   explicitly when presenting your change.
3. Intentional **breaking** change (delete/alter a shipped signature) → **stop and ask the
   maintainer first.** Never edit `PublicAPI.Shipped.txt` on your own initiative.

Full evolution rules (interface classification, DTO shape policy, enum tolerance, release
workflow): `docs/sdk-api-evolution.md`.

Related invariants: `AssemblyVersion` of both assemblies is pinned to 2.0.0.0 forever (see
csproj comments — do not "align" it with the release version); the manifest `sdk-version`
gate (`ExtensionManager.SdkVersion`) is a separate version axis and must be bumped when
shipping new API.

## 用户手册

`docs/user-manual.zh-CN.md` 是**唯一真相源**，同时有三个消费者，改它要意识到这几件事：

- **随软件发布**：`TuneLab.csproj` 用 `Link` 把它与 `docs/images/manual/*.png` 落进输出目录的 `Resources\Manual\`（仓库里不留副本）。
- **应用内手册窗**（帮助 → 用户手册 / `F1`，`TuneLab/UI/Manual/ManualWindow.cs`）与 **agent 的 `get_manual` 工具**共用 `TuneLab/Docs/ManualLibrary.cs` 的章节切分。
- **章节锚点**：每个 `##` 前的 `<!-- section: id -->` 注释是稳定引用（标题/章号可变，id 不变）。删改锚点会让 `ManualLibraryTests` 失败——那是防漂移的哨兵，不是障碍。

插图与快捷键表都是生成物，不是手工截屏：

```powershell
pwsh tools/ScreenshotBot/shoot.ps1     # -> docs/images/manual/*.png + docs/generated/keybindings.md
```

它用 Avalonia headless + 真 Skia 渲染跑真实的 `App`/`MainWindow`（不开真窗口），数据目录经 `TUNELAB_DATA_DIR`
重定向到临时沙盒（不读也不写开发机的 `%APPDATA%\TuneLab`）。**改了界面就重跑一次。**
原理、怎么加图、已知的坑见 [tools/ScreenshotBot/README.md](tools/ScreenshotBot/README.md)。

## Release notes 不能出现代码语法

应用内的更新弹窗直接渲染 GitHub release 正文（服务端 /api/app/get-update 把**最新** release
的 body 原样转发给客户端）。1.5.10 和 1.6.0 这两版客户端渲染任何 Markdown 代码元素——行内
反引号、三反引号围栏、四空格缩进块——都会当场抛异常，而弹窗建在 async void 里没人接管异常，
进程直接退出：老用户一开软件就闪退，且崩在弹窗渲染前，连「忽略此版本」都点不到。这些客户端
在用户机器上，改不了，只能约束我们写的正文。

所以写 release notes（以及任何会被搬进 release 正文的文案）时不要用代码语法，把 .tlx、F1、
WAVE_FORMAT_EXTENSIBLE 这类词直接写成正文。其余 Markdown 语法（标题、粗体、列表、表格、
链接、图片、emoji）都验证过是安全的。

两道闸：`CIUtils/check-release-notes.py` 用 CommonMark 解析判定（嵌套列表的四空格缩进不会
误报）；`.github/workflows/check-release-notes.yml` 在 release 发布或正文被编辑时跑它，
`upload-release.yml` 则对 CI 自动生成的正文用 `--fix` 就地净化。

## Conventions

- Code comments may be written in Chinese; log messages, assertions and exception messages
  are English.
- One concept, one word: check `docs/naming-glossary.md` before naming a new API or writing docs,
  and add a row there whenever you resolve a synonym pair.
- Do not extract csproj settings into `Directory.Build.props` — the SDK-layer csproj files
  are part of the public contract and stay self-contained.
- `agent-model` is a host-internal module (not a plugin type); new LLM adapters go in via
  PR — see `docs/agent-model-adapters.md`.
