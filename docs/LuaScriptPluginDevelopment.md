# TuneLab Lua 脚本插件开发指南

> 面向**插件开发者**：如何用 **Lua** 为 TuneLab 编写 `format` / `effect` / `voice` 脚本插件。
>
> - 与 [PythonScriptPluginDevelopment.md](PythonScriptPluginDevelopment.md)、[NodeJSScriptPluginDevelopment.md](NodeJSScriptPluginDevelopment.md) 结构对齐：**线协议同一套**，差别在语言 façade 的命名、异步模型（Lua 无 async，同步 RPC）与依赖打包。
> - 进程外执行单元术语 **sidecar**（[naming-glossary.md](naming-glossary.md)）；宿主侧设计/语言无关规范见 [scripted-plugin-sidecar-design.md](scripted-plugin-sidecar-design.md) 与 [`scripting/wire/v1.json`](../scripting/wire/v1.json)。

**关键事实**：Lua 插件由宿主 spawn 成独立 `lua` 进程（sidecar），走 **JSON-RPC 2.0 over stdio**。façade 是**单文件纯 Lua**（`scripting/lua/tunelab.lua`，内嵌 JSON codec 与 base64/f32 打包），兼容 **PUC Lua 5.1–5.4 与 LuaJIT**。

**Lua 的三点独有约束**（务必先记）：

1. **无异步**：反向调用（`ctx.input_read` / `ctx.evaluate` …）是**同步阻塞**函数，直接返回结果表——没有 `await`。
2. **音频面 = inline**：PUC Lua 自报 `capabilities.audio_plane="inline"`，PCM 走 base64（无 stdlib mmap）。功能等价，不经共享内存。
3. **表面更“薄”、更贴线**：段是**整数 id**（`ctx.output(...)` 返回 id，再 `ctx.write(seg, off, samples)` / `ctx.commit(seg)`），`ctx.input_read` 直接给你宿主回复的原语表。数组要序列化成 `[]`（而非 `{}`）时用 `tunelab.empty_array()`。`ctx.evaluate`/f64 载荷需 **Lua 5.2+** 的 `string.pack/unpack`。

> 现实取舍：**Lua 最成熟、有维护样例的路径是 `format`**。`effect`/`voice` 的 Lua façade 面已实现，但样例集目前只充分演练了 format；要写“读整段输入并原样偏移输出”的音频引擎时，注意下面 §6 标注的几何/偏移注意点——重音频引擎优先考虑 Python（有 shm 快车道与完整样例）。

---

## 目录

