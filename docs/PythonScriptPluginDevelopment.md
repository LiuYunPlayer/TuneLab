# TuneLab Python 脚本插件开发指南

> 面向**插件开发者**：如何用 **Python** 为 TuneLab 编写 `format`（工程文件导入/导出）、`effect`（音频效果）、`voice`（人声/乐器合成引擎）三类脚本插件。
>
> - 本文与 C# 插件开发指南（[plugin-development.zh-CN.md](plugin-development.zh-CN.md) / [plugin-development.md](plugin-development.md)）并列，但面向**进程外脚本插件**，讲的是**脚本这一侧**的写法。
> - 进程外执行单元在术语上叫 **sidecar**（见 [naming-glossary.md](naming-glossary.md)）。宿主侧协议/线程/内存布局的设计见 [scripted-plugin-sidecar-design.md](scripted-plugin-sidecar-design.md)；语言无关的**线协议规范**见 [`scripting/wire/v1.json`](../scripting/wire/v1.json)。本文只讲“怎么写、怎么跑、怎么打包”，不重复协议细节。
> - 三个前提：你会写 Python；你知道 TuneLab 里 format/effect/voice 各是干什么的（不清楚先看 C# 指南 §1）；你对“插件是独立进程、通过标准输入输出通信”没有心理负担。

