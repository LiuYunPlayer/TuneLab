# TuneLab Node.js 脚本插件开发指南

> 面向**插件开发者**：如何用 **Node.js** 为 TuneLab 编写 `format` / `effect` / `voice` 三类脚本插件。
>
> - 与 Python 版 [PythonScriptPluginDevelopment.md](PythonScriptPluginDevelopment.md)、Lua 版 [LuaScriptPluginDevelopment.md](LuaScriptPluginDevelopment.md) 结构对齐：**线协议同一套**，差别只在语言 façade 的方法命名、异步模型与依赖打包方式。
> - 进程外执行单元术语为 **sidecar**（[naming-glossary.md](naming-glossary.md)）；宿主侧设计与语言无关规范见 [scripted-plugin-sidecar-design.md](scripted-plugin-sidecar-design.md) 与 [`scripting/wire/v1.json`](../scripting/wire/v1.json)。
> - 本文只讲 Node 这一侧怎么写、怎么跑、怎么打包。

**关键事实**：Node 插件由宿主 spawn 成独立 `node` 进程（sidecar），走 **JSON-RPC 2.0 over stdio**（`Content-Length` 帧）。façade 包名 `tunelab`（CommonJS，零依赖，发布名 `@tunelab/plugin`）。

**Node 的两处独有约束**（务必先记）：