1. [运行前提与获取解释器](#1-运行前提与获取解释器)
2. [包结构与 manifest.json](#2-包结构与-manifestjson)
3. [执行模型与生命周期](#3-执行模型与生命周期)
4. [第一个插件：format](#4-第一个插件format)
5. [数据形状规范：ProjectInfo](#5-数据形状规范projectinfo)
6. [effect 插件](#6-effect-插件)
7. [voice 插件](#7-voice-插件)
8. [settings 设置面板](#8-settings-设置面板)
9. [日志与诊断](#9-日志与诊断)
10. [交互规范](#10-交互规范宿主契约--你的义务)
11. [代码规范](#11-代码规范)
12. [依赖 vendoring](#12-依赖-vendoring)
13. [本地调试与打包](#13-本地调试与打包)
14. [完整 Demo（format）](#14-完整-demoformat)
15. [常见坑 / FAQ](#15-常见坑--faq)

---

## 1. 运行前提与获取解释器

manifest 写 `"runtime": "lua>=5.1"`（或 `"luajit>=2.1"`），宿主四级探测：**设置指定 → 环境变量 `TUNELAB_LUA`/`TUNELAB_LUAJIT` → `type:"runtime"` 资源包 → PATH（`lua`/`lua5.4`）**。

> **Windows 上 Lua 无自动分发**：平台矩阵上 Windows *能承载* Lua（PUC C89 可便携编译），但宿主的【下载运行时】镜像表**不发 windows-lua 格**——没有可钉 sha256 的便携 zip 发行件、预编译版只有 NSIS 安装器（违反免执行纪律）。故 Windows 上请：**自带编译好的 `lua.exe` 放进 PATH**、或用 `TUNELAB_LUA` 指定、或打一个 `type:"runtime"` 资源包。macOS/Linux（apt/brew 出 `lua`）则开箱即用，CI 也在 posix 上真跑 Lua 用例。

---

## 2. 包结构与 manifest.json

```
MyLuaPlugin/
├── manifest.json
└── plugin/
    ├── main.lua          # entry 指向它
    └── tunelab.lua       # vendoring 的单文件 façade（§12）
```

字段同通用规则，只 flavor 不同：

| 字段 | 必需 | 说明 |
|---|---|---|
| `id` / `name` / `version` / `author` / `description` | id/name ✓ | 同通用。 |
| `sdk-version` | ✓ | 脚本插件写 `"1.1"`。 |
| `type` | ✓ | `format` / `effect` / `voice`。 |
| `runtime` | ✓ | `"lua>=5.1"`（PUC）或 `"luajit>=2.1"`。 |
| `entry` | ✓ | 包内相对路径，如 `plugin/main.lua`；不能越出包目录。 |
| `suffixes` / `import-suffixes` / `export-suffixes` | format | 同通用。 |
| `engine` | effect/voice ✓ | 引擎 id。 |
| `platforms` | | `["win","osx","linux"]`。 |
| `settings` | | 布尔。 |
| `deps` | | 只用 `vendored`/留空。 |

```json
{
  "id": "com.example.luafmt",
  "name": "Lua Format (txt notes)",
  "version": "1.0.0",
  "sdk-version": "1.1",
  "type": "format",
  "suffixes": ["lpx"],
  "runtime": "lua>=5.1",
  "entry": "plugin/main.lua",
  "platforms": ["win", "osx", "linux"]
}
```

---

## 3. 执行模型与生命周期

与 Python/Node 版一致：**懒 spawn → `initialize` 握手核对 `provides` → epoch 世代 → 崩溃 backoff 重拉 + 10 分钟 5 次熔断 → effect 回退上版 / voice 会话重建 → 心跳兜底 → 取消尽力通知 → stdin EOF 即退**。Lua 补充：

- **同步无并发**：一条 RPC 处理完才读下一条；不存在“事件循环被堵”，但一个死循环的 `synthesize` 同样会让心跳超时→被判僵→重拉。长循环请轮询 `ctx.cancelled`。
- **`os.exit` 慎用**：façade 收到 `shutdown` 会自己收尾；用户代码里 `os.exit(0)` 会跳过优雅应答。

> **不要用 `print`**——stdout 是协议信道。日志用 `tunelab.info/warn/error`（§9）。

---

## 4. 第一个插件：format

`tunelab.format{ import_project = function(path) ... end, export_project = function(path, project) ... end }`。**`stream_ref`/`path` 是宿主铺好的文件路径**：import 读它、export 写它，用完自动删。

```lua
package.path = package.path .. ";./vendor/?.lua"      -- vendored façade 优先（§12）
local tunelab = require("tunelab")

local function lines(s)                               -- 小工具：按行切
  local out = {}
  for line in (s .. "\n"):gmatch("([^\n]*)\n") do
    if line ~= "" and line:sub(1,1) ~= "#" then out[#out+1] = line end
  end
  return out
end

tunelab.format({
  import_project = function(path)
    local f = assert(io.open(path, "r")); local text = f:read("*a"); f:close()
    local notes = {}
    for _, line in ipairs(lines(text)) do
      local p, pos, dur, lyric = line:match("^(%S+)%s+(%S+)%s+(%S+)%s*(.*)$")
      if p then
        notes[#notes+1] = {
          pitch = tonumber(p), pos = tonumber(pos), dur = tonumber(dur),
          lyric = (lyric ~= "" and lyric) or "la", pronunciation = "",
          properties = { source = "lpx" },             -- 自由值，自动 tagged
          leadingPhonemes = tunelab.empty_array(), bodyPhonemes = tunelab.empty_array(),
          bodyOffset = 0.0,
        }
      end
    end
    return {
      tempos = { { pos = 0.0, bpm = 120.0 } },
      timeSignatures = { { barIndex = 0, numerator = 4, denominator = 4 } },
      tracks = { {
        name = "Lua Track", gain = 0.0, pan = 0.0, mute = false, solo = false, asRefer = true, color = "",
        parts = { {
          kind = "midi", name = "P", pos = 0.0, startOffset = 0.0, endOffset = 0.0,
          gain = 0.0, soundSource = { kind = "voice", type = "", id = "" },
          effects = tunelab.empty_array(), notes = notes,
          automations = {}, piecewiseAutomations = {},
          pitch = { segments = tunelab.empty_array() }, vibratos = tunelab.empty_array(), properties = {},
        } },
      } },
    }
  end,

  export_project = function(path, project)
    local out = {}
    for _, track in ipairs((project or {}).tracks or {}) do
      for _, part in ipairs(track.parts or {}) do
        for _, n in ipairs(part.notes or {}) do
          out[#out+1] = string.format("%s %s %s %s", n.pitch, n.pos, n.dur, n.lyric or "la")
        end
      end
    end
    local f = assert(io.open(path, "w")); f:write(table.concat(out, "\n") .. (#out > 0 and "\n" or "")); f:close()
  end,
})

tunelab.serve()
```

- **空数组要显式**：Lua 里 `{}` 既像空对象又像空数组——任何“线上必须是 `[]`”的字段（空的 `notes`/`effects`/`segments`/`properties` 列表）用 `tunelab.empty_array()` 包一下，否则会被编码成 `{}`。这是 Lua 版最容易踩的编码坑。
- 只读格式省略 `export_project`。多格式用 `tunelab.format{ entry = "a", ... }` 对齐 manifest `entries[].entry`。
- 结尾 `tunelab.serve()`。

---

## 5. 数据形状规范：ProjectInfo

同通用：`tracks → parts → notes`，键名 **camelCase**（`soundSource`、`leadingPhonemes`、`bodyOffset`、`timeSignatures`、`piecewiseAutomations`）；note 的 `pos/dur` 为 tick、`pitch` 半音、PartInfo 判别 `"kind":"midi"|"audio"`。`properties` 自由值自动打 tag/还原（`{source="lpx"}` 回来仍是表）。特殊浮点读写两端自动转字符串字面量（`0/0`、`math.huge`）。`Multiple` 不跨线。**再强调空数组：凡是宿主期望 `[]` 的，用 `tunelab.empty_array()`。**

---

## 6. effect 插件

`tunelab.effect("引擎id", { ... })`。引擎表钩子（**均 snake_case**）：`init` / `destroy` / `get_property_config(effects)` / `get_automation_configs(effects)` / `get_synthesized_parameter_configs(effects)` / `create_session(ctx)`（返回“会话用户表”，省略则用引擎表自身当会话）。会话表钩子：`process(ctx)` / `cancel` / `dispose`。

```lua
local tunelab = require("tunelab")

-- façade 只导出 base64（_unb64）与编码（_b64_encode_f32）；PCM 的 f32-LE 解码要你自己用
-- string.unpack 完成（需 Lua 5.2+）。下面这个本地小 helper 在 effect/voice 读输入时反复用到。
local function decode_f32(b64str)
  local raw = tunelab._unb64(b64str)
  local out, i = {}, 1
  while i <= #raw do out[#out+1], i = string.unpack("<f", raw, i) end
  return out
end

local Gain = {}

function Gain.init(self) self.gain = 0.5 end
function Gain.destroy(self) self.gain = 0 end

function Gain.get_property_config(self, effects)
  return { type = "object", properties = { {
    id = "amount",
    config = { type = "slider", defaultValue = 0.5, scale = { kind = "linear", min = 0.0, max = 1.0 } },
  } } }
end
function Gain.get_automation_configs(self, effects) return tunelab.empty_array() end
function Gain.get_synthesized_parameter_configs(self, effects) return tunelab.empty_array() end

function Gain.process(self, ctx)
  -- ctx.input_read 直接给宿主回复表：{ data = <base64 f32-LE>, sample_rate = <n> }
  local r = ctx.input_read(0, 0)                      -- count=0 ⇒ 读整段输入
  local rate = r.sample_rate
  local data = decode_f32(r.data)
  local count = #data
  local seg = ctx.output(0, count, rate)              -- 输出段（offset 见下方注意）
  local out = {}
  for i = 1, count do out[i] = data[i] * self.gain end
  ctx.write(seg, 0, out)                              -- seg 是 id，out 是 f32 数值数组
  ctx.commit(seg)
  local t0, t1 = 0, count / rate
  ctx.status({ { t0, t1, "synthesized", 1.0 } })
  ctx.parameters({ loudness = { segments = { { { t0, 0.4 }, { t1, 0.4 } } } } })
end
function Gain.cancel(self) end
function Gain.dispose(self) end

tunelab.effect("LuaGain", Gain)
tunelab.serve()
```

会话 `ctx`（Lua，均同步，段用 id）：

| 方法 | 作用 |
|---|---|
| `ctx.input_read(offset, count)` | 返回 `{ data = <base64 f32-LE>, sample_rate }`（`count=0/nil` 读整段）；PCM 用上面的 `decode_f32`（`tunelab._unb64` + `string.unpack`）解成逐样本 |
| `ctx.properties(keys)` | part 级参数值（已解码为原生表） |
| `ctx.automation_tracks()` | 自动化轨 id 列表 |
| `ctx.evaluate(track, positions)` | 曲线求值，返回 `list`（**需 Lua 5.2+** 的 `string.pack`；`positions` 非降序） |
| `ctx.output(offset, count, rate)` | 新建输出段，返回**段 id**（记入 `ctx.segments` 供自动回收） |
| `ctx.write(seg, offset, samples)` / `ctx.commit(seg)` | 写 / 提交（`samples` = 数字数组，f32） |
| `ctx.status(segments)` / `ctx.parameters(map)` | 发布状态 / 回显（effect 会话发 `effect.*`） |
| `ctx.segments` / `ctx.cancelled` | 段 id 列表 / 取消标志 |

> **几何注意（Lua 特有）**：`host.input_read` 回复只含 `data` + `sample_rate`，**不回绝对 `sample_offset`**，而 Lua façade 也未把 create_session 携带的 `input.sample_offset` 透成 `ctx.input`。所以上例把输出放在 offset 0——对“作用于段首”的效果正确，但若 effect 挂在非零起点段上、又需要严格对齐绝对轴，请改用 Python/Node（它们的 `ctx.input`/`inputGeometry()` 带完整几何）。这是当前 Lua façade 面较薄之处。

---

## 7. voice 插件

`tunelab.voice("引擎id", { ... })`。引擎钩子：`init`/`destroy`/`catalog()`（返回 `sources, layout`）/`automation_configs(parts)`/`synthesized_parameter_configs(parts)`/`part_property_config(parts)`/`note_property_config(part, notes)`/`phoneme_property_configs(part, notes)`/`create_session(ctx)`。会话钩子：`default_lyric`（字段）、`is_continuation(note)`、`peek(start, end)`、`synthesize(start, end)`、`on_dirty(kind, payload)`、`cancel`、`dispose`。

```lua
local tunelab = require("tunelab")
local math = require("math")

local Sine = {}
function Sine.init(self) self.pos = 0.0 end
function Sine.destroy(self) end

function Sine.catalog(self)
  return { { id = "sine", name = "Sine Bank", description = "440Hz" } }, tunelab.empty_array()
end
function Sine.automation_configs(self, parts) return tunelab.empty_array() end
function Sine.synthesized_parameter_configs(self, parts) return tunelab.empty_array() end
function Sine.part_property_config(self, parts) return { type = "object", properties = tunelab.empty_array() } end
function Sine.note_property_config(self, part, notes) return { type = "object", properties = tunelab.empty_array() } end
function Sine.phoneme_property_configs(self, part, notes) return tunelab.empty_array() end

function Sine.is_continuation(self, note) return false end
function Sine.peek(self, start, end)
  if self.pos >= end then return nil end
  return { math.max(start, self.pos), math.min(self.pos + 0.5, end) }
end
function Sine.synthesize(self, start, end)
  if start < self.pos then return end                  -- 幂等：已合成的块直接返回
  local rate = 44100
  local count = math.floor((end - start) * rate)
  local seg = self.ctx.output(math.floor(start * rate), count, rate)
  local buf = {}
  for i = 0, count - 1 do buf[i+1] = 0.2 * math.sin(2 * math.pi * 440 * i / rate) end
  self.ctx.write(seg, 0, buf)
  self.ctx.commit(seg)
  self.ctx.status({ { start, end, "synthesized", 1.0 } })   -- voice 会话自动路由到 voice.status
  self.ctx.pitch({ { { start, 60.0 }, { end, 62.0 } } })
  local notes = self.ctx.notes()
  local syl = {}
  for _, n in ipairs(notes) do
    if n.end > start and n.start < end then
      syl[n.id] = { tunelab.empty_array(), { { symbol = n.lyric or "a", duration = n.end - n.start, stretch_weight = 1.0 } }, 0.0 }
    end
  end
  self.ctx.phonemes(syl)                               -- key = note id（回指 request_id）
  self.pos = end
end
function Sine.on_dirty(self, kind, payload) end
function Sine.cancel(self) end
function Sine.dispose(self) end

function Sine.create_session(self, ctx)
  -- 会话直接复用引擎表，但把 ctx 挂上（本例单会话；多会话请 new 一个带 ctx 的对象）
  local s = setmetatable({}, { __index = Sine }); s.ctx = ctx; s.pos = 0.0; return s
end

tunelab.voice("LuaSine", Sine)
tunelab.serve()
```

- voice 会话 `ctx` 比 effect 多：`ctx.notes()`（宿主回复的音符表数组，含 `id/start/end/pitch/lyric/body/leading/body_offset/properties`）、`ctx.snapshot(note_ids)`（返回 `snapshot` 原语表）、`ctx.pitch(segments)`、`ctx.phonemes(by_note_id)`；`ctx.status`/`ctx.parameters` 在 voice 会话会自动发到 `voice.*`（façade 按 `kind` 路由，不像 Node 需 `statusPublished`）。
- **`peek`/`synthesize` 的 `end` 是 Lua 关键字**：façade 内部已用 `params["end"]` 读取，你写用户函数形参 `function Sine.peek(self, start, end)` 时 `end` 只是普通形参名（合法，因为不是 `params.end` 的属性访问）——但如果你自己构造 `{start=…, end=…}` 的表要按线协议发，记得键 `["end"]`。
- 曲线求值 `ctx.evaluate` 需 Lua 5.2+；voice 的 `snapshot` 里若不用求值器可绕过该约束。

---

## 8. settings 设置面板

manifest `settings:true`。在 format handler 表 / 引擎表上实现 `get_settings_config()`（返回 config 树）与 `apply_settings(values)`（值已解码）。config 节点类型同通用：`slider/checkbox/textbox/combo/number/path/automation/object`；自定义 lambda/scale 不跨线。

```lua
tunelab.format({
  import_project = function(path) ... end,
  get_settings_config = function()
    return { type = "object", properties = {
      { id = "quality", config = { type = "combo", items = {
        { value = { s = "low" }, displayText = "低" },
        { value = { s = "high" }, displayText = "高" } }, default = { s = "high" } } },
    } }
  end,
  apply_settings = function(values) Q = values.quality end,
})
```

---

## 9. 日志与诊断

```lua
tunelab.info("加载完成")       -- level=info
tunelab.warn("缺字段用默认")   -- warning
tunelab.error("失败: " .. e)   -- error
tunelab.log("info", "...")     -- 通用
```

进宿主日志系统。**别 `print`**。崩溃栈/stderr 滚进包目录 `logs/stderr-<boot>.txt`（单行 8KB、本 boot 2MB、旧 boot 留 2 份）——排查“sidecar 起来又死”第一眼看它。日志/错误消息用英文。

---

## 10. 交互规范

同通用宿主契约与义务（独立进程崩溃隔离、真值经反向调用、恢复路径、inline 音频面）。你要履行：**stdout 只跑协议**（禁 `print`）、**别长阻塞**（心跳/取消兜底）、**产物必 `commit`**、**`peek`/`synthesize` 幂等**（电平语义可重问）、**`positions` 非降序**、**特殊浮点交给 façade**、**写不越界**、**一个进程一世代**。Lua 补充：**空数组用 `tunelab.empty_array()`**；**段是 id 不是对象**（`ctx.write(seg, …)`）。

---

## 11. 代码规范

- 入口 `main.lua` 只做：扩 `package.path` → `require("tunelab")` → 注册（`tunelab.format/effect/voice`）→ `tunelab.serve()`。
- **命名**：façade 钩子一律 snake_case（`import_project`/`get_property_config`/`create_session`/`on_dirty`）；`ctx.*` 也是 snake_case。ProjectInfo 的键仍是 camelCase。
- **空数组纪律**：任何宿主期望 `[]` 的字段用 `tunelab.empty_array()`（§4/§5）。这是 Lua 版第一大坑。
- **版本兼容**：只用 5.1–5.4/LuaJIT 交集 API；`string.pack`/`//` 整除是 5.3+/部分 5.2 特性——用到 `ctx.evaluate` 即需 5.2+，manifest 就写 `"lua>=5.2"`。
- **确定性/纯**：合成逻辑别依赖时间/随机源（缓存一致性）；错误让 façade 转 RPC error，别静默返回半成品。

---

## 12. 依赖 vendoring

单文件 façade，vendoring 最轻：把 `scripting/lua/tunelab.lua` 拷进包里（如 `plugin/vendor/tunelab.lua`），入口首行：

```lua
package.path = package.path .. ";./vendor/?.lua;./?.lua"
local tunelab = require("tunelab")
```

第三方纯 Lua 模块同放 `vendor/`。**别依赖需要 C 编译的 rock**（sidecar 无编译链、跨平台负担大）。

---

## 13. 本地调试与打包

- 开发期：包丢进扩展目录（Windows `%APPDATA%\TuneLab\Extensions\<包>\`），重新加载；看 `logs/stderr-*.txt`。
- 指定解释器：`TUNELAB_LUA=<path>`（Windows 需自带 `lua.exe`，§1）。
- 联调 façade（不落包）：`LUA_PATH=<repo>/scripting/lua/?.lua;;`。
- 本仓库测试面：
  ```bash
  cd scripting/lua && lua tests/test_codec.lua          # façade 单测（posix；Windows 本机通常无 lua）
  dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj --filter "runtime=needs-interpreter"
  #   TUNELAB_REQUIRE_LUA=1 让“缺 lua”判红（Windows 上一般保持 vacuous）
  ```
- 打包 `.tlx`：`pwsh tests/pack-tlx.ps1` → `pwsh tests/install-tlx.ps1 <名>`（先关 TuneLab）。

---

## 14. 完整 Demo（format）

**`.lpx` 文本格式包**（只读+写一行一音符）——与 §4 代码一一对应，是仓库里有维护样例的 Lua 路径：

```
LuaFmtDemo/
├── manifest.json          # §2 示例（suffixes ["lpx"]）
└── plugin/
    ├── main.lua           # §4 全代码
    └── vendor/tunelab.lua # 从 scripting/lua/tunelab.lua 拷来（§12）
```

`song.lpx`：

```
60 0 480 la
62 480 240 du
65 720 720 mi
```

导入 → 三音符 MIDI part（note 的 `properties.source=="lpx"` 应回得去）；导出字节等价。effect/voice 见 §6/§7（注意 §6 的几何注意）。

---

## 15. 常见坑 / FAQ

- **空数组变空对象**：`{}` 被编码成 `{}` 而非 `[]`，宿主读不到列表。→ 空列表一律 `tunelab.empty_array()`。**Lua 头号坑**。
- **`ctx.evaluate` 报 `need Lua 5.2+`**：PUC 5.1 无 `string.pack`。→ 用 5.2+/5.3+/LuaJIT(带 pack)，或 manifest 写 `"lua>=5.2"`，或避开求值器。
- **`params.end` 语法错**：`end` 是关键字，读宿主回复里的区间结束键要写 `params["end"]`（façade 内部已这么处理；你自己构造/读取时同理）。
- **effect 段偏移不对**：Lua `input_read` 不回绝对 `sample_offset`、façade 未透 `ctx.input`（§6 几何注意）。非零起点段的严格对齐请用 Python/Node。
- **段写成对象崩**：Lua 的段是**整数 id**，调 `ctx.write(seg, offset, samples)` / `ctx.commit(seg)`，不是 `seg:write(...)`。
- **读到的输入是 base64 字符串**：Lua façade 不导出 f32 解码器，`ctx.input_read().data` 是 base64；用 §6 的 `decode_f32`（`tunelab._unb64` + `string.unpack("<f",…)`，需 5.2+）自行解码。编码回写 PCM 则 `ctx.write` 内部已用 `_b64_encode_f32` 处理，你只给数值数组。
- **Windows 探测不到 lua**：无自动分发（§1）。自带 `lua.exe` 进 PATH 或 `TUNELAB_LUA` 指。
- **握手 `does not declare entry`**：manifest 的 `engine`/`entry` 与 `tunelab.effect("X",...)`/`format{entry=...}` 的键没对齐。
- **产物不出现**：`ctx.write(seg,...)` 后忘了 `ctx.commit(seg)`。
- **`print` 把会话搞崩**：stdout 是协议信道，日志请用 `tunelab.*`。

---

*协议/生命周期完整设计*：[scripted-plugin-sidecar-design.md](scripted-plugin-sidecar-design.md) + [`scripting/wire/v1.json`](../scripting/wire/v1.json)。Python / Node 版见同目录另两份指南。