**关键事实**：TuneLab 里的 Python 插件**不在 TuneLab 进程内运行**。它由宿主 spawn 成一个独立的 `python` 进程（sidecar），两者用 **JSON-RPC 2.0 over stdio**（LSP 同款 `Content-Length` 帧）对话。你几乎不碰协议——`tunelab` façade 替你处理了——但它的所有约束会体现在你的代码里（见 §2 生命周期、§10 交互规范）。

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
10. [交互规范（宿主契约 / 你的义务）](#10-交互规范宿主契约--你的义务)
11. [代码规范](#11-代码规范)
12. [依赖 vendoring](#12-依赖-vendoring)
13. [本地调试与打包](#13-本地调试与打包)
14. [完整 Demo](#14-完整-demo)
15. [常见坑 / FAQ](#15-常见坑--faq)

---

## 1. 运行前提与获取解释器

manifest 里写 `"runtime": "python>=3.10,<4"`，宿主在**加载期**（不跑你的代码）按**四级探测**找一个满足区间的解释器：

1. **设置指定** —— 用户在设置里填 `ScriptedRuntimeSource`/解释器路径（最高优先级）；
2. **环境变量** —— `TUNELAB_PYTHON` 指向一个 `python(.exe)`（开发/CI 常用）；
3. **runtime 资源包** —— 扩展目录里一个 `type:"runtime"`、`flavor` 为 `python`、平台含当前 RID、版本落在区间的包；
4. **PATH** —— 直接在 PATH 上找 `python`/`python3`。

**探测落空会怎样**：侧栏该插件显示 `Skipped · 未找到 Python 运行时`，并给一个**【下载运行时】**按钮（`sdk-version` 需 ≥1.1）。点了以后宿主按内置**镜像表**（国内镜像在前、官方源兜底）直发上游发行件，本地组装成一个 `type:"runtime"` 包登记；成功即自动重新加载该条目。**全自动档**（设置项“自动获取脚本运行时”，默认关）则是探测一落空就在后台静默拉。你也可以自带一个编译好的 Python（走第 1/2/3 级）。

> 结论：开发期**不必**关心分发——本机有个 Python 3.10+（或用 `TUNELAB_PYTHON` 指定）就能跑。分发期才需要想 §15 的 vendoring/镜像问题。

---

## 2. 包结构与 manifest.json

一个包 = 一个**文件夹**（不是 zip，安装/卸载的原子单位）。最小结构：

```
MyPyPlugin/
├── manifest.json        # 必需：包的唯一身份与声明清单
└── plugin/
    ├── main.py          # entry 指向它：唯一入口脚本
    └── vendor/          # 可选：把 tunelab façade 与第三方依赖塞这里（§12）
```

### 2.1 manifest 字段

| 字段 | 必需 | 说明 |
|---|---|---|
| `id` | ✓ | 全局唯一，反向域名风格 `com.example.mypyplugin`。跨版本保持稳定的身份键。 |
| `name` | ✓ | 展示名。 |
| `version` | | semver，默认 `1.0.0`。 |
| `author` / `description` | | 展示用；`description` 一句话。 |
| `sdk-version` | ✓（要能力探测时） | 你实际用到的**最新**那一档，脚本插件写 `"1.1"`（脚本面自 2.1.0 起）。填高填低都会被校验拦下。 |
| `type` | ✓ | 本包条目类型：`format` / `effect` / `voice`。 |
| `runtime` | ✓（脚本） | 解释器区间串，如 `"python>=3.10,<4"`。flavor 前缀参与判定。 |
| `entry` | ✓（脚本） | **包内相对路径**的入口脚本（`plugin/main.py`），与 `runtime` 成对出现；不能越出包目录（`..` 逃逸会 Failed）。 |
| `suffixes` | format | 该格式能读**又**能写的扩展名，如 `["pyt"]`。 |
| `import-suffixes` / `export-suffixes` | format | 只读不写 / 只写不读时用，分别声明，比 `suffixes` 精细。 |
| `engine` | effect/voice ✓ | 引擎 id（字符串，唯一兑现键），如 `"PyGain"`。 |
| `platforms` | | 平台过滤 `["win","osx","linux"]`；省略 = 全平台。 |
| `settings` | | 布尔：本条目声明了设置面板（走 sidecar 的 `settings.get_config`/`settings.apply`）。见 §8。 |
| `deps` | | 依赖模式。**留空或 `"vendored"`**（默认，作者自带）；`"pip"` 已声明但当前宿主**未实现**，会被判 `Skipped`（非错误）。所以现在只有 vendoring 一条路（§12）。 |

> 一个包可含多个条目用数组形式 `entries: [ {...}, {...} ]`（每个 entry 自带 `type`/`engine`/`entry`/`suffixes` 等），但**共享一个入口脚本**时通常拆成多包更清晰；同一实现（同一 `entry` 路径）不能既当这个格式又当那个格式，一个实现 = 一个条目。

### 2.2 单条目 manifest 示例

```json
{
  "id": "com.example.pytxt",
  "name": "PyTxt Format",
  "version": "1.0.0",
  "author": "You",
  "description": "一行一音符的 .pyt 文本工程格式。",
  "sdk-version": "1.1",
  "type": "format",
  "suffixes": ["pyt"],
  "runtime": "python>=3.10,<4",
  "entry": "plugin/main.py",
  "platforms": ["win", "osx", "linux"]
}
```

---

## 3. 执行模型与生命周期

理解这些能避免一大半“本地好好的、进 TuneLab 就崩”的问题。

- **懒 spawn**：条目注册时不启进程；**第一次真正用到**（导入文件、加载 effect、合成 voice）才拉起 sidecar 并握手。
- **握手（`initialize`）**：宿主把协议版本、包 id、每个条目的期望（type/entry/suffixes/已持久化的 settings 值）发给 sidecar；`tunelab` 回 `provides`（每个 manifest 承诺的条目必须自报兑现，否则握手期就判死）+ `capabilities`（如 `audio_plane`）+ runtime 自报版本。
- **epoch（世代）**：每次 spawn 自增。崩溃重拉后，旧世代的在途应答一律被宿主丢弃（防“尾包”串台）。你的代码不用管，但要知道：**一个进程实例只服务一个世代**。
- **崩溃与恢复**：sidecar 挂了 → 当次调用报 `runtime_error` → 宿主按 backoff 在**下一次调用时**重 spawn（不是定时复活）。10 分钟内死 5 次 → **熔断**（侧栏可见 + stderr 尾部摘要）。effect 链会回退到上一版产物、voice 重建会话重合成的恢复路径由宿主负责（叫“会话重建”，别和侧栏那个手动“重新加载”按钮混）。
- **心跳**：宿主周期性 `ping`；你的用户处理器若卡死（比如 synthesize 死循环），心跳超时会被判僵、触发取消/重拉。所以**别在回调里做无上限的阻塞**。
- **取消**：`effect.cancel`/`voice.cancel` 尽力通知你的 session 的 `cancel()`；但一个彻底卡死的 handler 仍要靠心跳兜底——所以长任务应**周期性检查 `ctx.cancelled`**。
- **关停**：应用退出/包被卸载 → 优雅 `shutdown`，你收到时 `sys.stdin` 到 EOF，façade 会直接退出（防御线一：父进程死 = stdin EOF）。

> 你**永远不要往 stdout 打印东西**。stdout 是协议专用信道，`print()` 会污染它、当场把会话搞崩。日志一律走 `tunelab.info/warn/error`（§9）。

---

## 4. 第一个插件：format

`format` 负责把外部工程文件**读进** TuneLab（import → 返回一个 ProjectInfo dict）或**写回**磁盘（export → 把 ProjectInfo 落成文件）。

façade 用装饰器注册；`import_project(self, stream_ref)` / `export_project(self, stream_ref, project)` 里 **`stream_ref` 是一个文件路径**（宿主在临时目录 `…/TuneLab/Temp/io-<nonce>/` 里铺好的临时文件，用完自动删）：

- **import**：`stream_ref` 是**待读**的输入文件（宿主已把你声明能吃的后缀对应的字节写进去）→ 你 `open(stream_ref)` 读，返回 project dict。
- **export**：`stream_ref` 是**待写**的输出文件路径 → 你 `open(stream_ref,"w")` 写，宿主随后把它拷去真正的导出目标。

```python
import tunelab

@tunelab.format
class PyTxtFormat:
    def import_project(self, stream_ref):
        notes = []
        with open(stream_ref, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                p, pos, dur, *lyric = line.split()
                notes.append({
                    "pitch": int(p), "pos": float(pos), "dur": float(dur),
                    "lyric": lyric[0] if lyric else "la",
                    "pronunciation": "",
                    "properties": {"source": "pytxt"},        # 自由值，自动编解码（§5）
                    "leadingPhonemes": [], "bodyPhonemes": [], "bodyOffset": 0.0,
                })
        return {
            "tempos": [{"pos": 0.0, "bpm": 120.0}],
            "timeSignatures": [{"barIndex": 0, "numerator": 4, "denominator": 4}],
            "tracks": [{
                "name": "Py Track", "gain": 0.0, "pan": 0.0, "mute": False, "solo": False,
                "asRefer": True, "color": "",
                "parts": [{
                    "kind": "midi", "name": "P", "pos": 0.0, "startOffset": 0.0, "endOffset": 0.0,
                    "gain": 0.0, "soundSource": {"kind": "voice", "type": "", "id": ""},
                    "effects": [], "notes": notes, "automations": {}, "piecewiseAutomations": {},
                    "pitch": {"segments": []}, "vibratos": [], "properties": {},
                }],
            }],
        }

    def export_project(self, stream_ref, project):
        lines = []
        for track in project["tracks"]:
            for part in track["parts"]:
                for n in part.get("notes", []):
                    lines.append("%d %g %g %s" % (n["pitch"], n["pos"], n["dur"], n.get("lyric", "la")))
        with open(stream_ref, "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + ("\n" if lines else ""))

tunelab.serve()
```

- **只读格式**：省略 `export_project`（manifest 也别声明对应的导出后缀）。只写不读同理。
- **多后缀/多格式**：一个包里用 `@tunelab.format(entry="a")` 注册多个 handler，manifest 的 `entries[]` 各自 `entry` + `suffixes`；不写 `entry` 时单 handler 自动绑到声明的条目。
- 别忘了结尾 `tunelab.serve()`——不 serve 进程就退出。

---

## 5. 数据形状规范：ProjectInfo

`import_project` 返回、`export_project` 收到的，是宿主 `ProjectInfo` 的 **JSON 镜像**，键名 **camelCase**（`soundSource`、`leadingPhonemes`、`bodyOffset`、`timeSignatures`、`piecewiseAutomations`…）。层级：`tracks → parts → notes`。

要点：

- **note 的 `pos`/`dur` 是 tick（浮点），`pitch` 是整数半音**。PartInfo 用判别器 `"kind": "midi" | "audio"` 区分。
- **`properties`（note/part/effect/工程各处都有）是自由属性 map**：随便放 bool/number/string/nested object/array，façade 自动打 tag、跨语言还原成原生 Python 值（`{"source":"pytxt"}` 回来就是 `{"source":"pytxt"}`）。
- **特殊浮点**：`NaN / +Infinity / -Infinity` 在线上一律是**字符串** `"NaN"|"Infinity"|"-Infinity"`（合法的裸 JSON 数字 token net8 解析器不吃）；façade 读写两端都自动转换，你只管用 `float("nan")`/`math.inf`。
- **`Multiple` 三态永不跨线**（它是 UI 概念）。
- 不认识的键会被宿主忽略，别指望往 ProjectInfo 塞自定义顶层字段能存活——自定义数据放 `properties` 里。

音频自动化“曲线”在 import/export 面是数据（`pitch.segments`、`piecewiseAutomations`）；在 **effect/voice 合成期**则走**曲线面**的远程求值器（§6/§7），不在此列。

---

## 6. effect 插件

`effect` 处理一段音频：宿主把输入段的**几何 + 参数**给你，你产出**输出段**（通常等长）+ 可选回显曲线。用 `@tunelab.effect("引擎id")` 注册一个**引擎类**，宿主对每个“实例化到工程里的效果”调用 `create_session(ctx)` 得到一个**会话对象**，之后对该会话调 `process(ctx)`。

```python
import tunelab

@tunelab.effect("PyGain")
class PyGain:
    def init(self):                      # 引擎级一次性初始化（可选）
        self.gain = 0.5

    def destroy(self):                   # 引擎销毁（可选）
        pass

    # —— 声明面：告诉宿主有哪些可调参数（返回 config 树，见 §8 的 config 类型）——
    def get_property_config(self, effects):
        return {"type": "object", "properties": [{
            "id": "amount",
            "config": {"type": "slider", "defaultValue": 0.5,
                       "scale": {"kind": "linear", "min": 0.0, "max": 1.0}},
        }]}

    def get_automation_configs(self, effects):        # 可被自动化的参数轨（无则 []）
        return []

    def get_synthesized_parameter_configs(self, effects):   # 本引擎会产出的回显轨（无则 []）
        return []

    def create_session(self, ctx):
        return _Session(self)

class _Session:
    def __init__(self, engine):
        self.engine = engine

    def process(self, ctx):
        g = ctx.input                                   # 输入段几何：sample_offset / sample_count / sample_rate
        data = ctx.input_read(0, g.sample_count)        # 读输入（list[float]）
        out = ctx.output(g.sample_offset, g.sample_count, g.sample_rate)
        out.write(0, [x * self.engine.gain for x in data])
        out.commit()                                    # ← commit 是产物进下游 effect 链的唯一闸门
        t0 = g.sample_offset / g.sample_rate
        t1 = (g.sample_offset + g.sample_count) / g.sample_rate
        ctx.status([(t0, t1, "synthesized", 1.0)])     # 状态：pending|synthesizing|synthesized|failed
        ctx.parameters({"loudness": {"segments": [[[t0, 0.4], [t1, 0.4]]]}})  # 回显曲线

    def cancel(self):                                   # 取消通知（可选，长任务里要主动查 ctx.cancelled）
        pass

    def dispose(self):                                  # 会话回收（可选）
        pass

tunelab.serve()
```

会话 `ctx` 的方法：

| 方法 | 作用 |
|---|---|
| `ctx.input` | 输入段几何 `.sample_offset/.sample_count/.sample_rate` |
| `ctx.input_read(offset, count=None)` | 读输入一段，返回 `list[float]`（绝对轴寻址的段内偏移；越界返回 false/空） |
| `ctx.properties(keys)` | 拉 part 级当前参数值（`{id: native}`） |
| `ctx.automation_tracks()` | 可用自动化轨 id 列表 |
| `ctx.evaluate(track, positions)` | 对某条自动化轨求值（`positions` **必须非降序**，返回同长 `list[float]`）——曲线面，宿主算插值 |
| `ctx.output(offset, count, rate)` | 新建输出段 → `.write(off, samples)` / `.commit()` /（可选）`.resize()` / `.dispose()` |
| `ctx.status([...])` | 发布状态段 |
| `ctx.parameters({...})` | 发布回显曲线（`{id: {"segments": [[[t,v],…],…]}}`） |
| `ctx.cancelled` | 布尔，长任务轮询它 |

**性能与快车道**：`ctx.output(...).write(...)` 在 Python façade 下会走 **shm 音频面**（握手时 façade 自报 `capabilities.audio_plane="shm"`，宿主回一个 `region` 让你用 `mmap`/`memoryview`/numpy 直写 PCM，`commit` 时才拷进宿主缓冲）。你**不用写 shm 代码**——façade 已经封装，`write/commit` 语义不变；只是大批量音频不会走 base64、内存带宽级完成。若 numpy 在，`out.shm_view()` 可拿 float32 视图直写。

---

## 7. voice 插件

`voice` 从乐谱（音符 + 歌词 + 参数）**合成出人声/乐器**：宿主按块问你要“接下来哪段没合成”（`peek`）、给定区间要产物（`synthesize`），并拉音符/参数/曲线。用 `@tunelab.voice("引擎id")` 注册引擎类。

引擎钩子（声明面 + 会话工厂）：

```python
import math, tunelab

@tunelab.voice("PySine")
class PySine:
    RATE = 44100

    def init(self): pass
    def destroy(self): pass

    def catalog(self):
        # 返回 (声源列表, 布局列表)；声源 = {"id","name","description"}
        return [{"id": "sine", "name": "Sine Bank", "description": "440Hz 测试声库"}], []

    def automation_configs(self, parts):                # part 级声明（可空）
        return []
    def synthesized_parameter_configs(self, parts):     # 本声源产出的回显轨声明
        return []
    def part_property_config(self, parts):             # part 级可调参数（config 树）
        return {"type": "object", "properties": []}
    def note_property_config(self, part, notes):       # note 级可调参数
        return {"type": "object", "properties": []}
    def phoneme_property_configs(self, part, notes):   # 音素级参数槽（无则 []）
        return []

    def create_session(self, ctx):
        return PySineSession(ctx)

class PySineSession:
    default_lyric = "la"                               # 会话级：缺省歌词（属性即可）

    def __init__(self, ctx):
        self.ctx = ctx
        self.pos = 0.0

    def is_continuation(self, note):                   # 该音符是否延续上一段（一般 False）
        return False

    def peek(self, start, end):
        """告诉宿主 [start,end) 里下一个待合成区间；没有就返回 None。电平语义可被重问。"""
        if self.pos >= end:
            return None
        return (max(start, self.pos), min(self.pos + 0.5, end))

    def synthesize(self, start, end):
        if start < self.pos:                           # 已合成的块：直接返回（幂等，电平语义可重问）
            return
        notes = [n for n in self.ctx.notes() if n.end > start and n.start < end]
        snap = self.ctx.snapshot(notes)               # 拉真值：音符 + part 属性 + 曲线求值器
        rate = PySine.RATE
        count = int((end - start) * rate)
        times = [start + (end - start) * i / 63 for i in range(64)]
        curve = snap.pitch.evaluate(times)            # 曲线面：远程句柄求值（非降序 positions）
        seg = self.ctx.output(int(start * rate), count, rate)
        seg.write(0, [0.2 * math.sin(2 * math.pi * 440.0 * i / rate) for i in range(count)])
        seg.commit()
        self.ctx.status([(start, end, "synthesized", 1.0)])
        self.ctx.pitch([[(start, 60.0), (end, 62.0)]])                      # 合成音高折线
        self.ctx.phonemes({n.id: ([], [{"symbol": n.lyric or "a", "duration": n.end - n.start,
                                        "stretch_weight": 1.0}], 0.0) for n in snap.notes})  # 音素按 note_id 归属回指
        self.pos = end

    def on_dirty(self, kind, payload):                # 宿主推送“标脏”粗信号（可选）：缓存作废、下轮重算
        pass
    def cancel(self): pass
    def dispose(self): pass

tunelab.serve()
```

会话 `ctx`（voice）方法：

| 方法 | 作用 |
|---|---|
| `ctx.notes()` | 拉本 part 全部音符 `NoteView`（`.id/.start/.end/.pitch/.lyric/.body_offset/.leading/.body/.properties`） |
| `ctx.snapshot(notes_or_ids)` | 拉快照：`.notes`（请求序对齐）、`.part_properties`，以及求值器 `.pitch` / `.pitch_deviation` / `.automations[id]`（各带 `.evaluate(positions)`） |
| `ctx.output(offset,count,rate)` | 输出音频段（同上，走 shm 快车道） |
| `ctx.status / .pitch / .phonemes / .parameters` | 四路产物发布：状态 / 音高折线 / 音素 / 回显轨 |
| `ctx.cancelled` | 取消标志 |

- **`phonemes` 的 key = note id**，且是 `snapshot` 回来的 `request_id`（归属回指）；每个音素 `{"symbol","duration","stretch_weight","properties"}`。
- **`peek` 的“可重问”**：宿主可能多次问同一段，你按内部游标 `self.pos` 决定还欠它什么，已交付的直接 `return`——幂等是硬要求。
- `synthesize(start, end)` **同步返回即视为该块完成**；不要在这阻塞过久（心跳 + 取消会兜底，但你会被重拉）。

---

## 8. settings 设置面板

manifest `settings: true` 声明本条目有设置面板。宿主经 `settings.get_config`（要一棵 config 树）和 `settings.apply`（推来一组终值）与 sidecar 往返；持久化桶、密码字段的解密都在**宿主侧**（DPAPI/Keychain），你只见到终值明文。你的条目上实现（Python 用方法名，façade 反射发现）：

```python
@tunelab.format
class MyFmt:
    def get_settings_config(self):
        return {"type": "object", "properties": [
            {"id": "author", "displayText": "作者", "config": {"type": "textbox", "defaultValue": ""}},
            {"id": "quality", "config": {"type": "combo", "items": [
                {"value": {"s": "low"}, "displayText": "低"},
                {"value": {"s": "high"}, "displayText": "高"}], "default": {"s": "high"}}},
            {"id": "token", "config": {"type": "textbox", "defaultValue": "", "password": True}},
            {"id": "volume", "config": {"type": "slider", "defaultValue": 0.8,
                                        "scale": {"kind": "linear", "min": 0.0, "max": 1.0}}},
        ]}

    def apply_settings(self, values):          # values 已是原生 dict（解码好）
        self.author = values.get("author", "")
        # spawn 前用户就改过设置 → 握手时随 entries[].settings 补发，仍进这里，一次不落
```

可用的 config 节点类型（`type` 字段）：`slider` / `checkbox` / `textbox`（`password`/`multiline`/`maxVisibleLines`）/ `combo`（可嵌套 `subItems`）/ `number` / `path`（`file`/`folder` + `fileTypes`）/ `automation` / `object`（嵌套）。**lambda/自定义 scale 不能跨线**——宿主对未知 kind 抛错。effect/voice **引擎类**上实现 `get_settings_config`/`apply_settings` 同理。

---

## 9. 日志与诊断

```python
tunelab.info("加载完成")           # level=info
tunelab.warn("字段缺失，用默认")    # level=warning
tunelab.error("写盘失败: %s" % e)  # level=error
```

- 这些通过 `log.write` 通知发回宿主，进 TuneLab 日志系统；**别用 `print`**（stdout 是协议信道）。
- **stderr**：解释器崩溃栈、第三方库往 stderr 喷的东西，会被宿主捕获、滚进**包目录** `logs/stderr-<boot>.txt`（单行截 8KB、本次 boot 文件硬顶 2MB、旧 boot 只留 2 份）。排查“sidecar 起来又死了”第一时间看这个文件。
- 报错误信息、日志正文用**英文**（仓库约定：日志/异常/断言消息英文；代码注释可中文）。

---

## 10. 交互规范（宿主契约 / 你的义务）

**宿主给你的保证**

- 你在自己的进程里，崩了不连累宿主，也不连累别的插件；宿主会重拉并走恢复（effect 回退上版、voice 会话重建）。
- 数据真值（音符/参数/曲线/音频段）通过反向调用给，插值/重采样等**唯一实现在宿主**——你 `evaluate` 拿到的就是宿主口径，不必自己实现。
- 你声明的 `capabilities.audio_plane="shm"` 时，大音频走共享内存（你无感）；否则 inline base64。

**你要履行的义务**

1. **stdout 只写协议**：禁 `print`；`tunelab` 之外任何东西别碰 stdout。
2. **别长阻塞**：`process`/`synthesize` 会占用该会话；心跳和取消是最后防线不是常规。长循环查 `ctx.cancelled`。
3. **产物必须 `commit` 才算数**：commit 前的写只供进度/波形预览，不进下游链。
4. **幂等**：`peek`/`synthesize` 可能被电平语义重问，重复调用要能安全重入。
5. **positions 非降序**：`evaluate` 的 `positions` 必须升序，宿主不校验（乱序=未定义，与 in-proc 同语义）。
6. **特殊浮点交给 façade**：不要自己往 JSON 塞裸 `NaN`。
7. **不越界**：`write(offset, samples)` 超出段 `sample_count` 非法。
8. **一个进程一世代**：别缓存跨 epoch 的宿主对象句柄；snapshot 换代后旧 `note_ids`/`track` 句柄会 `stale_handle`。

---

## 11. 代码规范

- **入口脚本 = 唯一 `entry` 指向的文件**，只做：`import tunelab` → 注册（装饰器）→ `tunelab.serve()`。重逻辑拆成同级模块 import 进来（相对 import 走 `plugin/` 目录）。
- **façade 导入**：生产包把 `tunelab` vendoring 进包里（§12）再 `import tunelab`；开发期可用 `PYTHONPATH` 指向 `scripting/python`。建议入口首行显式扩搜索路径，兼容 vendoring 与裸装：
  ```python
  import os, sys
  sys.path.insert(0, os.path.join(os.path.dirname(__file__), "vendor"))  # vendored 优先
  import tunelab
  ```
- **命名**：Python 侧一切 snake_case（`import_project`、`get_property_config`、`on_dirty`），与宿主 wire 的 camelCase 由 façade 转换，你不用操心。
- **类型**：给 config 树、ProjectInfo 这种嵌套结构建议用 `TypedDict`/常量集中管理，避免手抖写错键名（写错键会被静默忽略——最难查的那类 bug）。
- **错误处理**：让用户可复现的错误经 façade 变成 RPC error（宿主侧显示）；不可恢复的让进程死掉、由宿主按 backoff 处理，别自己吞异常返回半成品产物。
- **确定性**：合成产物不应依赖当前时间/随机源/环境差异（复现和缓存一致性要求）；要随机就吃 `note`/`pos` 派生 seed。
- **别 spawn 子进程/别开端口**：sidecar 的世界是 stdio；额外 IPC/网络在 §9 纪律里不鼓励（防火墙、半关语义都是净亏）。

---

## 12. 依赖 vendoring

当前宿主只支持 **vendored（作者自带）** 依赖，`"pip"` 模式未实现。做法（Python）：

```bash
# 把 façade 和第三方库都塞进包的 plugin/vendor/
pip install --target=plugin/vendor tunelab numpy
```

- `tunelab` façade 目前是**本地源码包**（未上 PyPI）：开发时从 `scripting/python/tunelab/` 拷进 `plugin/vendor/tunelab/`，或 `PYTHONPATH` 指过去。
- 入口确保 `vendor` 在 `sys.path`（§11 首行片段）。
- 纯 stdlib 的最省事（如本文所有 Demo：`math`/`struct` 足矣）；能不加二进制依赖就不加（跨平台负担大）。

---

## 13. 本地调试与打包

**跑起来最快**：开发期直接把包文件夹放进 TuneLab 的**扩展目录**（Windows：`%APPDATA%\TuneLab\Extensions\<你的包名>\`），启动/重新加载即可。

- TuneLab 里“重新加载”（或重启）后，去 `TuneLab\Extensions\<包>\logs\stderr-*.txt` 看 sidecar 有没有起来、崩在哪。
- 指定解释器便于对照版本：设 `TUNELAB_PYTHON=<path>`（或让用户在设置里填）。
- **不依赖宿主的最快回路**——façade 有完整单测，你也可以脱离宿主自测纯函数（把 import/export/process 逻辑拆成能单测的纯函数）。

**本仓库的脚本测试面**（贡献/自查时用）：

```bash
python -m unittest discover -s scripting/python/tests        # façade 单测（纯 stdlib，无需宿主）
dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj \
  --filter "runtime=needs-interpreter"                        # 真解释器 E2E
#   TUNELAB_REQUIRE_PYTHON=1 → 找不到 Python>=3.10 就判红；TUNELAB_PYTHON 钉版本
```

**打包成可分发 `.tlx`**（本仓库工具链，三步）：把包放 `tests/plugins/`，`pwsh tests/pack-tlx.ps1` → `pwsh tests/install-tlx.ps1 <名>`（脚本包无 csproj，`pack-tlx` 会直接收编目录）。生产分发的解释器/依赖问题见 §1 与 §12。

---

## 14. 完整 Demo

下面是一个**能直接跑的最小 `.pyt` 格式包**全貌（只读+写一行一音符的文本工程）。

```
PyTxtDemo/
├── manifest.json
└── plugin/main.py
```

`manifest.json`：

```json
{
  "id": "com.example.pytxtdemo",
  "name": "PyTxt Demo",
  "version": "1.0.0",
  "author": "You",
  "description": "一行一音符的 .pyt 文本工程格式（导入/导出）。",
  "sdk-version": "1.1",
  "type": "format",
  "suffixes": ["pyt"],
  "runtime": "python>=3.10,<4",
  "entry": "plugin/main.py",
  "platforms": ["win", "osx", "linux"]
}
```

`plugin/main.py`：即 §4 的完整代码（`import tunelab` → `@tunelab.format` 类 → `tunelab.serve()`）。把它放进扩展目录、造一个 `song.pyt`：

```
60 0 480 la
62 480 240 du
65 720 720 mi
```

在 TuneLab 里“导入”该文件 → 出现一个含三音符的 MIDI part；再导出回去应字节级等价。加一个 effect：另建一个包按 §6 的 `PyGain`（把 `@tunelab.effect("PyGain")` 那段照抄）即可给某条 part 挂“×0.5 增益”。voice 同 §7。

**三家族各自的“一句话 Demo”**：

| 家族 | 做什么 | 关键 API |
|---|---|---|
| format | 读写工程文件 | `import_project`/`export_project` → ProjectInfo dict |
| effect | 音频进、音频出 | `create_session` → `process(ctx)`，`ctx.input_read`/`ctx.output().write/commit` |
| voice | 乐谱 → 音频 + 音素 + 音高 | `catalog`/`peek`/`synthesize`，`ctx.notes/snapshot/output/pitch/phonemes` |

---

## 15. 常见坑 / FAQ

- **一 import 就卡住/无响应**：忘了 `tunelab.serve()`，或在 `import` 阶段（serve 之前）做了阻塞/联网。serve 之前的顶层代码要快、无副作用。
- **握手判死 `does not declare entry`**：manifest 声明了条目，façade 没注册（`@tunelab.effect("X")` 的 id 要和 manifest `engine` 一致；format 的 `entry` 要和文件路径一致）。
- **产物不出现 / effect 链不更新**：`write` 了但没 `commit`。commit 是闸门。
- **`MissingMethodException` 之类**（脚本面少见，多为 C# 样例）：一般是加载了旧二进制；脚本包改了源码要重新走 §13 打包/安装三步并**先关掉 TuneLab**（否则 dll/文件被占用）。
- **音频巨大却慢**：确认宿主 ≥2.1 且握手 `audio_plane=shm`（Python façade 默认声明）。老宿主/异常平台回落 inline base64，功能等价、性能自降。
- **`evaluate` 结果怪**：`positions` 没升序，或用了换代后的旧 `snapshot` 句柄（会 `stale_handle`）。
- **settings 值对不上**：combo 的 `value` 要用 tagged 形（`{"s":"high"}`/`{"n":1}`），别塞裸字符串到需要 typed 的位置；密码字段你只会在 apply 里见到宿主解密后的终值。
- **多格式互相覆盖**：同包两条目用了重叠后缀 = 加载期判冲突；一个实现对应一个条目，按方向用 `import-suffixes`/`export-suffixes` 拆。
- **Python 版本**：façade 纯 stdlib，3.10+ 皆可；`mmap`/`memoryview.cast` 依赖较新运行时，别在 3.8 上跑 shm（区间写 `>=3.10` 已挡）。

---

*线协议、线程/内存模型、三面传输、握手/心跳/熔断的完整设计*：见 [scripted-plugin-sidecar-design.md](scripted-plugin-sidecar-design.md) 与规范 [`scripting/wire/v1.json`](../scripting/wire/v1.json)。Node.js / Lua 的写法分别见 [NodeJSScriptPluginDevelopment.md](NodeJSScriptPluginDevelopment.md) / [LuaScriptPluginDevelopment.md](LuaScriptPluginDevelopment.md)——协议同一套，差别只在语言 façade 的方法命名与依赖打包方式。