1. **全异步**：所有反向调用（读输入、求值、建段）返回 `Promise`——处理器写成 `async`、`await` 它们。
2. **音频面 = inline**：Node façade 声明 `capabilities.audio_plane="inline"`（CommonJS 无内建 mmap），PCM 走 base64。功能与 Python 完全等价，只是大音频不经共享内存。

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
14. [完整 Demo](#14-完整-demo)
15. [常见坑 / FAQ](#15-常见坑--faq)

---

## 1. 运行前提与获取解释器

manifest 写 `"runtime": "node>=18"`，宿主加载期按**四级探测**找解释器：**设置指定 → 环境变量 `TUNELAB_NODE` → `type:"runtime"` 资源包 → PATH（`node`）**。探测落空 → 侧栏 `Skipped · 未找到 Node 运行时` + **【下载运行时】**按钮（宿主按内置镜像表直发上游发行件，本地组装成 runtime 包登记、自动重载）。开发期本机有个 Node ≥18 即可（或 `TUNELAB_NODE=<path>` 钉死）。

---

## 2. 包结构与 manifest.json

```
MyNodePlugin/
├── manifest.json
└── plugin/
    ├── main.js           # entry 指向它（CommonJS：require/module.exports）
    └── vendor/           # 可选：vendored tunelab / 第三方依赖（§12）
```

manifest 字段与 Python 版**完全同构**，只有 flavor 前缀不同：

| 字段 | 必需 | 说明 |
|---|---|---|
| `id` / `name` / `version` / `author` / `description` | id/name ✓ | 同通用规则。 |
| `sdk-version` | ✓（要探测时） | 脚本插件写 `"1.1"`。 |
| `type` | ✓ | `format` / `effect` / `voice`。 |
| `runtime` | ✓ | 例 `"node>=18"`（`node>=18,<21` 之类区间都行）。 |
| `entry` | ✓ | 包内相对路径入口脚本，如 `plugin/main.js`；不能越出包目录。 |
| `suffixes` / `import-suffixes` / `export-suffixes` | format | 同 Python。 |
| `engine` | effect/voice ✓ | 引擎 id，如 `NodeGain`。 |
| `platforms` | | `["win","osx","linux"]`。 |
| `settings` | | 布尔，声明设置面板（§8）。 |
| `deps` | | 只用 `vendored`/留空；`"pip"` 未实现（`Skipped`）。 |

```json
{
  "id": "com.example.njsjson",
  "name": "Node JSON Format",
  "version": "1.0.0",
  "sdk-version": "1.1",
  "type": "format",
  "suffixes": ["tnjson"],
  "runtime": "node>=18",
  "entry": "plugin/main.js",
  "platforms": ["win", "osx", "linux"]
}
```

---

## 3. 执行模型与生命周期

与 Python 版一致（懒 spawn、`initialize` 握手核对 `provides`、**epoch** 世代、崩溃 backoff + 10 分钟 5 次熔断、effect 回退上版/voice 会话重建、心跳兜底、取消尽力通知、stdin EOF 即退）。Node 侧额外两条：

- **事件循环别堵**：`process`/`synthesize` 是 async，别在里面跑同步死循环（会饿死心跳/取消的 IO 回调）。
- **未处理拒绝**：`unhandledRejection` 会让进程崩；façade 的 dispatch 会 catch 用户异常转 RPC error，但你自己 `spawn` 的异步链要保证 catch，否则表现为反复重启→熔断。

> **不要 `console.log` 到 stdout**——那是协议信道。日志用 `tunelab.info/warn/error`（§9）；`console.error` 到 stderr 会被宿主捕获但不进日志系统。

---

## 4. 第一个插件：format

`require("tunelab")` 后用 `tunelab.format({ importProject, exportProject, ... })` 注册。`stream_ref` 是宿主铺好的**文件路径**（读入文件 in / 写出文件 out，用完自动删）。Node 里 `importProject` 可以是 `async`（`await` 读文件）。

```js
const fs = require("fs");
let tunelab;
try { tunelab = require("tunelab"); }              // vendored（§12）
catch { tunelab = require("./vendor/tunelab"); }  // 兜底：明确放 vendor/ 下

tunelab.format({
  importProject(streamRef) {
    const notes = [];
    for (const line of fs.readFileSync(streamRef, "utf8").split("\n")) {
      const s = line.trim();
      if (!s || s.startsWith("#")) continue;
      const [p, pos, dur, lyric] = s.split(/\s+/);
      notes.push({
        pitch: Number(p), pos: Number(pos), dur: Number(dur),
        lyric: lyric || "la", pronunciation: "",
        properties: { source: "njs" },              // 自由值，自动 tagged（§5）
        leadingPhonemes: [], bodyPhonemes: [], bodyOffset: 0.0,
      });
    }
    return {
      tempos: [{ pos: 0.0, bpm: 120.0 }],
      timeSignatures: [{ barIndex: 0, numerator: 4, denominator: 4 }],
      tracks: [{
        name: "Node Track", gain: 0, pan: 0, mute: false, solo: false, asRefer: true, color: "",
        parts: [{
          kind: "midi", name: "P", pos: 0.0, startOffset: 0.0, endOffset: 0.0,
          gain: 0, soundSource: { kind: "voice", type: "", id: "" },
          effects: [], notes, automations: {}, piecewiseAutomations: {},
          pitch: { segments: [] }, vibratos: [], properties: {},
        }],
      }],
    };
  },

  exportProject(streamRef, project) {
    const lines = [];
    for (const track of (project || {}).tracks || [])
      for (const part of track.parts || [])
        for (const n of part.notes || [])
          lines.push(`${n.pitch} ${n.pos} ${n.dur} ${n.lyric || "la"}`);
    fs.writeFileSync(streamRef, lines.join("\n") + (lines.length ? "\n" : ""));
  },
});

tunelab.serve();
```

- 只读格式省略 `exportProject`（manifest 也别声明写后缀）。多格式用 `tunelab.format({ entry: "a", ... })`，与 manifest `entries[].entry` 对齐。
- 结尾 `tunelab.serve()` 必须有。

---

## 5. 数据形状规范：ProjectInfo

同 Python 版：`tracks → parts → notes`，键名 **camelCase**，note 的 `pos/dur` 为 tick、`pitch` 半音、PartInfo 判别 `"kind":"midi"|"audio"`。`properties` 自由值由 façade 自动打 tag（`{source:"njs"}` 回来仍是对象）。`NaN/±Infinity` 出口自动转字符串字面量（Node `JSON.stringify` 会把裸 `NaN` 变 `null`，façade 在出口统一兜住——你直接用 `Number.NaN`/`Infinity` 即可）。`Multiple` 不跨线。

---

## 6. effect 插件

`tunelab.effect("NodeGain", engine)` 注册引擎对象；宿主对每个实例化调用 `create_session(ctx)`（注意 Node 里**这个方法名是 snake_case**，与下面的配置 getter 的 camelCase 不同——见 §15）。会话对象实现 `process(ctx)` 等。所有 ctx 方法**返回 Promise**。

```js
const tunelab = require("tunelab");

tunelab.effect("NodeGain", {
  async init() { this.gain = 0.5; },
  async destroy() {},

  getPropertyConfig(effects) {                       // camelCase 声明钩子
    return { type: "object", properties: [{
      id: "amount",
      config: { type: "slider", defaultValue: 0.5, scale: { kind: "linear", min: 0, max: 1 } },
    }]};
  },
  getAutomationConfigs() { return []; },
  getSynthesizedParameterConfigs() { return []; },

  create_session(ctx) { return new GainSession(this); },   // snake_case 工厂钩子
});

class GainSession {
  constructor(engine) { this.engine = engine; }
  async process(ctx) {
    const g = await ctx.inputGeometry();             // { sample_offset, sample_count, sample_rate }
    const { data } = await ctx.inputRead(0, g.sample_count);   // data: Float32Array
    const seg = await ctx.outputSegment(g.sample_offset, g.sample_count, g.sample_rate);
    const out = new Float32Array(data.length);
    for (let i = 0; i < data.length; i++) out[i] = data[i] * this.engine.gain;
    await seg.write(0, out);
    await seg.commit();                              // commit = 进下游链的闸门
    const t0 = g.sample_offset / g.sample_rate;
    const t1 = (g.sample_offset + g.sample_count) / g.sample_rate;
    ctx.status([[t0, t1, "synthesized", 1.0]]);      // effect.status
    ctx.parameters({ loudness: { segments: [[[t0, 0.4], [t1, 0.4]]] } });
  }
  cancel() {}
  async dispose() { for (const s of this.segments || []) await s.dispose().catch(() => {}); }
}

tunelab.serve();
```

Node effect 会话 `ctx` 方法（异步）：

| 方法 | 作用 |
|---|---|
| `await ctx.inputGeometry()` | 输入段几何 `{sample_offset, sample_count, sample_rate}` |
| `await ctx.inputRead(offset, count?)` | `{ data: Float32Array, sampleRate }` |
| `await ctx.properties(keys)` | part 级参数值（解码为原生） |
| `await ctx.automationTracks()` | 自动化轨 id 数组 |
| `await ctx.evaluate(track, positions)` | 曲线求值（`Float64Array`/数组，**非降序**；返回 `number[]`） |
| `await ctx.output(off,cnt,rate)` / `outputSegment(...)` | 建输出段（`outputSegment` 会登记到 `ctx.segments` 供统一回收）→ 段有 `await seg.write(off, Float32Array|array)` / `await seg.commit()` / `resize` / `dispose` |
| `ctx.status([...])` | 发 `effect.status`：`[[start,end,"synthesized",progress?],…]` |
| `ctx.parameters({...})` | 发回显曲线 `{id:{segments:[[[t,v],…],…]}}` |
| `ctx.cancelled` | 布尔，长任务轮询 |

---

## 7. voice 插件

`tunelab.voice("NodeSine", engine)`。引擎钩子：`catalog`、`automationConfigs`、`synthesizedParameterConfigs`、`partPropertyConfig`、`notePropertyConfig`、`phonemePropertyConfigs`（camelCase）+ `create_session`（snake）。会话钩子：`defaultLyric`（属性）、`isContinuation`、`peek`、`synthesize`、`onDirty`、`cancel`、`dispose`。

**⚠ Node 特例**：voice 状态发布用 **`ctx.statusPublished([...])`**（`ctx.status` 是基类的 `effect.status`，voice 要用 `statusPublished` 才发 `voice.status`）。

```js
const tunelab = require("tunelab");

tunelab.voice("NodeSine", {
  async init() {}, async destroy() {},
  catalog() {                                          // 可 async 可同步
    return [{ id: "sine", name: "Sine Bank", description: "440Hz" }], [[]];
  },
  automationConfigs() { return []; },
  synthesizedParameterConfigs() { return []; },
  partPropertyConfig() { return { type: "object", properties: [] }; },
  notePropertyConfig() { return { type: "object", properties: [] }; },
  phonemePropertyConfigs() { return []; },
  create_session(ctx) { return new SineSession(ctx); },
});

class SineSession {
  constructor(ctx) { this.ctx = ctx; this.pos = 0.0; }
  defaultLyric = "la";                                 // 会话级属性
  isContinuation(note) { return false; }
  peek(start, end) {
    if (this.pos >= end) return null;
    return [Math.max(start, this.pos), Math.min(this.pos + 0.5, end)];
  }
  async synthesize(start, end) {
    if (start < this.pos) return;
    const notes = await this.ctx.notes();
    const inWin = notes.filter((n) => n.end > start && n.start < end);
    const snap = await this.ctx.snapshot(inWin);
    const rate = 44100, count = Math.floor((end - start) * rate);
    const times = Array.from({ length: 64 }, (_, i) => start + (end - start) * i / 63);
    const curve = await snap.pitch.evaluate(times);    // 曲线面远程求值（非降序）
    const seg = await this.ctx.outputSegment(Math.floor(start * rate), count, rate);
    const buf = new Float32Array(count);
    for (let i = 0; i < count; i++) buf[i] = 0.2 * Math.sin(2 * Math.PI * 440 * i / rate);
    await seg.write(0, buf);
    await seg.commit();
    this.ctx.statusPublished([[start, end, "synthesized", 1.0]]);      // ← voice.status
    this.ctx.pitch([[[start, 60.0], [end, 62.0]]]);
    const syl = {};
    for (const n of snap.notes)
      syl[n.id] = [[], [{ symbol: n.lyric || "a", duration: n.end - n.start, stretchWeight: 1.0 }], 0.0];
    this.ctx.phonemes(syl);                            // key = note id（回指 request_id）
    this.pos = end;
  }
  onDirty(kind, payload) {}
  cancel() {}
  async dispose() { for (const s of this.ctx.segments) await s.dispose().catch(() => {}); }
}

tunelab.serve();
```

voice 会话 `ctx`：`await ctx.notes()`（`NoteView`：`id/start/end/pitch/lyric/bodyOffset/leading/body/properties`）、`await ctx.snapshot(ids)`（`{ notes, partProperties, pitch, pitchDeviation, automations }`，求值器带 `await .evaluate(positions)`）、`await ctx.outputSegment(...)`、`ctx.statusPublished/.pitch/.phonemes/.parameters`。`phonemes` 每项 `[leading, body, bodyOffset]`。

---

## 8. settings 设置面板

同通用协议：manifest `settings:true`，引擎/format handler 上实现 `getSettingsConfig()`（返回 config 树）与 `applySettings(values)`（值已解码为原生；camelCase 方法名）。config 节点类型同 §（Python 版 §8）：`slider/checkbox/textbox/combo/number/path/automation/object`；lambda/自定义 scale 不跨线。

```js
tunelab.format({
  importProject(ref) { /* ... */ },
  getSettingsConfig() {
    return { type: "object", properties: [
      { id: "quality", config: { type: "combo",
        items: [{ value: { s: "low" }, displayText: "低" }, { value: { s: "high" }, displayText: "高" }],
        default: { s: "high" } } },
      { id: "volume", config: { type: "slider", defaultValue: 0.8, scale: { kind: "linear", min: 0, max: 1 } } },
    ]};
  },
  applySettings(values) { this.q = values.quality; },
});
```

---

## 9. 日志与诊断

```js
tunelab.info("加载完成");      // log.write level=info
tunelab.warn("缺字段用默认");  // warning
tunelab.error("失败: " + e);   // error
```

- 进宿主日志系统。**别 `console.log` 到 stdout**；`console.error` 走 stderr（被捕获但不入日志）。
- 崩溃栈/stderr 滚进包目录 `logs/stderr-<boot>.txt`（单行 8KB、本 boot 2MB 硬顶、旧 boot 留 2 份）——排查“起来又死”第一眼看它。
- 日志/异常消息用英文（仓库约定）。

---

## 10. 交互规范

宿主保证（独立进程、真值经反向调用、恢复路径、Python 才有 shm 快车道——**Node 恒 inline**）、你的义务都同 [Python 版 §10](#10-交互规范宿主契约--你的义务)：stdout 只跑协议、别堵事件循环、`commit` 才算数、`peek/synthesize` 幂等、`positions` 非降序、特殊浮点交给 façade、写不越界、一个进程一世代。Node 补充：**所有反向调用是 Promise，忘记 `await` = 拿到 pending 对象乱崩**。

---

## 11. 代码规范

- 入口 `main.js` 只做：`require tunelab` → 注册 → `tunelab.serve()`；重逻辑拆模块 `require` 进来。
- CommonJS（`require`/`module.exports`）——façade 是零依赖 CommonJS 包。
- **命名混合是 façade 现状**（见 §15）：ctx/段方法是 camelCase，但两个“工厂/会话”钩子 `create_session` 是 snake_case。照抄本文示例最稳。
- PCM 用 `Float32Array`（`seg.write`/`inputRead` 都收/给 Float32Array），别用普通数组绕序列化。
- 音频/合成逻辑保持**确定性 + 纯**（可单测）。

---

## 12. 依赖 vendoring

只支持 vendored。façade 是本地包（未上 npm），把 `scripting/node/tunelab/` 整个拷进 `plugin/vendor/tunelab/`，入口：

```js
const path = require("path");
module.paths.unshift(path.join(__dirname, "vendor"));   // 或 require("./vendor/tunelab")
const tunelab = require("tunelab");
```

第三方纯 JS 依赖 `npm install` 到 `plugin/node_modules/`（随包发）；别装原生模块（跨平台 + sidecar 无编译链）。

---

## 13. 本地调试与打包

- 开发期：包文件夹丢进扩展目录（Windows `%APPDATA%\TuneLab\Extensions\<包名>\`），重新加载；看 `logs/stderr-*.txt`。
- 钉解释器：`TUNELAB_NODE=<path>`。联调 façade：`NODE_PATH=<repo>/scripting/node`。
- 本仓库测试面：
  ```bash
  node --test scripting/node/test                        # façade 单测
  dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj --filter "runtime=needs-interpreter"
  #   TUNELAB_REQUIRE_NODE=1 让“缺 Node”判红
  ```
- 打包 `.tlx`：`pwsh tests/pack-tlx.ps1` → `pwsh tests/install-tlx.ps1 <名>`（脚本包无 csproj，直接收编目录；先关 TuneLab）。

---

## 14. 完整 Demo

**只读+写的 `.tnjson` 文本格式包**：

```
NodeJsonDemo/
├── manifest.json          # 见 §2 示例
└── plugin/main.js         # 即 §4 全代码（require fs → tunelab.format(...) → serve()）
```

放 `song.tnjson`：

```
60 0 480 la
62 480 240 du
65 720 720 mi
```

导入 → 三音符 MIDI part；导出应字节等价。effect（§6 `NodeGain`）、voice（§7 `NodeSine`）各抄一段即可挂上。三家族一句话 Demo 同 [Python 版 §14](#14-完整-demo)。

---

## 15. 常见坑 / FAQ

- **`create_session is not a function` / 引擎被当会话用**：Node 里工厂钩子是 snake_case `create_session`（不是 `createSession`），而 config getter 是 camelCase `getPropertyConfig`——命名混合是 façade 现状，照示例写。
- **voice 状态不更新**：用了 `ctx.status`（那是 effect.status）；voice 要 `ctx.statusPublished(...)`。
- **拿到 Promise 而不是数据**：忘了 `await` 一个反向调用。
- **`write` 里 PCM 全 0 或乱码**：传了普通 number[] 且长度/类型不对；用 `Float32Array`。
- **导入即崩、反复重启→熔断**：`serve()` 前的顶层代码同步阻塞或抛异常；或未 catch 的 `unhandledRejection`。把逻辑挪进回调、加 try/catch。
- **握手 `does not declare entry`**：manifest `engine`/`entry` 与 `tunelab.effect("X")`/`format({entry})` 的键没对齐。
- **产物不出现**：`seg.write` 了没 `await seg.commit()`。
- **大音频慢**：Node 恒 inline base64（设计如此，非 bug）；真要快车道用 Python（shm）。
- **依赖找不到**：确认 `vendor/tunelab` 或 `NODE_PATH`；发布包里 `require("tunelab")` 前必须已把 façade vendoring 进去。

---

*协议/线程/生命周期完整设计*：[scripted-plugin-sidecar-design.md](scripted-plugin-sidecar-design.md) + [`scripting/wire/v1.json`](../scripting/wire/v1.json)。Python/Lua 版见同目录 `PythonScriptPluginDevelopment.md` / `LuaScriptPluginDevelopment.md`。
