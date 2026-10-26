# TuneLab Plugin Development Guide

> For TuneLab's new extension system (V1). This document explains how to write, package, and publish a TuneLab plugin.
> Compatibility notes for old (Legacy) plugins are in the final "Appendix: Legacy Plugins".
>
> This English document is the source of truth. A Chinese translation is at [plugin-development.zh-CN.md](plugin-development.zh-CN.md) (if they diverge, this English version wins).

---

## 1. Core Concepts

- **A plugin package = a folder**. It is the atomic unit of deployment, installation, and uninstallation, placed into TuneLab's extension directory (see §11).
- **`manifest.json` is the package's identity manifest**, which must sit at the **top level** of the package folder. New (V1) plugins **must** include it; TuneLab reads this file first and then **selectively** loads the assemblies inside the package based on its contents — it does not blindly scan the whole folder.
- **One package can contain multiple plugins**. If you have a shared infrastructure assembly, you can pack multiple plugins built on top of it into the same package — the infrastructure is distributed once and loaded once at runtime (they share a single load context).
- **Plugin category (type)**: currently supported are `format` (project file import/export), `voice` (singing synthesis engine), `instrument` (polyphonic sound-source engine such as a synth/sampler), and `effect` (an effect that applies whole-segment offline transforms to synthesized audio, such as voice conversion). A package can also be a pure **resource package** (no code, e.g. voicebank resources).

Each plugin package is loaded into its own **isolated AssemblyLoadContext (ALC)**:

- TuneLab's SDK contract assemblies (`TuneLab.Foundation` + `TuneLab.SDK.*`) and the .NET runtime are **provided by the host and shared across all plugins** — reference them, but **do not** bundle them into your package.
- Your **private dependencies** (third-party libraries, native dlls, etc.) go into the package folder and are loaded into your package's dedicated ALC, **isolated from other plugins**, so different plugins bundling different versions of the same library **will not conflict**.

---

## 2. manifest.json

### 2.1 Fields

Package level (top level):

| Field | Required | Description |
|---|---|---|
| `id` | ✅ | The package's unique identifier; a reverse-domain form is recommended, e.g. `com.example.myplugin`. **It is also the V1 marker** — the presence of `id` means it is loaded as a new-format plugin. |
| `name` | ✅ | Display name |
| `version` | | Package version (semver), defaults to `1.0.0` |
| `author` | | Author (shown in the extensions sidebar) |
| `description` | | One line about **the package as a whole** (shown in the extensions sidebar tooltip and the detail window header). What an individual capability is belongs in that entry's own `introduction` — this line never stands in for it. |
| `icon` | | An icon at a path relative to the package; bitmap (`.png`/`.jpg` etc.) or vector (`.svg`) both work. **Shown as-is** — the sidebar does not add a background or clip rounded corners, so rounding/transparency/padding are all up to you (draw rounded corners into the icon if you want them). A **square** icon (e.g. 64×64 or larger) is recommended. If omitted, the sidebar uses the name's first letter over a dark rounded square as a placeholder. |
| `sdk-version` | ✅ for plugins with code | The SDK version you compiled against (e.g. `"1.0"`). TuneLab uses it for a compatibility check: a plugin requiring a higher version than the host provides is skipped. Resource packages may omit it. **Declare the level you actually use, not the newest one** — asking for more than you need shuts you out of older hosts for nothing. The levels are below. |

**What each SDK level adds** (the host's level rises with TuneLab; the one your plugin declares does not):

| `sdk-version` | Since | What it adds |
|---|---|---|
| `1.0` | TuneLab 2.0.0 | Everything the V1 SDK shipped with |
| `1.1` | TuneLab 2.1.0 | Path picker controls (`PathPickerConfig` / `FileTypeFilter`), multiline text boxes (`TextBoxConfig.WithMultiline`) |
| `1.2` | Not released yet | Tag groups (`TagConfig` / `TagItem`), buttons (`ButtonConfig` / `IButtonClickContext` / `IDialogService` / `IProgressReporter`, see §9), declaration invalidation (`InvalidatePart` / `InvalidateNotes` with the matching `PartId` / note `Id`, see §5.2), groups and separators in `+` menus and drop-downs (`AddableElement` / `AddableKey` groups, `Separator`, `ComboBoxItem.Separator`); plus the **experimental** canvas (`CanvasConfig` / `ICanvasControl` / `ICanvasHost` / `ICanvasDrawing` / `CanvasImage` and their event types, see §10) |

Plugin level (describing "what this package provides"). **Identity is inlined into the manifest**: one entry = one concrete registrable capability, carrying its own identity (engine id / file extension) + the full name of the implementing class. After reading the manifest the host knows what the plugin provides without loading assemblies and reflecting.

| Field | Required | Description |
|---|---|---|
| `type` | ✅ | Category: `format` / `voice` / `instrument` / `effect` / a resource type. A type this host does not know, that nevertheless declares `assembly`/`class`, is reported as an unsupported plugin type and skipped. |
| `engine` | ✅ for voice/instrument/effect | The engine type **id** (unique identity, e.g. `"MyEngine"`). **Immutable** — it is written into project files, so changing it makes old projects mismatch. Never localize it. |
| `suffixes` | for format | Shorthand for "both directions accept these **file suffixes**" (no dot, e.g. `["mid", "midi"]`), i.e. exactly the same as setting `import-suffixes` and `export-suffixes` to this value. |
| `import-suffixes` / `export-suffixes` | for format | The suffixes each direction accepts. **A direction you do not declare does not exist** — that is how an import-only or export-only format is written. Asymmetric aliases go here too: `"import-suffixes": ["mid","midi"], "export-suffixes": ["midi"]` reads two and writes one, still as **one** entry. Mutually exclusive with `suffixes`; at least one direction must be non-empty. |
| `name` | | The **display name** (for UI), which may differ from the identity id and may be translated. If omitted, the UI falls back to showing the identity id. |
| `introduction` | | A path relative to the package pointing at a markdown **introduction** (what it does, how to get started, what to watch out for). The host renders it in the extension detail window; the agent fetches it on demand via `get_extension_introduction`. **Describe what every field means and what values it takes** (structured values above all, such as a canvas field's value shape, see §10.1): this is the only place the agent learns what a field is for and what to write. |
| `localizations` | | Per-language overrides of `name` / `introduction`, e.g. `{ "zh-CN": { "name": "增益", "introduction": "Introduction.zh-CN.md" } }`. Missing entries fall back to the base value. Language variants of `introduction` go **here** (each language may point at a different filename); there is **no** implicit `<base>.<lang>.md` filename convention. |
| `class` | ✅ when it contains code | The full name of **the one entry class** for this entry (e.g. `"My.Ns.MyVoiceEngine"`). The host verifies it implements what this entry declares (see below) and has a **parameterless constructor**; anything else is a load error. |
| `assembly` | ✅ when it contains code | The assembly (a single path relative to the package folder) containing that class. Resource packages omit it. |
| `platforms` | | Platform filter, e.g. `["win", "osx", "linux"]` or with architecture `["win-x64"]`. Empty = all platforms. |

**What the entry class must implement:**

| Entry | The entry class must implement |
|---|---|
| `voice` | `IVoiceSynthesisEngine` |
| `instrument` | `IInstrumentSynthesisEngine` |
| `effect` | `IEffectSynthesisEngine` |
| `format` with import suffixes | `IImportFormat` |
| `format` with export suffixes | `IExportFormat` |
| `format` with both | both interfaces — one class doing both |

> **One entry = one implementation class = one introduction = one set of settings.** These are the same thing seen from three angles, so each is declared exactly once. Two implementations → two entries. The host never guesses: what the manifest says is what it is, and a mismatch is a load error rather than a silent downgrade. **The same class may not appear in two entries whose suffixes overlap** — that would be one implementation pretending to be two, collecting two of everything.
>
> **Direction is not part of `type`.** It is fully expressed by which suffix fields you fill, so `type` stays a single value for every format. Internally the host still distinguishes `format-import` / `format-export` capability slots — routing has always been per suffix *and* per direction, so a user can let another package take over just the export of one suffix — but those are **derived** from your suffix fields; you never write them.
>
> **Extra suffixes are aliases**, sharing the entry's implementation class, all of its text and its settings — yet registration and routing stay per suffix, so another package can take over just one of them. Each suffix is an immutable identity. Two genuinely different formats → two entries.
>

> **Identity id vs display name are separate**: `engine`/`suffixes` are immutable identities (registration key + project-serialization reference); `name`/`localizations` are for UI display only and may be renamed/translated freely.
>
> **`introduction` is entry-level, and it is the only description you need to write.** The one-line summary an AI needs is distilled by the AI itself from this text — you are not asked to write a separate line for it, since you cannot know what the model is looking for and would most likely end up writing marketing copy. For a multi-capability package, write one per entry.
>
> **What happens if you ship none**: the host will *not* pass the package-level `description` off as this capability's. The agent is told the capability has no introduction of its own, plus the package's self-description **explicitly labelled as a fallback** — and warned that the sentence describes the whole package (which may hold other capabilities) rather than this one. So an AI ends up knowing nothing about your plugin beyond its name, and will not recommend it on its own.
>
> **A README is not metadata**: the host only reads the `introduction` declared in the manifest. A `README.md` inside your package is the file you keep for people browsing your repository (build steps, license, contributing) — the host neither reads nor displays it. The two have different audiences, so they need not (and should not) be the same file.
>
> When one assembly has multiple engines/formats, list them one by one in `extensions[]` (same `assembly`, each with its own `engine`/`suffixes` + `class`).

### 2.2 Single plugin (most common)

Just write the plugin-level fields at the top level; no array needed:

```json
{
  "id": "com.example.myformat",
  "name": "My Format",
  "version": "1.0.0",
  "author": "Example",
  "description": "Import/export .myfmt files",
  "sdk-version": "1.0",
  "type": "format",
  "suffixes": ["myfmt"],
  "introduction": "Introduction.md",
  "class": "My.Ns.MyFormat",
  "assembly": "MyFormat.dll"
}
```

> In the shorthand form there is only one top-level `name`: it is **both** the package name and the display name of the single engine/format (`localizations` is shared the same way). If you want the package name and the engine's display name to differ, use the `extensions[]` form below and give the entry its own `name`.
>
> `introduction` sits at the top level here for the same reason — with a single entry, the package *is* that capability. In the `extensions[]` form it belongs to each entry; writing it at the top level then has no effect, because a package has no introduction of its own.

### 2.3 Multiple plugins in one package

Use the `extensions[]` array, where each element is one independent plugin's metadata. Package-level fields (id/name/version/author/description/sdk-version) go at the top level and are shared:

```json
{
  "id": "com.example.suite",
  "name": "Example Suite",
  "version": "2.0.0",
  "sdk-version": "1.0",
  "extensions": [
    { "type": "format", "import-suffixes": ["exfmt"], "name": "Example Format (Import)", "introduction": "Introduction.Import.md", "class": "Example.Format.Importer", "assembly": "Example.Format.dll" },
    { "type": "format", "export-suffixes": ["exfmt"], "name": "Example Format (Export)", "introduction": "Introduction.Export.md", "class": "Example.Format.Exporter", "assembly": "Example.Format.dll" },
    { "type": "voice",  "engine": "ExEngine", "introduction": "Introduction.Voice.md", "class": "Example.Voice.ExVoiceEngine", "assembly": "Example.Voice.dll", "platforms": ["win"] }
  ]
}
```

> `Example.Format.dll` and `Example.Voice.dll` can both reference the same `Example.Common.dll` (placed in the package), which only needs to be distributed once.
>
> Each entry carries its **own** `introduction`, so the detail window shows one tab per entry and the agent can describe each capability accurately. The top-level `description` stays about the package as a whole.
>
> Rule: when `extensions[]` is present it takes precedence, and the top-level identity fields (`type`/`engine`/`class`/…) are ignored.

### 2.4 Resource package (no code)

Omit the code fields (`assembly`/`class` etc.) and use only `type` to declare its purpose. TuneLab merely registers it and does not load code; the corresponding engine discovers the in-package resources at runtime:

```json
{
  "id": "com.example.mybank",
  "name": "My Voicebank",
  "version": "1.0.0",
  "type": "voicebank"
}
```

---

## 3. Project Configuration

Create a new .NET class library project referencing the SDK assemblies:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>   <!-- SDK ABI floor, pinned to net8 -->
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <!-- Reference the SDK assemblies you need; these are provided by TuneLab, don't ship them -->
    <Reference Include="TuneLab.Foundation" />
    <Reference Include="TuneLab.SDK" /> <!-- format/voice/effect: all plugin types share one assembly -->
  </ItemGroup>
</Project>
```

Rules:

- **Target framework pinned to `net8.0`** (the SDK's ABI floor). When the host later upgrades .NET, plugins compiled against this floor run without recompilation.
- **Reference only `TuneLab.Foundation` and `TuneLab.SDK`**. **Do not** reference `TuneLab.Hosting.Foundation` or the host app — they are not the plugin contract.
- Set the SDK assemblies to "do not copy to output" (`Private=false` / Copy Local = No), and **don't put them in the package** when packing — the host shares its own copy.
- Reference your **private third-party dependencies** as usual and ship them with the package.

---

## 4. Writing a Format Plugin

Implement `IImportFormat` (import) and/or `IExportFormat` (export). A **parameterless constructor** is required. The file suffixes and the implementing class go in `manifest.json` (`suffixes` + `class` + `assembly`); you **no longer declare them via attributes** in code.

### 4.1 Declaring what you read and what you write

You answer one question: which suffixes can this format **read**, and which can it **write**. Direction follows from that — there is nothing else to choose.

```json
// Reads and writes the same suffixes → `suffixes` is the shorthand for both.
{ "type": "format", "suffixes": ["myfmt"], "name": "My Format",
  "class": "My.Ns.MyFormat", "assembly": "MyFormat.dll" }
```

```json
// Reads two, writes one. Still ONE entry: one name, one introduction, ONE settings bucket.
{ "type": "format", "import-suffixes": ["mid", "midi"], "export-suffixes": ["midi"],
  "class": "My.Ns.MidiFormat", "assembly": "MyMidi.dll" }
```

```json
// Import only — the direction you don't declare doesn't exist.
{ "type": "format", "import-suffixes": ["myfmt"], "name": "My Format",
  "class": "My.Ns.MyFormatImporter", "assembly": "MyFormat.dll" }
```

The class must implement whatever the entry declares: import suffixes require `IImportFormat`, export suffixes require `IExportFormat`, both require both. A mismatch fails at load with `class 'X' does not implement IExportFormat` — the host will not quietly drop the direction you asked for.

### 4.2 Two classes means two entries

If your importer and exporter are genuinely **different classes**, they are two entries — an entry has exactly one `class`:

```json
"extensions": [
  { "type": "format", "import-suffixes": ["myfmt"], "name": "My Format (Import)",
    "class": "My.Ns.MyFormatImporter", "assembly": "MyFormat.dll" },
  { "type": "format", "export-suffixes": ["myfmt"], "name": "My Format (Export)",
    "class": "My.Ns.MyFormatExporter", "assembly": "MyFormat.dll" }
]
```

They then get their own name, introduction and **their own settings** — which is the point: two implementations with different options have nowhere to put them otherwise.

Do **not** split a single class this way to express asymmetric suffixes: writing the same `class` in two entries with overlapping suffixes is a load error. One implementation is one entry; use `import-suffixes` / `export-suffixes` on it, as in §4.1. (Splitting would cost two detail-window tabs, two settings rows and two buckets for one implementation.)

```csharp
using System.IO;
using TuneLab.SDK;

// The `format` form: one class, both interfaces.
public class MyFormat : IImportFormat, IExportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        // Parse the stream into TuneLab's project model
        var project = new ProjectInfo();
        // ... populate project ...
        return project;
    }

    public void Serialize(Stream output, ProjectInfo info)
    {
        // Write info into the host-provided stream. The host owns output's lifecycle
        // (create/position/close); do not Dispose/Close/Seek/reset Position — just write.
        // ... write info into output ...
    }
}
```

### 4.3 Suffix aliases

Several suffixes in one direction means "one format, several aliases" — they share the class, the text and the settings, yet each remains separately routable. Each is an immutable identity (routing + serialization); `name` is an optional display name (add `localizations` for translations).

Reading liberally while writing one canonical extension is the normal shape for a format with aliases — that is the second example in §4.1, and it stays one entry.

The project model (`ProjectInfo`/`TrackInfo`/`PartInfo`/`NoteInfo`…) is defined in `TuneLab.SDK`.

---

## 5. Writing a Voice Plugin

A voice is a **singing synthesis engine** (e.g. an SVS model). This chapter is organized as "build the mental model first → walk each interface's responsibilities and pitfalls → five error-prone topics (phoneme I/O, pitch sampling, snapshots, property conventions, native-dependency packaging)". After following this chapter you should be able to deliver a voice plugin that is **thread-safe, correct under incremental re-synthesis, and unambiguous in product attribution**.

### 5.0 Mental model (read this section first)

- **Session-hosted thick model**: You implement `IVoiceSynthesisEngine` (one per engine type, parameterless constructor required; the engine id goes in `manifest.json`'s `engine` and the implementing class in `class`, which the host verifies implements `IVoiceSynthesisEngine`). The host calls `CreateSession` once **per MidiPart** in the project to build an `IVoiceSynthesisSession`. **All synthesis state is hosted by the session itself** — chunking, scheduling state, audio buffers, synthesis progress, and dirty (invalidation) decisions are all on your side. The reason: the invalidation dependency graph (e.g. the tiered pipeline "phoneme duration → pitch → audio", where changing an automation only requires re-rendering audio and not recomputing phonemes) is understood only by the engine; the host cannot replicate it. The host does only three things: push the change stream of project data to you, drive scheduling, and read your products to display them.
- **Declaration vs execution layering**: The session has two kinds of outward responsibilities — *declaration* (which automation tracks / synthesized parameter tracks / property panels / default lyric this sound source exposes) and *execution* (synthesis). Declaration is entirely a **pure function of the current part/note parameter values**; the host recomputes it on parameter commit and diffs it to the UI (see §5.2).
- **All time quantities on the plugin side are global seconds**: note boundaries, curve query points, windowing intervals, status-segment ranges, audio-segment alignment — **all are seconds** (`double`). Ticks are the host's internal score representation and are **never exposed** to the plugin. Global time 0s = sample 0. Tempo changes (and part shifts) do not need explicit handling on your side, and there is **no incremental notification**: the host simply rebuilds the whole session (old session `Dispose`d, a new session with a new context), and the new session reads the new second values — implement `Dispose` correctly and it is naturally correct (§5.9).
- **Two views + thread discipline (the most important pitfall)**:
  - **Live view** (`IVoiceSynthesisContext` and its `IVoiceSynthesisNote` / `ISynthesisAutomation`): subscribable, **accessible only on the data thread**. Used for "receive change notifications → mark dirty", "`GetNextPendingSynthesisRange` chunking decisions", and "the `SynthesizeNext` synchronous prefix pulling a snapshot".
  - **Frozen snapshot** (`VoiceSynthesisSnapshot` and the `*Snapshot` family, `IAutomationEvaluator`): immutable, event-free, **cross-thread safe**. Background workers **only read snapshots** and never touch any live-view object.
  - Naming is the discipline: live views (`IVoiceSynthesisContext` / `IVoiceSynthesisNote` / `ISynthesisAutomation`) are data-thread only; `*Snapshot` = frozen (cross-thread). **Violating this is the most common and hardest-to-debug bug in voice plugins** (a worker thread reading a live note → data races with the editing thread). During development the host asserts the data thread at live-view entry points, so cross-thread access throws immediately to help you locate it.

Manifest entry: `{ "type": "voice", "engine": "MyEngine", "name": "My Engine", "class": "My.Ns.MyVoiceEngine", "assembly": "MyVoice.dll" }` (`engine` is the immutable identity written into project files, so changing it makes old projects mismatch; `name` is an optional display name that can be translated via `localizations`; the host verifies `class` implements `IVoiceSynthesisEngine`).

### 5.1 `IVoiceSynthesisEngine`: engine lifecycle and voicebank catalog

```csharp
using TuneLab.Foundation;
using TuneLab.SDK;

public class MyVoiceEngine : IVoiceSynthesisEngine    // engine id is declared in the manifest's "engine"
{
    // Voicebank catalog (for menus/pickers, readable without creating a session).
    // Contract: it must [return immediately and never block] — the host and UI read it synchronously, with no async wait.
    // Correct approach: scan voicebanks and cache during Init; here just return the cached reference. Lazy loading (scanning disk on first get) would stall the UI.
    public IReadOnlyOrderedMap<string, VoiceSourceInfo> VoiceSourceInfos => mVoiceInfos;

    // Presentation layout for the voicebank picker (an ordered group tree): with hundreds of banks, folds the
    // dropdown into nested submenus instead of one long flat list. Parallel to VoiceSourceInfos — the layout only
    // governs "how they are arranged"; identity/lookup/project references still go through that id→info map.
    // Leaves reference ids (VoiceSourceLayoutItem.Voice), the host takes their display names from the map; group
    // names (VoiceSourceLayoutItem.Group) you localize yourself (like VoiceSourceInfo.Name). Bare voices and
    // subgroups may interleave freely at any level, nested to any depth. Ids not referenced anywhere in the layout
    // are appended by the host at top level in map order (so full coverage is not required); not overriding this
    // member = everything flat (= no grouping).
    public IReadOnlyList<VoiceSourceLayoutItem> VoiceSourceLayout =>
    [
        VoiceSourceLayoutItem.Voice("solo-a"),                     // bare top-level voice (interleaved with groups)
        VoiceSourceLayoutItem.Group("Japanese", [                  // a group (name already localized)
            VoiceSourceLayoutItem.Voice("jp-f1"),
            VoiceSourceLayoutItem.Group("Dialects", [              // nest subgroups as deep as you like
                VoiceSourceLayoutItem.Voice("jp-kansai"),
            ]),
        ]),
        // any remaining unlisted banks → the host appends them at top level
    ];

    // Parameterless, throws on failure: the host catches at the call boundary (responsibility attribution is by capture point, not exception type).
    // No install path is passed in — locate your package directory yourself via typeof(MyVoiceEngine).Assembly.Location (see §5.10).
    // Init is called lazily (on first use); the host may also warm it up proactively. Only "holding expensive resident state across calls" (e.g. loading a model) needs Init/Destroy.
    public void Init() { /* scan voicebanks to fill mVoiceInfos; load/warm up the model */ }
    public void Destroy() { /* release resident resources (unload the model, close the ONNX session, etc.) */ }

    // One session per part: the selected voicebank is baked into the context (context.VoiceId, a key of
    // VoiceSourceInfos), so CreateSession does not list it separately; context is that part's input live view,
    // living and dying with the session. The session is a lightweight handle — heavy model loading should be lazy.
    public IVoiceSynthesisSession CreateSession(IVoiceSynthesisContext context)
        => new MySession(context);

    // —— Declaration side (property panels / automation tracks): see §5.2, all on the engine, independent of session instances ——
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => mAutomationConfigs;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => mSynthesizedParameterConfigs;
    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => mPartConfig;
    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => mNoteConfig;

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}
```

**`VoiceSourceInfo` fields** (voicebank catalog metadata; the session does not re-carry these):

```csharp
public sealed class VoiceSourceInfo
{
    public required string Name { get; init; }          // voicebank display name (localizable, see "Localization" at the end of §5.2)
    public required string Description { get; init; }   // one-line summary
    public ImageResource? Portrait { get; init; }       // optional portrait (shown in the piano window); null = none
}
```

`Portrait` uses `FileImageResource` (`TuneLab.Foundation`) and takes an **absolute path** — build it from your own package directory (see §5.10). It can point to a single image or to a frame-sequence directory, which the host decodes as needed:

```csharp
var portrait = new FileImageResource(System.IO.Path.Combine(packageDir, "voices", voiceId, "portrait.png"));
mVoiceInfos.Add(voiceId, new VoiceSourceInfo { Name = "Alice", Description = "...", Portrait = portrait });
```

> When switching the sound source (switching engines) the host discards the old session and rebuilds it with the new `voiceId` (the context is rebuilt too); the engine object itself lives on, and `Init` is called only once on first use.

### 5.2 Engine declaration side: property panels and automation tracks

The four declaration-side methods are **on `IVoiceSynthesisEngine`** (not on the session) and are **all pure functions** (same input → same output, no side effects, lightweight); the host calls them on every parameter commit and diffs the result to the UI. A statically-declared plugin ignores `context` and returns a fixed map/config; for conditional UI (a control/track that appears only when some switch is on) read the current values from `context` to decide what to return; multi-voicebank engines (one engine with multiple `voiceId`s) branch on the part view's **`VoiceId`** (`context.Part.VoiceId` on the note side, `context.Parts[i].VoiceId` on the part side).

> **Why on the engine and not the session**: Declaration depends only on `(voiceId, current part values)` and touches no synthesis runtime state — it is inherently a pure function. Putting it on the engine lets the host compute declarations (track set / panel) **before creating a session**, so the session returned by `CreateSession` can **subscribe in its constructor to the automation tracks it declared** (during construction `context.Automations` already contains the tracks you declared, retrievable via `TryGetValue` / enumerable — the declaration is ready). If declaration lived on the session it would be a deadlock: the session wants to subscribe to the tracks it declared, but the declaration is only available after the session finishes constructing — during construction `context.Automations` would not yet be filled with your declared tracks. `DefaultLyric` is the only value that stays on the session (it is used at runtime, see §5.3).

```csharp
// Automation track set (part level): continuous tracks and piecewise tracks share one ordered map, declaration order = presentation order. context.Parts[i].VoiceId selects which voicebank.
public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => mAutomationConfigs;
// Read-only synthesized parameter track declarations (engine-produced, non-editable curves such as energy). Return an empty map if there are none.
public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => mSynthesizedParameterConfigs;
// Part-level property panel (depends only on the part's own sparse values + the part view's VoiceId).
public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => mPartConfig;
// Note-level property panel (depends on part settings + the merged values of the selected notes).
public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => mNoteConfig;
```

> `IVoiceSynthesisPartPropertyContext` (part panel / automation): **`IReadOnlyList<IVoiceSynthesisPartView> Parts`** (one value view per selected part; multiple parts may be selected). `IVoiceSynthesisNotePropertyContext` (note panel, a **separate interface, not inheriting**): **`IVoiceSynthesisPartView Part`** (the **single** part the notes belong to — a note always belongs to one part) + **`IReadOnlyList<IVoiceSynthesisNoteView> Notes`** (one view per selected note). A part view carries `VoiceId` / `PartProperties` (`PropertyObject`) / `Notes` / `Automations`; a note view carries `StartTime` / `EndTime` / `Pitch` / `Lyric` / `Properties` / `LeadingPhonemes` / `BodyPhonemes` / `BodyOffset`. If you don't care about multi-selection, reduce the values to one tri-state `PropertyObject` with `.Merge()` (the `PropertyObjectExtensions` extension method, in `TuneLab.Foundation`, taking an `IEnumerable<PropertyObject>`) — e.g. `context.Notes.Select(n => n.Properties).Merge()` (same key all-equal → the value, unequal/partly-missing → `Multiple`) — and write as if single-selected; if you need per-member truth (e.g. combining seeds of unequal-length arrays) iterate the list directly. Having voiceId in the context makes the voice context permanently diverge from effect's `IEffectSynthesisPropertyContext` (which has no equivalent) — this is intentional: effect is a single-type engine and has no notion of "which bank to pick".

**Note / part property conventions (the keyed `Properties`, the sole channel for per-note/per-part parameters)**:

- The fixed fields of `IVoiceSynthesisNote` are only the minimal common musical quantities (`Id`/`StartTime`/`EndTime`/`Pitch`/`Lyric`/`LeadingPhonemes`/`BodyPhonemes`/`BodyOffset`). **All voice-specific per-note parameters (tension, breathiness, gender, etc.) go through `note.Properties` (keyed)** — adding a new parameter = adding a key in `GetNotePropertyConfig`'s `ObjectConfig.Properties`, without touching the fixed interface surface. Part-level specific parameters go through `GetPartPropertyConfig` the same way.
- Build the panel from the control-config vocabulary (all in `TuneLab.SDK`): `SliderConfig` (constructors are sealed, use static factories only: `SliderConfig.Linear(default, min, max)` for continuous, `SliderConfig.Integer(default, min, max)` for integer, `SliderConfig.Create(default, scale)` for a custom `INormalizedScale`; fluent `.WithFormat(INumberFormat)` to customize value display/read-back, `.WithRandomizable()` to declare it randomizable — the host gives a random entry on the right, and clicking it re-picks the value uniformly on the scale in normalized space, suitable for random seeds and the like; `.WithMinLabel(text)` / `.WithMaxLabel(text)` add descriptive text at the ends of the range (e.g. min="Soft", max="Hard", translated by the plugin, may set only one end) — shown at the slider's ends, and when the user pins this property onto the parameter panel the same text serves as the bounds, sharing semantics with `AutomationConfig`'s same-named fields), `ComboBoxConfig.Create(options)` (value/display separated, so "UI in Chinese / stores the enum value underneath"; `.WithDefault(option)` sets the default selection; without it (or if the given item can't be selected) the default is the first selectable leaf — groups and separators are skipped, and with nothing selectable the default is empty; options may mix value types, and a stored value that isn't among the options is kept as is and shown in amber; `new ComboBoxItem(groupName, subItems)` is a group — not selectable itself, it opens a submenu and may nest further; `ComboBoxItem.Separator(label)` is a separator (label optional) that works both at the top level and inside a group's items (at the top level `.AppendSeparator(label)` also works); once there are 10 or more leaves the host adds a search box at the top of the menu), `CheckBoxConfig.Create(default)`, `TextBoxConfig.Create(default)` (`.WithPassword()` for masking; `.WithMultiline(maxVisibleLines)` for a multi-line box — the box grows with the content and stops at `maxVisibleLines` rows, scrolling beyond it (`0` = no cap, grow forever); the value stays ONE string with newlines inside it, so nothing about storage or undo changes. Masking has no multi-line form: declaring both keeps the field single-line), `PathPickerConfig.CreateFile(default)` / `PathPickerConfig.CreateFolder(default)` (a path box with a browse button that opens the system picker; the value is the picked path as a plain string — `.AppendFileType(name, "*.exe")` adds a file-type filter, `.WithPickerTitle(text)` sets the dialog title; the host does NOT check that the path exists — validate it yourself in `ApplySettings`/`Init`); all the above constructors are sealed, use static factories only. The container is `ObjectConfig.Create(IReadOnlyOrderedMap<PropertyKey, IControllerConfig>)` — like the value configs, its constructor is sealed and you go through the static factory. A `PropertyKey` is `(id, displayText?)` and converts implicitly from a bare string or from a tuple, so `{ ("tension", "Tension"), … }` gives the key its display name; a bare `"tension"` shows the id. The `+` candidates of the variable-length containers `ListConfig` / `ExtensibleObjectConfig` are shaped like the dropdown: `new AddableElement(groupName, subItems)` / `new AddableKey(groupName, subItems)` is a group (a submenu, may nest) and `AddableElement.Separator(label)` / `AddableKey.Separator(label)` is a separator, with the same search-box rule; once an extensible object hides the keys already added, a separator whose section is left empty is hidden too; groups and separators carry no `Template` (and no key Id on `AddableKey`), so check `SubItems` first when reading candidates. An `ExtensibleObjectConfig` shows its entries in the order of the `Properties` you return: the keys in the data are unordered (a key removed and added again can land in the freed slot), so don't copy the data's key order — sort by your own candidate catalog; if users should arrange the order themselves, use a `ListConfig` of objects (its elements can be reordered by dragging their index). To let users pick several values from a fixed set, use `TagConfig.Create(items)`: the picked values are shown as chips that wrap onto new lines, with a `+` at the end that opens the candidate menu. The value is a `PropertyArray` of the picked candidates' values, **ordered and without duplicates**: a newly picked tag is appended at the end, users can drag chips to reorder them (so order can carry meaning, e.g. most important first), and a value already picked is hidden from the `+` menu. Candidates are `TagItem`s, shaped like `ComboBoxItem` (value/display separated, any primitive value; `new TagItem(groupName, subItems)` is a group, `TagItem.Separator(label)` a separator, same search-box rule) but a separate type. `.WithDefault([values])` sets the initially picked tags (none by default). Candidate values and default tags must be real values: `PropertyValue.Null` / `PropertyValue.Multiple` throw an argument error on construction. With several items selected the host shows the union, and tags only some of them have are drawn faded — picking one from `+` adds it to the rest. A stored value that is not among the candidates is kept as is and shown in amber (how it takes effect is up to you), the same as a dropdown's unknown value. A `ListConfig` whose elements are `ComboBoxConfig`s with the same options stores the same data shape but allows duplicates and per-element editing — pick the one whose semantics you need. If you migrate an existing field (for example an `ExtensibleObjectConfig` of empty objects used as on/off switches) to `TagConfig`, the data shape changes: put the tags under a new key, and see "Closures in declarations and evolving a field's shape" later in this section for keeping the old data working.

```csharp
readonly ObjectConfig mNoteConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
{
    { ("tension", "Tension"),         SliderConfig.Linear(0, -1, 1) },
    { ("breathiness", "Breathiness"), SliderConfig.Linear(0, 0, 1) },
});
```

- **Reading values**: at synthesis time, read from `VoiceSynthesisNoteSnapshot.Properties` (a `PropertyObject` value copy) using `GetDouble(key, default)` / `GetBoolean` / `GetString`. **Sparse storage** — only fields the user has changed are present; if not found use the default you declared (`PropertyObject`'s `Get*` second argument is the fallback; pass the same default as declared).
- **`AutomationConfig`**: `DefaultValue` / `MinValue` / `MaxValue` / `Scale` / `Color` (e.g. `"#E5A573"`) / `Randomizable` (adds a random entry to the right of the default-value panel's slider, best for continuous tracks) / `MinLabel` / `MaxLabel` / `Format`, built by `AutomationConfig.Create(...)` plus the fluent `WithColor` / `WithDefault` / `WithRandomizable` / `WithMinLabel` / `WithMaxLabel` / `WithFormat`. The **track's display name is not on the config** — it rides on the map key (`PropertyKey`'s `DisplayText`), the same as property panels. **`DefaultValue = double.NaN` ⇒ a piecewise track** (no default baseline, disconnected between segments, e.g. pitch-like, bend); a real number ⇒ a continuous track (has a value everywhere, has a baseline, e.g. growl). Synthesized parameter tracks are always piecewise (`DefaultValue = NaN`).
- **Value-axis scale**: `AutomationConfig.Create(minValue, maxValue)` is a linear axis; the `Create(INormalizedScale)` overload takes a custom scale (mirroring `SliderConfig.Create`) — e.g. `NormalizedScale.Integer(min, max)` makes it an integer track, or implement your own log axis etc. **A discrete scale ⇒ the signal lands on the grid everywhere**: beyond snapping anchors on write, the host projects the continuous Hermite output back onto the scale at **evaluation and rendering** time (the curve renders as a staircase, and `Evaluate` returns already-gridded final values), so the **engine need not round** and every edit path (load / preset / fed-back data) is covered. On a continuous scale the projection is the identity (up to ULP-level float round-trip noise — do not rely on values being passed through bit-for-bit).
  **The scale defines the shape and grid of the value axis, not a value-range guarantee.** The host does *not* clamp evaluated values into `[MinValue, MaxValue]` — that guarantee cannot be honoured, since monotonicity is only a documented convention (`INormalizedScale` is a public interface and `NormalizedScale.Custom` takes arbitrary lambdas), and for a non-monotonic scale the endpoints are not the extremes, so clamping would neither land inside the range the engine has in mind nor leave legitimate values intact. Out-of-range values do reach `Evaluate` — the realistic sources are projects saved before you changed a track's range, presets carried over from another source, and values your own engine fed back — so **validate in the engine to whatever degree your algorithm needs** (array indices, table lookups and divisors are the ones that bite). The host's **strokes** themselves stay in range (dragging and anchor entry are both clamped to the range), but the value model is **additive**, so ordinary use goes out of range too: anchors store an offset relative to `DefaultValue` (the user later moves the default-value slider ⇒ the whole set of anchors shifts out of range when evaluated), and vibrato deviation is likewise added on top of the track (a curve sitting near the upper bound ⇒ out of range once vibrato affects that track). Both are deliberate consequences of the additive model, not defects. The parameter panel clips its drawing to the pane, so an out-of-range anchor renders flush against the boundary.
- **Binary interval track (band / toggle)**: for an on/off interval track (e.g. a breathiness switch, section mute), declare a **piecewise track with a degenerate range** — `AutomationConfig.Create(v, v)` (`Create` defaults to `DefaultValue=NaN` ⇒ piecewise; `min==max` ⇒ no value axis). The host recognizes this form and renders it as a **full-height toggle band** (segment = on-interval highlighted, gap = off/blank) instead of a curve, with interaction switched to horizontal drag = paint on / right-drag = paint off (vertical ignored, there is no height). Consumption is pure segment presence: `!double.IsNaN(evaluator.Evaluate(t)[i])` means "on"; the value inside a segment is irrelevant and need not be read. Interval boundaries = the segment's anchor span, dragged precisely by the user.
- **Conditional declaration + orphaned data**: the track set may show/hide with parameters (e.g. a Growl track exposed only when some switch is checked). After a track disappears from the declaration, the host **keeps its already-drawn curve (hidden, not deleted, not participating in synthesis)**, and rolling the parameter back to make the track reappear restores it as-is — you need not worry about losing user data when toggling conditional tracks.

> ⚠️ **Automation parameter names must avoid host reserved names**: the keys of `GetAutomationConfigs` are merged and displayed with the host's built-in automations, and **a name clash with a built-in is claimed by the built-in and your parameter won't show**. Known reserved names: **`Volume`**, **`VibratoEnvelope`**. Use your own distinctive names (e.g. `Breathiness` / `Growl` / add a prefix).

> **Localization**: `DisplayText`, `ComboBox` option text, track names, voicebank names/summaries are all translated by you — read `TuneLabContext.Global.Language` (e.g. `"zh-CN"`) and produce text from your own dictionary; the host does not do any lookup. When untranslated, just return the English as-is. The manifest's `name`/`description` are localized via the `localizations` field.

#### Phoneme properties (`GetPhonemePropertyConfigs`)

Beyond notes, the engine can also declare user-editable custom properties on **phonemes** — parallel to `GetNotePropertyConfig`, but both the declaration side and the value-reading side drop down to a single phoneme. Typical use cases: attaching an "articulation strength" slider to the vowel (nucleus), or a different set of properties to consonants.

```csharp
// per-phoneme property declaration (required, must be implemented just like GetNotePropertyConfig):
// **reuses the note declaration context IVoiceSynthesisNotePropertyContext** (there is no separate phoneme context) —
// each IVoiceSynthesisNoteView carries the phoneme lists LeadingPhonemes / BodyPhonemes. Return a schema map **keyed by
// nucleus-relative slot**: key = slot (0 = nucleus, <0 = leading consonants (closer to the nucleus = closer to −1), >0 = post-nucleus),
// value = the schema of that slot (that "role") across the whole selection.
public IReadOnlyMap<int, ObjectConfig> GetPhonemePropertyConfigs(IVoiceSynthesisNotePropertyContext context)
{
    var map = new Map<int, ObjectConfig>();
    foreach (int slot in context.Notes.UnionSlots())               // SDK PhonemeSlots helper: the selection's slot universe (ascending)
        map.Add(slot, slot == 0 ? VowelConfig : ConsonantConfig);  // differentiated by role: nucleus vs consonants
    return map;
}
```

- **Required, not a default interface method**: `GetPhonemePropertyConfigs`, like `GetNotePropertyConfig` / `GetPartPropertyConfig`, **must be implemented**. An engine that declares no phoneme properties just does `return [];` (an empty map).
- **Reuses the note declaration context**: there is no longer a separate phoneme context — the phoneme declaration context is inherently equivalent to the note declaration context, so it directly takes `IVoiceSynthesisNotePropertyContext` (the same `{ Part; IReadOnlyList<IVoiceSynthesisNoteView> Notes }` as the note panel), and each `IVoiceSynthesisNoteView` carries the phoneme lists `LeadingPhonemes` / `BodyPhonemes`.
- **Keyed by nucleus-relative slot**: key = the phoneme's nucleus-relative coordinate `slot = index − LeadingPhonemes.Count` (0 = nucleus, <0 = leading consonants, >0 = post-nucleus), i.e. the phoneme's **role** within its note; value = that role's schema across the whole selection. The schema is granted to a role, not an individual phoneme — with multiple notes selected, the host merges each note's phoneme at the same slot into one row sharing that schema; with a single note selected, slots map one-to-one to phonemes with no loss of expressiveness. **An empty map = no phoneme has any properties**; a missing slot = that role has no properties (the host treats it as property-less, no error) — keys are self-describing, there is no positional "length must match exactly" contract.
- **Multi-select merging belongs to the engine (same contract as `GetNotePropertyConfig`)**: when the schema depends on current property values, merge the values of the members at the same slot yourself (three-state) before conditioning — `context.Notes.Select(n => n.PhonemeAt(slot)?.Properties)`, take the non-nulls and `Merge()`. The host only merges **values** per slot, never schemas.
- **The `PhonemeSlots` helper (shared pure functions in the SDK; engine and host use the same single source, so they can never drift)**: `note.PhonemeAt(slot)` (the phoneme at that slot, null if absent), `notes.UnionSlots()` (the selection's slot universe, an ascending contiguous range); the nucleus index is simply `LeadingPhonemes.Count` (self-evident expression, no forwarding API).
- **`IVoiceSynthesisNoteView.LeadingPhonemes` / `BodyPhonemes`**: that note's phonemes as two time-ordered lists (leading consonants; nucleus + trailing consonants) with elements of type `IVoiceSynthesisPhonemeView`. The full ordered sequence is `LeadingPhonemes` ++ `BodyPhonemes` (leading consonant → nucleus → trailing consonant); a phoneme's position within the note = its index in that concatenation. There is deliberately no merged `Phonemes` projection on the view — derive it yourself, or use the `PhonemeSlots` helpers above which index across both lists.
- **`IVoiceSynthesisPhonemeView`** (reads the phoneme's current values on the declaration side): `string Symbol` / `double Duration` / `double StretchWeight` / `PropertyObject Properties` (a snapshot of that phoneme's current property values). Leading/body attribution is the note view's `LeadingPhonemes` / `BodyPhonemes` list membership (+ `double BodyOffset`), not a per-phoneme flag. You may condition the schema further based on these current values + slot.
- The schema still uses the same control-config vocabulary (`SliderConfig` / `ComboBoxConfig` / `CheckBoxConfig` / `TextBoxConfig` / `PathPickerConfig`, container `ObjectConfig`), written the same way as note properties.

- **Reading values at synthesis time**: read from the snapshot — `VoiceSynthesisNoteSnapshot` exposes `LeadingPhonemes` / `BodyPhonemes` (each `IReadOnlyList<VoiceSynthesisPhonemeSnapshot>`) + `double BodyOffset`, each item `{ string Symbol; double Duration; double StretchWeight; PropertyObject Properties }`. The geometry fields (`Symbol`/`Duration`/`StretchWeight`) are read directly flat (leading/body attribution is the list membership); when feeding `PhonemeLayout.Resolve` rebuild a `SynthesizedPhoneme` from the fields (see §5.7). `Properties` is that phoneme's frozen property values (unset = `PropertyObject.Empty`), read with `GetDouble(key, default)` etc., stored sparsely, falling back to the declared default when not found.

```csharp
foreach (var ph in note.LeadingPhonemes.Concat(note.BodyPhonemes))   // note is a VoiceSynthesisNoteSnapshot (no merged Phonemes property on the snapshot; consumers concatenate)
{
    string symbol = ph.Symbol;                // geometry fields read directly flat
    double stress = ph.Properties.GetDouble("stress", 0);  // per-phoneme property value
}
```

**Semantic points:**

- Properties are only meaningful on **pinned phonemes** (user data); phonemes produced by the engine's automatic G2P have no properties.
- The pinned phonemes of the input live view (`IVoiceSynthesisNote.LeadingPhonemes` / `BodyPhonemes`) carry **no properties** (see §5.3 / §5.7); properties only appear in the synthesis snapshot (`VoiceSynthesisPhonemeSnapshot.Properties`).
- **Editing UI**: the sidebar phoneme-property panel is done — **one row per slot** (a symbol label + that slot's controllers); with multiple notes selected, each note's phoneme at the same slot merges into that row (three-state values, edits fan out). Still to be done is a phoneme **selection model** (a phoneme has no selection state of its own yet — a host-side matter, not part of the SDK surface); until the selection model is complete, the host uses all phonemes of the selected notes as the panel scope.

#### Declarations that depend on derived state: `InvalidatePart` / `InvalidateNotes`

Some panel options depend on something the engine **computes itself**, not just on values the user typed — typically background lyric parsing or tone analysis: only after parsing finishes do you know which options to expose for a note. The host cannot see such derived state and only re-asks for declarations on parameter commit / selection change, so when your analysis finishes you must **announce** it:

```csharp
// in the session (callable from any thread; the host marshals to the data thread and coalesces — dozens of calls refresh once)
mContext.InvalidateNotes([note.Id]);   // the note / phoneme declarations of these notes are stale
mContext.InvalidatePart();             // all declarations of this part are stale (cascades down to notes; no need to list them)
```

Declaration methods are engine-level calls, so identity is how you look up what the session computed: `IVoiceSynthesisNoteView.Id` is **the same token** as the live view's `IVoiceSynthesisNote.Id`, and `IVoiceSynthesisPartView.PartId` equals `IVoiceSynthesisContext.PartId`. Both stay constant for the object's lifetime and survive session rebuilds (voicebank switch / tempo change), so store results on the engine keyed by id:

```csharp
public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context)
{
    bool analyzed = mAnalyses.TryGetValue(context.Part.PartId, out var a)
        && context.Notes.All(n => a.Tones.ContainsKey(n.Id));
    return analyzed ? mConfigWithToneOptions : mBaseConfig;
}
```

Key points:

- **It only voids your own answers**: it announces no user-data change and makes no range pending re-synthesis — if the analysis affects synthesis, mark dirty through your own invalidation as usual.
- **Cascade direction**: `InvalidatePart` covers the part property panel, the automation track set and the synthesized parameter track set, and cascades down to note / phoneme declarations; `InvalidateNotes` covers only those notes and **does not cascade up** — if a note result also changes the part panel, call `InvalidatePart` as well.
- **Continuation is excluded**: `IsContinuation` must be answered **synchronously** from current data, never "answer now, change after parsing"; this signal does not make the host re-ask it.
- **No cycles**: derived state may only be a function of user data (lyric / pitch / timing / property values), never of declaration output.
- Unknown / deleted ids are silently ignored; calls after the session is disposed are ignored (the new session should re-analyze). An empty `PartId` means the declaration is evaluated without a concrete part (e.g. static schema enumeration) — answer as "no derived state".
- Don't call when nothing changed — the host coalesces, but not calling is cheapest.

Reference implementation: `LyricAnalysis` in `tests/plugins/V1.Voice` (a lyric ending in a tone digit such as `ma3` makes Tone Depth appear on the note panel and Tone Strength on the part panel after ~0.6 s).

#### What property values mean (applies to every panel)

This section applies wherever you declare a panel: part / note / phoneme properties, instruments, effect parameters, extension settings. Most plugin pitfalls live here, so read it before writing a panel; the sections on individual controls only cover what is specific to them.

- **The default value is "unset", and it also fixes the field's type.** A field the user never changed isn't in the data (storage is sparse); reading it gives the default, so you don't need some other value (such as `PropertyValue.Null`) as an "unset" sentinel. The default therefore has to be a real value of the field: with `PropertyValue.Null` or `PropertyValue.Multiple` as the default, the field can never hold a value — tags and canvases throw an argument error on construction; a drop-down with no default, or a default that can't be selected, uses its first selectable option.
- **Reset to default = remove the key.** "Reset to Default" on the title row removes the stored value, after which reads give the default. Don't make meaning depend on "was it ever stored": a value set equal to the default reads the same as one never set.
- **A stored value of a different type than the default reads as the default.** Sliders and number boxes only take numbers, check boxes booleans, text boxes and paths strings, tags and lists arrays, object containers objects. The exception is the drop-down: option values may mix types, whatever is stored is read as is, and a value not among the options is kept and shown in amber. This is the host's safety net for bad data — **don't use it for migration** (see "Changing a field's shape" in the next section).
- **Read with the right API.** On a `PropertyObject` from a snapshot or a declaration context:
  - numbers with `GetDouble(key, default)`, booleans with `GetBoolean`, strings with `GetString`. Don't write `GetValue(key, 0)`: the `0` makes the generic type `int`, and on hosts with SDK 1.1 or earlier it always returns the default.
  - values of no fixed shape (arrays, objects) as they are: `props.Map.TryGetValue(key, out var value)`. `GetValue(key, PropertyValue.Null)` also always returns the default on older hosts.
  - validate whatever you read: undo, presets, paste and older versions of your plugin can all bring any shape.
- **Multi-selection.** A declaration context gives each selected object's own values; merge them yourself with `.Merge()` (equal values give that value; differing or partly missing give `Multiple`). In the panel, scalars and canvases compare whole values and become the multiple-value marker as a whole when they differ (canvases: §10.1); lists merge index by index; tags show the union. An edit writes the same value to every object (lists and tags fan out per element).
- **`Multiple` only marks "differs across the selection" when reading; it is not a value.** It can't be a default, and it can't be written back — the host refuses to store it (a canvas committing it writes nothing and gets an error logged). If nothing changed, don't commit.
- **Closures, rebuilding and changing a field's shape**: see the next section.

#### Closures in declarations and evolving a field's shape (applies to every panel)

This section applies wherever you declare a panel: part / note / phoneme properties, instruments, effect parameters, extension settings.

**Capture values only.** A config can carry functions: a button's click handler (§9), a canvas factory and its collapsed summary (§10). They all run after the declaration call has returned, and the declaration context and its views are valid only during that call. Read what you need — the PartId, note ids, phoneme symbols, values already filled into sibling fields — in `GetXxxConfig`, and let the closure capture those values, never the context or a view object.

```csharp
public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context)
{
    var props = BaseNoteProperties();
    // offer "re-analyze" only for a single note: a multi-selection has no "one" note to match
    if (context.Notes.Count == 1)
    {
        string partId = context.Part.PartId, noteId = context.Notes[0].Id;   // read the values, then capture them
        props.Add(("reanalyze", "Tones"), ButtonConfig.Create("Re-analyze", _ => ReanalyzeAsync(partId, noteId)));
    }
    return ObjectConfig.Create(props);
}
```

**Build what depends on the context each time; cache the rest.** The host neither requires configs to be shared nor requires them to be rebuilt. Build the configs that depend on the context (like the button above) on every declaration — closures are cheap — and keep caching the ones that don't.

**When outside data changes, have the host declare again.** A closure and the control instances it builds live for one declaration (canvases: §10.3). The host declares again by itself when the user edits a value or the selection changes; when data you computed yourself changes (tones from an analysis, results cached after synthesis), call `InvalidatePart` / `InvalidateNotes` (previous section) so the host calls your declaration again and the closures pick up the new values.

**Changing a field's shape: use a new key.** When a new version of your plugin changes a field's value shape (say from an object to an array, or different members inside an object), users' projects still hold values in the old shape. The host doesn't interpret shapes — only you know what an old value meant — so compatibility is your job:

- **Put the new shape under a new key**; don't change the shape under the old key. For a stored value of the wrong type the host only has a safety net — it treats the value as absent and shows the declared default — which tolerates bad data and goes by data type: if the old and new shapes differ only in an object's members and have the same type, it can't tell. Don't rely on it for migration.
- `GetXxxConfig` sees the current values (each view's `Properties` snapshot). When the old key holds a value in the old shape, pick one:
  - keep returning the old-shape config for the old key (shown only while it has content, remove-only if you like), and read it with the old logic when synthesizing;
  - or convert the old value to the new shape and return it as the **default** of the new key's config: the user sees the converted value, and the first edit stores it in the new shape under the new key. When synthesizing, use the new key if present, otherwise convert the old key with the same logic.
- **Multi-selection**: the default is shared by the whole declaration. If the selected objects' old values convert to different results, no single default represents them all — return the plain default then.
- **Why changing the shape under the old key always fails when the types match**: panel edits change the value currently shown and write the whole thing back (adding a tag writes "the shown tags + the new one"). When the old and new shapes have the same type, the host shows the old value as is, the user's edit builds on it, and the converted value you offered as the default never appears.

### 5.3 Input live view `IVoiceSynthesisContext` and `IVoiceSynthesisNote`

> The session itself (`IVoiceSynthesisSession`) now declares only **`DefaultLyric`** (`string`, the default lyric for a new note) — it is a runtime value consumed only after creation and does not participate in pre-construction declaration, so it stays on the session instance; all other declarations (tracks/panels) are on the engine (§5.2).

The context is implemented by the host, is session-scoped (dies with the session), and is **accessible only on the data thread**. You subscribe to it to perceive input changes, and in the `SynthesizeNext` synchronous prefix you materialize a snapshot from it via `GetSnapshot`.

```csharp
public interface IVoiceSynthesisContext
{
    string VoiceId { get; }   // the selected voicebank (a key of VoiceSourceInfos) — baked into the context, which is why CreateSession does not take it
    IReadOnlyNotifiableLinkedList<IVoiceSynthesisNote> Notes { get; }   // linked list: consume in enumeration order, First/Last, note.Next/Previous neighbor navigation; WhenAny auto-wires member add/remove
    IReadOnlyNotifiablePropertyObject PartProperties { get; }
    IReadOnlyMap<string, ISynthesisAutomation> Automations { get; }   // get the editable tracks you declared (TryGetValue / enumerate)
    ISynthesisAutomation Pitch { get; }            // absolute pitch constraint (piecewise: has value = pinned, NaN = free), see §5.6
    ISynthesisAutomation PitchDeviation { get; }   // additive deviation (continuous, default 0, never NaN), see §5.6
    VoiceSynthesisSnapshot GetSnapshot(IReadOnlyList<IVoiceSynthesisNote> notes);  // see §5.5
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate);             // see §5.8
    IActionEvent Committed { get; }            // logical-edit closure point, see §5.9
}
```

**`Notes` ordering and overlap**: total-order deterministic — `StartTime` ascending → same start, `EndTime` descending (long note first) → still tied, keep insertion order. Notes **may overlap** (chords): the sequence passes overlapping notes through as-is, and de-overlapping (e.g. "later covers earlier") is **your responsibility** (a monophonic plugin truncates as needed; a chord plugin consumes overlaps as-is).

**`IVoiceSynthesisNote` fields** are all subscribable properties (`IReadOnlyNotifiableProperty<T>`, with `Value` / `WillModify` / `Modified`): `StartTime`/`EndTime` (global seconds), `Pitch` (`int` semitones), `Lyric` (`string` — the text this note sings: either the lyric the user typed or an explicit pronunciation override they set. **The host does not normalize it into any one phonology**, so a Han character can arrive as-is; G2P belongs to your engine, which is what lets it honor a dialect or any other non-pinyin phonology), `Phonemes` (`IReadOnlyList<SynthesizedPhoneme>`, see §5.7), `LeadingPhonemes` / `BodyPhonemes` (`IReadOnlyNotifiableProperty<IReadOnlyList<SynthesizedPhoneme>>` each) + `BodyOffset` (see §5.7), `Properties` (keyed per-note parameters). Besides the properties there is a plain `string Id` — the note's run-time identity, which is what you key the phoneme product map by (§5.7). There is also a `Next`/`Previous` neighbor chain — **for data-thread chunking decisions only** (inside an event handler you have only the note's own reference, no list index); at synthesis time you must navigate neighbors by index over the snapshot's ordered list and never touch live notes again.

### 5.4 Scheduling: `GetNextPendingSynthesisRange` (peek) and `SynthesizeNext` (commit)

The host owns the global playback line and **drives step-by-step synthesis**: first peek the boundary of "the next block to synthesize" within the window, then commit synthesizing that block.

```csharp
// peek: the pure-value second boundary of the next block to synthesize within the window, [no side effects]. null = nothing to synthesize in the window.
// Executes cheaply on the data thread (it will be asked speculatively by multiple sessions, most not chosen — don't do heavy work or capture here).
public SynthesisRange? GetNextPendingSynthesisRange(double startTime, double endTime)
{
    // Make a [deterministic] chunking decision based on the full part, returning the next dirty block [start,end].
    // Determinism is key: at commit time the chunking is recomputed over the same window and must produce the same block as this peek.
    return FindNextDirtyPiece(startTime, endTime) is { } p ? new SynthesisRange(p.StartTime, p.EndTime) : null;
}

// The commit argument = the [same window] as the peek that selected it (not the SynthesisRange that GetNextPendingSynthesisRange reported).
public async Task SynthesizeNext(double startTime, double endTime, CancellationToken cancellation = default)
{
    // —— Synchronous prefix (still on the data thread): recompute chunking over the same window + pin down this block's notes + GetSnapshot to materialize a snapshot ——
    if (FindNextDirtyPiece(startTime, endTime) is not { } piece) return;
    var snapshot = mContext.GetSnapshot(piece.Notes);
    piece.Dirty = false;            // new changes arriving during synthesis re-mark dirty and naturally re-queue after completion
    mStatusChanged.Invoke();        // mark this segment as entering Synthesizing (mStatusChanged is the ActionEvent field behind the IActionEvent StatusChanged)

    // —— offload: the worker only reads snapshot to compute PCM/phonemes/curves (never touches the live view) ——
    var report = new Progress<double>(p => { piece.Progress = p; mStatusChanged.Invoke(); });
    var rendered = await Task.Run(() => Render(snapshot, piece.Notes, report, cancellation), CancellationToken.None);
    if (rendered == null) return;   // cancelled: return normally, the product keeps the previous version

    // —— marshal back to the data thread to publish the product (swapping references = immutable) ——
    piece.Segment?.Dispose();       // drop the old segment, build a new one (see §5.8)
    piece.Segment = mContext.CreateAudioSegment((long)(rendered.StartTime * rate), rendered.Audio.Length, rate);
    piece.Segment.Write(0, rendered.Audio);
    piece.Segment.Commit();
    piece.Phonemes = rendered.Phonemes;
    mStatusChanged.Invoke();
}
```

Scheduling pitfalls:

- **Atomic peek→commit hand-off**: both are in the same scheduling tick, both on the data thread, with no edit inserted in between; the commit receives the **same window** as the peek that selected it. So your chunking must be **deterministic** (unchanged data + same window ⇒ commit recomputes the same block the peek reported). If at peek time you want to leave info for the commit (a chunk cache), store it in the session's own fields; **do not** stuff it into `SynthesisRange` (which is just the two `double`s the peek returns).
- **A single session synthesizes only one block at a time**; parallelism happens across different sessions on different parts, with a concurrency cap managed ledger-style by the host.
- **Cancellation is a normal scheduling outcome**: `SynthesizeNext` returns a plain `Task`, with no outcome. On cancellation, **return normally and never throw `OperationCanceledException`** (otherwise every await is forced into a try-catch). Only errors throw an exception (the host catches, and the segment is marked `Failed`). **The slot is released only when the `await` actually returns** — a non-abortable implementation just finishes this block and returns, and resources always stay capped by the concurrency limit.
- **Progress** is reported via the status band: `SynthesisStatusSegment.Progress` ([0,1]) + `StatusChanged`, not passed through `SynthesizeNext` arguments. A `Progress<T>` constructed on the data thread captures the synchronization context, so the worker's progress reports marshal back to the data thread.
- **Offload with `Task.Run`**: `Render` only reads `snapshot`; pass `cancellation` into it so it can exit as early as possible. Note that the second argument to `Task.Run` is `CancellationToken.None` (cancellation is handled by `Render` internally checking `cancellation.IsCancellationRequested`, not by making the scheduler throw TaskCanceledException).

### 5.5 The synthesis snapshot `VoiceSynthesisSnapshot` (the core of isolation)

The worker cannot touch the live view, so the synchronous prefix of `SynthesizeNext` must **materialize everything the synthesis needs into an immutable snapshot** before offloading. `GetSnapshot` returns one at a time:

```csharp
VoiceSynthesisSnapshot GetSnapshot(IReadOnlyList<IVoiceSynthesisNote> notes);
```

- **`notes`**: the notes this synthesis needs — **in-segment notes + coarticulation neighbors** — which you pin down freely (e.g. if you want to see the previous note's trailing consonant, include it too). The returned `snapshot.Notes` is **index-aligned** with the `notes` you passed in — this is the product-attribution contract (see §5.7: the `SynthesizedPhonemes` map is keyed by `snapshot.Notes[i].Id`).
- **Automation and pitch are frozen in full, never windowed**: the snapshot takes no time interval, because the range you actually sample depends on phoneme durations, which are only known during synthesis (after offload) — a window pinned down in the synchronous prefix would be wrong (too narrow and the sampling points for padding / onset consonants fall outside it and read wrong values). What is frozen is the raw control points (anchors), which is cheap — a densely drawn four-minute pitch curve is a few hundred kilobytes and sub-millisecond, far below one inference pass — and the worker interpolates at any query point, clamping past the ends exactly as the live curve does.
- **You may pull multiple in one synthesis**: e.g. pull a phoneme-level small window to time phonemes, then pull an audio-level large window based on the phoneme results. But **it may only be called in the synchronous prefix (before offload, on the data thread)**.

What `VoiceSynthesisSnapshot` carries (all immutable values, cross-thread safe; when this later goes cross-process it is the serialized message body):

```csharp
public sealed class VoiceSynthesisSnapshot
{
    IReadOnlyList<VoiceSynthesisNoteSnapshot> Notes { get; }   // index-aligned with the notes passed in; navigate neighbors by index (no Next/Previous)
    SynthesisAutomationSnapshot Pitch { get; }            // absolute pitch constraint (frozen evaluator)
    SynthesisAutomationSnapshot PitchDeviation { get; }   // additive deviation (frozen evaluator)
    PropertyObject PartProperties { get; }                // part parameter value copy
    IReadOnlyMap<string, SynthesisAutomationSnapshot> Automations { get; }   // get the editable tracks you declared (same functional entry as the live view)
}

public sealed class VoiceSynthesisNoteSnapshot   // bottomed out to value types, no live reference whatsoever
{
    string Id { get; }                                    // the note's run-time identity — the key of the SynthesizedPhonemes product map (§5.7)
    double StartTime { get; }  double EndTime { get; }    // global seconds. EndTime = effective end (after the host de-overlaps by clamping later-covers-earlier to the next note's start, a monophonic audio measure); the host owns phoneme layout exclusively and does not expose the full end
    int Pitch { get; }         string Lyric { get; }
    IReadOnlyList<VoiceSynthesisPhonemeSnapshot> LeadingPhonemes { get; }  BodyPhonemes { get; }   // pinned phonemes as two structured lists (leading = onset consonants / body = nucleus + coda); both empty = not pinned. Element { Symbol; Duration; StretchWeight; Properties } (geometry flat), see §5.7. There is deliberately no merged Phonemes projection — concatenate the two lists yourself
    double BodyOffset { get; }   // signed offset of the body start (the junction between the two lists) relative to the note head: junction = noteStart + BodyOffset (left negative / right positive)
    PropertyObject Properties { get; }                    // per-note parameter value copy
}

public sealed class SynthesisAutomationSnapshot { IAutomationEvaluator Evaluator { get; } }
```

- **Automation is a frozen evaluator, not bare points**: `SynthesisAutomationSnapshot.Evaluator.Evaluate(times)` takes a list of seconds and returns the value at each point (`double[]`). The interpolation algorithm always lives on the host side (to prevent two interpolators from drifting apart); you just pass points and get values. This precisely solves "the query points are often intermediate synthesis products (you only know where to sample after phoneme timing), unpredictable at snapshot time".
- **If you want to sample in the prefix**: in the synchronous prefix call `Evaluator.Evaluate(...)` directly to sample into a `double[]` you store, then offload — so the background depends on no evaluator at all (recommended when the query points are already known).
- **The one discipline**: the snapshot is immutable, written once, read-only thereafter. **The host never modifies a published snapshot** — when data changes it flows through the live-view notification → you mark dirty → the next peek yields a new segment → materialize **a brand-new snapshot**. Replace rather than sync, so no lock is needed.

### 5.6 Pitch curve sampling: dual channels + per-frame values

Pitch is **two parallel channels**, composed at synthesis time by the formula:

```
finalPitch(t) = resolve(Pitch(t)) + PitchDeviation(t)
```

- **`Pitch` (absolute constraint, piecewise)**: the user's pinned absolute pitch curve (semitones). **Has value = pinned by the user, must be obeyed**; **`NaN` = free zone, you generate it yourself** (typically falling back to the note's `Pitch`, and adding your own portamento/transitions).
- **`PitchDeviation` (additive deviation, continuous)**: has a value everywhere, defaults to 0, **never NaN**. Host-side deviation sources like vibrato all pool here. It is **added on top of the resolved absolute surface**, so deviation applies to the "free zone" too (the old style laid vibrato on the drawn curve, and a free zone with no carrier would lose the deviation — this is structurally fixed here).

A reference for per-frame (control-rate) sampling (inside the worker, reading only the snapshot):

```csharp
// Place points at the control rate (e.g. 100 Hz) over the note time range, evaluate in batch, then linearly interpolate per sample.
int controlCount = Math.Max(2, (int)((noteEnd - noteStart) * kControlRate) + 1);
var times = new double[controlCount];
for (int c = 0; c < controlCount; c++)
    times[c] = noteStart + (noteEnd - noteStart) * c / (controlCount - 1);

double[] pitch     = snapshot.Pitch.Evaluator.Evaluate(times);          // absolute constraint (may contain NaN)
double[] deviation = snapshot.PitchDeviation.Evaluator.Evaluate(times); // additive deviation (no NaN)
for (int c = 0; c < controlCount; c++)
    pitch[c] = (double.IsNaN(pitch[c]) ? note.Pitch : pitch[c]) + deviation[c];  // NaN free zone falls back to the note pitch
// Afterward pitch[] is the "final semitone curve", frequency = 440 * 2^((pitch-69)/12), linearly interpolated per sample.
```

- **`times` are global seconds**, in the same time system as audio/phonemes. Batch `Evaluate` is far more efficient than per-point calls — accumulate a batch and call once.
- **`Evaluate` never requires you to understand interpolation**: a continuous track never returns NaN; a piecewise track returns NaN between segments (use it to decide "free/pinned").
- The synthesized pitch you **produce** goes through `SynthesizedPitch` (the named rich type `SynthesizedPitch { IReadOnlyList<IReadOnlyList<Point>> Segments }`, a piecewise polyline, `Point = (global second, semitone)`), for the host to draw the synthesized pitch line on the pitch track. Empty = `new() { Segments = [] }`. Other acoustic quantities (e.g. energy) go through synthesized parameter tracks (§5.2 + §5.8).

### 5.7 Phoneme I/O: `SynthesizedPhoneme` (same type read-in / output)

The phoneme descriptor is **direction-agnostic** — read-in (the user's pinned constraints) and output (synthesis products) both use the same `SynthesizedPhoneme`: it reports only "nominal duration + weight", and **not absolute position, not before/after attribution**. Leading / body attribution is a **structured pair of lists** (`LeadingPhonemes` / `BodyPhonemes`, membership *is* the classification — engine-declared, jitter-proof, cross-beat phonemes attributed explicitly, no alternating-flag illegal states); geometry is a single signed **`BodyOffset`** (the body start = the junction between the two lists, relative to the note head). Classification and geometry are orthogonal. Positioning / cross-note de-overlap compression / melisma layout are all derived by the **host** via the same duration model (an engine reporting already-compressed absolute positions would make the host misjudge the adjacency criterion, so report only natural durations and let the host own layout exclusively).

```csharp
public readonly struct SynthesizedPhoneme { public string Symbol { get; init; }  public double Duration { get; init; }  public double StretchWeight { get; init; } }
```

**Input (host → engine): `note.LeadingPhonemes` / `note.BodyPhonemes` (`IReadOnlyList<SynthesizedPhoneme>` each, per note) + `BodyOffset`**

- Represented as two "duration + weight" lists (+ the note's `BodyOffset`) rather than resolved absolute time — de-overlapping adjacent pinned notes (later-covers-earlier / **cross-note consonant-cluster compression**) must be solved jointly by the **global layout algorithm**; and using durations makes "push-style" editing easy (change one phoneme's length and the neighbors shift as a whole rather than crowding each other).
  - `Duration`: the fixed duration of a consonant (`StretchWeight=0`); the duration of a nucleus (`StretchWeight>0`) is a **fill-derived quantity** (layout ignores its recorded value, see below).
  - `StretchWeight`: the elastic-stretch weight, `>0` = the nucleus can stretch (**yields first** in global compression) / `0` = the consonant is rigid (**compressed proportionally to its nominal length**).
  - **`LeadingPhonemes` / `BodyPhonemes`**: leading = onset consonants before the nucleus; body = nucleus + trailing consonants. Membership *is* the classification (structural, jitter-proof, no derivation).
  - **`BodyOffset` (per note, signed natural seconds)**: `junction = noteStart + BodyOffset` (left negative / right positive). `0` ⇒ the body start falls **exactly** on the note head (clean, nucleus-initial); `<0` ⇒ the body's first element (vowel) straddles the head, voicing before the beat; `>0` ⇒ the leading's last element (consonant) straddles the head, extending past the beat. The head-split (for cross-note compression) uses the note head as the cut point regardless — the phoneme it cuts is **not necessarily** the junction phoneme (they are `BodyOffset` apart), which is exactly how classification and geometry decouple.
  - **Position is not stored, it is derived by layout** (single-anchor, no Σ round-trip): body accumulates rightward from the junction, leading accumulates leftward from it — consonants use fixed durations, the nucleus fills to the **note's full end** (including melisma laid over passengers), and multiple nuclei split by weight. `BodyOffset=0` makes the junction identical to the note head (same number, no add), so the leading|body boundary lands on the head with zero error.
- **The pinning granularity is a whole note**: a **non-empty** list = the user has pinned **all** phonemes (you must obey this set of constraints); an **empty** list = you do G2P from `Lyric` + fully free timing. **Partial per-phoneme pinning is not supported.**

**Resolving to real timing: call the SDK shared function `PhonemeLayout.Resolve`**

`Resolve` takes over only the "positioning" half — compressing the "phoneme **nominal durations** + note geometry" you give it, de-overlapping across notes, into real `[StartTime, EndTime]`. **How the nominal durations come about (G2P, grouping by vowel-segmented words, the `word_div`/`dur` model, head/tail padding) is still entirely on your side** (engine-specific, not eliminated); what you hand over is only the end-alignment / de-overlap layer, not the entire phoneme chain.

**Two uses, don't conflate them**:

- **Audio layout (drive frame timing with `Resolve` — you basically must use it)**: if you lay frames in per-phoneme duration order to feed the acoustic model, use the `[Start,End]` output of `Resolve` to size the frames — overlaps are compressed away and the total frame length no longer overflows the real window. **Here the choice of `FillEnd` directly shapes the audio**: for audio == host display (WYSIWYG), `FillEnd` must use **the same measure as the host** — your own effective end + melisma laid only over **continuation passengers** (notes for which your session's `IsContinuation(note)` returns true, see "Continuation and rest" below); **stop at your own end in the gap between truly-voiced notes (the gap is silence) — don't lay the vowel across the gap to the next voiced note**. Once `FillEnd` deviates from this measure (e.g. filling across a gap), the audio and display diverge, and this is **audible**, not "non-fatal".
- **Display alignment (optional)**: if you do **not** drive audio with `Resolve` and only want the phoneme lines the host draws to line up with your free audio, call it for consistency; if you don't call it, place freely — this **display-only** misalignment is the "at most the phoneme lines misalign with the waveform, non-fatal" case. That escape hatch **holds only for display, not for audio**.

Calling: materialize each note into a `PhonemeLayoutNote` (`FillStart` = the note head; `FillEnd` see above; `LeadingPhonemes` / `BodyPhonemes` / `BodyOffset` = that note's phoneme geometry), pass the whole segment to `Resolve`, and it returns an isomorphic jagged array `PhonemeTiming[][]` (`{ Start, End, Duration }`, destructurable as `var (s,e)=`) — `result[i][j]` = the real placement of the j-th phoneme of note i in the concatenated order (`LeadingPhonemes` ++ `BodyPhonemes`). `Resolve` holds for any contiguous note range; the host display passes a window, you pass the whole segment, same function. What's frozen is only the I/O shape; the compression body can evolve host-side, and since you bind that copy at runtime it doesn't drift.

**Pinning override**: `snapshot.Notes[i]`'s `LeadingPhonemes` / `BodyPhonemes` non-empty = that note is user-pinned, so when materializing `PhonemeLayoutNote` use its pinned two lists + `BodyOffset` rather than your G2P prediction; only use the prediction when both are empty. The snapshot's list elements are `VoiceSynthesisPhonemeSnapshot`, so rebuild the geometry fed to layout as `SynthesizedPhoneme` from `ph`'s fields (`{ Symbol = ph.Symbol, Duration = ph.Duration, StretchWeight = ph.StretchWeight }`), set `PhonemeLayoutNote.LeadingPhonemes` / `BodyPhonemes` / `BodyOffset` from `snapshot.Notes[i]`, and take per-phoneme properties from `ph.Properties` (see "Phoneme properties" in §5.2 and the end of §5.7).

- **Global allocation semantics (multiplication within available space / proportional allocation, per-note independent, no single/cross two paths)**: each note takes its available space `[note head … nucleus fill end FillEnd]`, collects the phonemes falling within it — this note's non-leading (nucleus + trailing consonants), plus **only when adjacent to the next note** the next note's leading consonants — and allocates uniformly by the **scale ratio `len/d = r^w`**: a stretchable phoneme (w>0) has its length multiplied by `r^w` of its original length, a rigid phoneme (w=0) always stays at its original length; `r` is the single global reference scale ratio uniquely determined by "total length after allocation = available space" (>1 stretch / <1 compress / =1 unchanged). **Same weight ⇒ same scale ratio (proportional shape preservation, relative proportions unchanged)**, and larger `w` means more drastic stretching (`w=2` is the square of `w=1`). Two degenerate cases: ① the available space is squeezed to where even the rigid nominal lengths don't fit (`space ≤ Σ_{w=0}d`) → stretchable phonemes all go to 0, and rigid phonemes compress proportionally to their nominal lengths to fill it; ② all `w=0` (no stretchable phonemes) → everything scales proportionally by nominal length as a whole. Example `kas`+`bus` adjacent: the nucleus `a` absorbs the stretch exponentially, the consonants `s`/`b` are rigid, and only when squeezed to the limit do the consonants get proportionally compressed. **Invariant: a phoneme never overflows the available space.**
- **Gap (silence) = phonemes don't affect each other**: only **adjacent / overlapping** neighbor notes cooperate across notes (sharing that available-space allocation above). When two notes **have a gap** (the earlier note's content end < the later note's nucleus start), the two notes' phonemes **each keep their natural geometry and do not push/compress each other** — the later note's leading consonant may naturally reach into the gap and overlap the earlier note in the display, but nothing moves. This way fixed phonemes don't jump because a neighbor moves within the gap; if the user **wants it adjustable (leading consonant pushing the earlier note's vowel), drag the notes to be adjacent**. **How the synthesis side handles the gap is up to you (the engine)**: merged continuous inference (→ pushing) or producing chunks independently and overlap-mixing on the timeline (→ overlapping voicing) is your audio decision; the host only displays the ideal form (adjacent = pushing, gap = independent). If you drive audio with `Resolve` and want WYSIWYG, keep `FillEnd` on the same measure (stop at your own end in the gap); deviating makes audio and display diverge at the gap, which **is audible when driving audio, not "non-fatal"** ("non-fatal" holds only for display-only misalignment, see "Two uses" above).
- **Continuation and rest (the decision is yours)**: transparency (the previous note's phonemes flowing forward over it — melisma) is decided **solely by the continuation judgment**, not by "did you return phonemes": only a note you decide is a continuation is transparent; a non-continuation note you return no phonemes for merely leaves its own slot blank and does not affect its neighbors (see the three states of the map below). To make a note voice **silence / a breath**, output a still phoneme for it (e.g. `sp` / `AP`). **Which notes are continuations (legato / melisma passengers) is your decision**: implement `bool IsContinuation(IVoiceSynthesisNote note)` on the session — **it must be implemented, deliberately with no default body**: the decision and your synthesis behavior are a paired promise, and any default body would promise, on behalf of your synthesis, a semantics it might not implement (a `"-"`-fills-end default lies about engines that don't do melisma; an always-`false` default masks an engine that does melisma but forgot to implement this method), so silent inheritance is silent divergence, and the interface forces you to state it explicitly. **An engine that does no legato semantics honestly returns `=> false`** (every note is content). **The host adds none of its own criteria and consumes it as given** (display layout / editing gestures share the source) — your markers (`"-"`, `"+"`, "ー", dictionary-driven syllable layout) automatically get host UI support. Reference semantics (the decision corresponding to the editor's `"-"` entry convention, which the host also uses to display parts with no sound source; full code in the sample plugin `tests/plugins/V1.Voice`, a ten-line chain walk-back): the lyric is `"-"` ∧ walking back through the unbroken adjacency chain reaches a content note (adjacency = earlier end ≥ later start, strict comparison — boundaries share the source tick conversion, so adjacency is exact equality; a gap breaks the chain / a missing chain head → an orphan `false`) ∧ this note has no pinned phonemes (a pin is content, exiting the passenger role and becoming a legitimate chain head). Chains / adjacency / markers are all your semantic space (e.g. an engine policy like "a small gap counts as adjacent"), and the SDK deliberately provides no decision helper — the decision is bound to synthesis behavior, so you must fully own its semantics. Contract constraints: synchronous on the data thread, retain no note reference, deterministic observation (same current data → same answer), **must not depend on synthesis progress/products** (the decision must not change based on "whether it has been synthesized"). **Binding**: a note you decide is a continuation **must not** return phonemes in `SynthesizedPhonemes` — the whole-region voicing (including a trailing consonant glued to the end of the melisma) is all returned attributed to the **chain-head note**; a violation is not corrected or discarded by the host, and the display faithfully falls back to the content you returned (self-contained rendering + skeleton correction), so it's your bug. Pinned phonemes are **peers of** lyric/position — they are inputs to your decision, not host-enforced conditions: `Standard`'s default semantics choose "a pin is content, exiting the passenger role" (a pinned note is a legitimate chain head), and your custom decision may freely decide the status of a pin in the continuation decision — the host respects the return value as given, and a note decided to be a continuation displays transparent (**even with pins**, its semantics defined by you to the user). **The decision domain is live data**: the snapshot window may cut off the chain head, so when you need to carry the continuation identity into the worker, call your own decision on the live notes in the `SynthesizeNext` synchronous prefix and freeze it along with your own snapshot structure (an engine that chunks by note gaps and decides within the chunk is equivalent to live).

**Output (engine → host, returned at synthesis time): a phoneme map keyed by attribution note**

```csharp
IReadOnlyMap<string, SynthesizedSyllable> SynthesizedPhonemes { get; }   // key = the attribution note's Id
// SynthesizedSyllable: a sealed class ctor'd as new SynthesizedSyllable(leadingPhonemes, bodyPhonemes, bodyOffset),
// exposing { IReadOnlyList<SynthesizedPhoneme> LeadingPhonemes; IReadOnlyList<SynthesizedPhoneme> BodyPhonemes; double BodyOffset }
// (no merged Phonemes projection — the flat order is the host's business). A class rather than a struct so a TryGetValue
// miss yields null instead of a struct with two null lists.
// Each attribution note → its leading / body phoneme lists (reporting only Symbol / Duration / StretchWeight, no absolute position) + BodyOffset (junction = noteStart + BodyOffset; 0 ⇒ body start exactly on the head, supports cross-beat phonemes)
```

- **Keyed by attribution note** (rather than a flat timeline + an origin field): the phoneme descriptor reports no absolute position, an ownerless phoneme has no anchor to be positioned and can't fall into a note's invalidation chain, so **there is no "ownerless phoneme" contract** — `SynthesizedPhoneme` has no `Note` field, and attribution is entirely expressed by the map key. Boundary crossings like a consonant invading the previous note's tail arise naturally when the host derives positions by the duration model, without you declaring positions. (Breath etc. will later be carried by "the attribution note's leading / trailing phonemes" or a dedicated event channel.)
- **How to fill the map key**: the key is the attribution note's **`Id`** — a plain string carried by both the live note (`IVoiceSynthesisNote.Id`) and its snapshot (`VoiceSynthesisNoteSnapshot.Id`), so the product of `snapshot.Notes[i]` is attributed with `snapshot.Notes[i].Id` and the worker never needs a live reference at all (the id is a value: it is safe to hold and compare across threads). **Key presence is the semantics** (three states, and the host displays by them): **key absent** = no answer yet (the block is dirty / mid-synthesis) — the host leaves it blank **and leaves adjacent neighbors blank too** (a neighbor's layout boundary depends on this note's phoneme geometry; laying it out before that is known would make it jump when the data arrives); **key present, syllable with zero phonemes** = the answer is "this note has no phonemes" (already computed; the note voices nothing, or its voicing is not attributed to itself) — the host leaves this note blank but **neighbors display normally**; **key present with phonemes** = the normal case. The two empty states are not interchangeable: using "zero phonemes" to mean "not computed yet" makes the adjacent notes disappear along with it.
- **`StretchWeight` (stretch / compress weight)**: after the user locks phonemes, the host fixes the product to the pinned geometry (the `StretchWeight` in `note.LeadingPhonemes` / `note.BodyPhonemes` comes from this), and thereafter display / synthesis de-overlap allocates by the **scale ratio `len/d = r^w`**: **the vowel (w>0) absorbs stretch exponentially**, **the consonant (w=0) is rigid and doesn't move** (compressed proportionally to nominal length only when the space is too tight even for the consonants). A typical syllable is `[lead consonant w0, vowel w1, trailing consonant w0]`. **With no phonological knowledge, just fill the same positive value everywhere (e.g. `w = 1`)** — all phonemes scale proportionally, a safe default; distinguishing consonant=0/vowel=1 is an optimization done only with phonological knowledge, and to make a vowel stretch more drastically give a larger `w` (e.g. `w=2` for a diphthong's main vowel). All `w` = 0 (including unset, the struct default of all zeros) degenerates to scaling the whole thing proportionally by nominal length, with no division by zero.
- **The nucleus duration is a base ratio, cancelled out when single**: a nucleus's (`StretchWeight>0`) `Duration` is its original length — with **multiple nuclei** it sets the base ratio between them (each multiplied by `r^w`); with a **single nucleus** the original length is cancelled out and it degenerates to filling the nucleus space, so whatever you report is the same. A consonant's (w=0) `Duration` is its fixed length. Leading / body attribution is the list membership (structural, not derived), and `BodyOffset` places the junction (leading accumulates leftward from it, body rightward). Therefore the output **needs no positioning by you** — just honestly report each phoneme's duration + weight, its list, and the note's `BodyOffset`, and the host derives positions uniformly — zero jumping.
- **Preview is display-only, never fed back to you as a constraint**: the authoritative durations are returned by a full re-timing synthesis (with new weights), overriding the preview. You just honestly output the current durations + weights each synthesis.

### 5.8 Audio product and status

**Audio is delivered via the segment handle `IAudioSegment`** (not a flat pull) — because the downstream effect chain re-renders incrementally per segment, and the segment is the effect's invalidation/re-render unit.

```csharp
public interface IAudioSegment : IDisposable   // Dispose() = delete this segment (rebuilt on re-chunk / length or position change)
{
    void Write(int offset, ReadOnlySpan<float> samples);  // write in place within the segment [offset, offset+len); the span is borrow-semantics, reusable after return
    void Commit();                                        // mark this segment's audio as fixed — the [only gate] to the effect
    void Resize(long sampleOffset, int sampleCount);      // content-continuous extend/trim: identity preserved (downstream caches survive) — intersecting content stays aligned by absolute position, new regions are zeroed, and the segment drops to uncommitted until re-Commit()ed
}
```

- A segment is requested via `context.CreateAudioSegment(sampleOffset, sampleCount, sampleRate)`: `sampleOffset` = the global start sample position (**the plugin's native rate**, global 0s = sample 0); `sampleCount` = segment length (in samples); `sampleRate` = **that segment's native sample rate** (you pass it, the host interprets accordingly — equal to the project rate is read directly, unequal wraps a resample, centralized in one place in the host). **The sample rate travels with the segment and may differ per segment** (e.g. offering a synthesis-sample-rate dropdown).
- A segment's **start and length are fixed at creation** (the host allocates the buffer once, you write in place, and progressive synthesis doesn't accumulate re-copies); to change position/length → `Dispose()` the old segment and `CreateAudioSegment` a new one. Each re-render of a segment is "drop the old, build the new".
- **`Commit()` is the only gate to the effect**: `Write`s before Commit are only for progress/waveform display; only frozen data (Commit) enters the effect. So a synthesis burst does not drag expensive effects into frequent reruns.
- Writing/committing/releasing are **all on the data thread** (after the worker renders, write in the continuation marshaled back to the data thread).
- **Silent segments**: the host buffer is zero-initialized, so after `CreateAudioSegment` just `Commit()` directly, no `Write` needed.

**The status band `SynthesisStatusSegment`** (returned by `Status`, used by the host to color/progress/report errors):

```csharp
public readonly struct SynthesisStatusSegment
{
    public double StartTime { get; init; }  public double EndTime { get; init; }   // seconds
    public SynthesisSegmentStatus Status { get; init; }    // Pending / Synthesizing / Synthesized / Failed
    public string? Message { get; init; }                  // Failed = error message; Synthesizing = optional stage text (e.g. "computing phoneme durations"), shown by the host as-is
    public double Progress { get; init; }                  // [0,1] while Synthesizing, kept at 0 if not reported
}
```

- The status segments and audio segments are **decoupled**: the former is the UI status band, the latter is the effect invalidation unit; the two partitions may differ and the host does not assume alignment.
- **`StatusChanged` is the only refresh signal**: whenever the product (audio/pitch/synthesized parameters/phonemes) or the status has any update, fire it, and the host re-reads and re-draws on receipt. Outbound events may be fired from any thread and the host marshals — but your product fields must swap references on the data thread (swapping references = immutable publication).

**Synthesized parameter track data** goes through `SynthesizedParameters` (`IReadOnlyMap<string, SynthesizedParameter>`, keys aligned with `GetSynthesizedParameterConfigs`):

```csharp
public sealed class SynthesizedParameter { IReadOnlyList<IReadOnlyList<Point>> Segments { get; } }  // piecewise polyline, in-segment Point=(second,value), disconnected between segments
```

### 5.9 Invalidation and incremental re-synthesis

Correct incremental re-synthesis = "cheap dirty-marking + closing off the heavy work". Subscribe to the context at session construction, do **only cheap dirty-marking** in the handlers, and defer the heavy work (e.g. re-chunking) to `Committed`:

```csharp
// Wire up at construction (data thread)
mNotesSub = context.Notes.WhenAny(SubscribeNote, UnsubscribeNote);  // auto-covers member add/remove
context.Notes.ItemAdded   += _ => mNeedResegment = true;
context.Notes.ItemRemoved += _ => mNeedResegment = true;
context.PartProperties.Modified += MarkAllDirty;
context.Pitch.RangeModified.Subscribe(OnRangeModified);   // (startTime, endTime) seconds: mark only the intersecting blocks dirty
context.PitchDeviation.RangeModified.Subscribe(OnRangeModified);
if (context.Automations.TryGetValue("Growl", out var growl)) growl.RangeModified.Subscribe(OnRangeModified);   // ← can subscribe to your declared track at construction time
context.Committed.Subscribe(() => { if (mNeedResegment) Resegment(); });   // logical-edit closure: do the heavy work once
```

> **Subscribing to your declared automation tracks at construction time is reliable**: the declaration (`GetAutomationConfigs`) is on the engine, and the host has already filled the track set from it before creating the session, so in the session constructor `context.Automations` already contains your declared tracks (retrievable via `TryGetValue` / enumerable). Interval invalidation after drawing that track arrives via this callback → marks dirty → re-renders on the next scheduling tick. If you forget to subscribe, drawing the parameter won't trigger a re-render (the track data changed but no one marked it dirty).

- **The three minimal change facts**: ① a field changed (subscribe to `note.StartTime/EndTime/Pitch/Lyric/Phonemes/Properties`'s `Modified`, using `WillModify` to grab the old value and invalidate the old interval when needed); ② an interval changed (`ISynthesisAutomation.RangeModified` with a second range); ③ the collection changed (`Notes` add/remove, `WhenAny` auto-wires new members). Which segments these facts map to and to which pipeline tier the re-synthesis goes (the invalidation dependency graph) is **up to you** — the mechanism's granularity supports the finest strategy, and also allows the lazy "any notification → mark everything dirty" implementation.
- **`Committed` is the closure point**: it fires once after all notifications of each logical edit (one command, including a single edit) have been sent (a single edit is also re-fired, so you need not distinguish "in a batch or not"). A batch edit (transposing hundreds of notes) therefore **re-chunks only once**.
- **Tempo changes (and part shifts) have no separate signal and no incremental decomposition notification**: the host simply rebuilds the whole session — the old session `Dispose`s, and a new session is rebuilt with a new context, reading the new second values. You need no special handling; implement `Dispose` correctly (unsubscribe + release audio segments) and it is naturally correct; boundary `Modified` / track `RangeModified` only fire when a note / curve itself is edited.
- **Always unsubscribe in `Dispose`** — although the context is short-lived (dies with the session, so a leak is structurally impossible), unsubscribing is good practice and makes it easy to release the model/segment handles you hold. In `Dispose` you must also `Dispose` all audio segments.

> Overlapping-note (chord) chunking pitfall: when chunking by note gaps, judge the gap by "the **maximum** end within the group" and not "the previous note's end" — in a same-start chord the previous note may end earlier, and using it would wrongly cut out a long note that is still sounding. Likewise take the block end as `notes.Max(n => n.EndTime)`.

### 5.10 Native dependencies and model packaging

A voice engine often depends on a native runtime (ONNX Runtime, etc.), model weights, and a pronunciation dictionary (dict). Packaging rules:

- **Private dependencies ship with the package and are isolated from other plugins**: your third-party managed libraries and native `.dll`/`.so`/`.dylib` go into the **package folder** and are loaded into your package's dedicated ALC. Different plugins bundling different versions of the same library **will not conflict**. The SDK assemblies (`TuneLab.Foundation` / `TuneLab.SDK`) and the .NET runtime are shared by the host — **do not** pack them (see §3).
- **Locating in-package resources (model/dict/native libs)**: build absolute paths from your own assembly's location; **do not** use the working directory or `AppContext.BaseDirectory` (that is the host directory):

  ```csharp
  static readonly string PackageDir =
      System.IO.Path.GetDirectoryName(typeof(MyVoiceEngine).Assembly.Location)!;
  // then: Path.Combine(PackageDir, "models", voiceId, "acoustic.onnx"), etc.
  ```
- **Loading native libraries**: place the native `.dll` in the **same directory** as your managed `.dll` (the package root), and default probing can usually P/Invoke straight to it. If you use a NuGet package with a native backend like ONNX Runtime, just let its native libs output to the package root; for cross-platform, provide the corresponding native libs per target platform and filter with `platforms` in the manifest (e.g. a voicebank shipped for Windows only).
- **Don't stuff large model weights into the `.tlx`**: the `.tlx` is an install-and-load-immediately package, and stuffing a several-hundred-MB model into it makes install/load heavy. Two recommended forms:
  - **Resource-package separation**: the model is a standalone resource package (no code, `type` declares its purpose), discovered by the engine at runtime; or
  - **Let the user configure the model path via extension settings**: the engine implements `IExtensionSettings`, exposes a "model directory" setting with `PathPickerConfig.CreateFolder()` (a path box with a browse button — use `CreateFile()` plus `.AppendFileType(...)` when it is an executable or a single file), the user picks the path in "Settings → Extensions", and you receive it in `ApplySettings` and load from that path in `Init`/`CreateSession` (see §8). Secrets like API keys use `TextBoxConfig.Create().WithPassword()`, which the host masks and stores securely.
- **Load in `Init`, throw on failure**: put model/dictionary loading in `Init` (or lazier, on the first `CreateSession`). On load failure just throw an exception; the host catches at the call boundary, marks the plugin as failed to load, and reflects the reason in the sidebar, without crashing the host.

### 5.11 Interface responsibility quick reference

| Member | Thread | Responsibility |
|---|---|---|
| `IVoiceSynthesisEngine.VoiceSourceInfos` | any (synchronous read) | voicebank catalog; **must return immediately, never block** (cached during Init) |
| `IVoiceSynthesisEngine.VoiceSourceLayout` | any (synchronous read) | picker grouping tree (optional, DIM `[]`=flat); leaves reference ids, unlisted ids appended by host at top level |
| `IVoiceSynthesisEngine.Init/Destroy` | — | load/release resident state (models); throw on failure |
| `IVoiceSynthesisEngine.CreateSession` | data thread | build one session per part |
| `IVoiceSynthesisEngine.GetPartPropertyConfig`/`GetNotePropertyConfig` | data thread | property panel (pure function of voiceId + current values, may show/hide conditionally) |
| `IVoiceSynthesisEngine.GetPhonemePropertyConfigs` | data thread | per-phoneme property panel (required; takes `IVoiceSynthesisNotePropertyContext`, returns a schema map keyed by nucleus-relative slot (PhonemeSlots), empty map = no properties, multi-select merging belongs to the engine) |
| `IVoiceSynthesisEngine.GetAutomationConfigs` | data thread | editable automation track set (NaN ⇒ piecewise; avoid reserved names) |
| `IVoiceSynthesisEngine.GetSynthesizedParameterConfigs` | data thread | read-only synthesized parameter track declarations (always piecewise) |
| `IVoiceSynthesisSession.DefaultLyric` | data thread | default lyric for a new note (a session-level runtime value) |
| `GetNextPendingSynthesisRange` | data thread | peek the next dirty block boundary (no side effects, deterministic) |
| `SynthesizeNext` | synchronous prefix = data thread; then worker | pull snapshot → offload render → publish back on the data thread |
| `GetSnapshot` | **synchronous prefix only** | materialize an immutable snapshot (you pin the notes; automation/pitch are frozen in full, no window) |
| `CreateAudioSegment` / `IAudioSegment.Write/Commit/Resize` | data thread | request and write an audio segment; Commit is the gate to the effect; Resize extends/trims it keeping its identity |
| `SynthesizedPitch/Parameters/Phonemes`, `Status` | published on the data thread, readable cross-thread | products; publication = immutable |
| `StatusChanged` | fired from any thread, host marshals | the only refresh signal |
| `Dispose` | data thread | unsubscribe, release models and segment handles |

---

## 6. Writing an Effect Plugin

An effect transforms **already-synthesized whole-segment audio**. It targets **relatively slow offline models** (e.g. SVC voice conversion, neural timbre conversion), not real-time VST-style effects.

Implement `IEffectSynthesisEngine`. A **parameterless constructor** is required. The effect id goes in `manifest.json`'s `engine`, and the implementing class in `class` (the host verifies it implements `IEffectSynthesisEngine`, no longer using attributes). There is one engine per effect type; the host creates a **persistent thick processor** `IEffectSynthesisSession` per "effect instance × upstream audio segment" in the project to drive it. The processor holds its own segment's context `IEffectSynthesisContext`, **subscribes itself, and manages invalidation and reprocessing itself** — the engine-private invalidation graph (which parameter / which automation segment marks dirty and triggers which internal recomputes) lives inside the processor, which the host cannot replicate, hence a thick model.

Manifest entry: `{ "type": "effect", "engine": "MyEffect", "name": "My Effect", "class": "My.Ns.MyEffectEngine", "assembly": "MyEffect.dll" }` (`engine` is the immutable identity; `name` is an optional display name that can be translated via `localizations`; the host verifies `class` implements `IEffectSynthesisEngine`).

```csharp
using TuneLab.Foundation;
using TuneLab.SDK;

public class MyEffectEngine : IEffectSynthesisEngine   // engine id is declared in the manifest's "engine"
{
    // Property panel / automation tracks / synthesized parameter tracks: all pure functions of the current parameter values
    // (context.Effects — one read-only view per selected effect instance, each with Properties / Automations) — the host recomputes on parameter commit
    // and diffs to the UI, so controls/tracks may show/hide with parameters (conditional declaration). A static one ignores context and returns fixed values (as below).
    public ObjectConfig GetPropertyConfig(IEffectSynthesisPropertyContext context) => mPropertyConfig;
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetAutomationConfigs(IEffectSynthesisPropertyContext context) => mAutomationConfigs;

    // Synthesized-parameter synthesized parameter track declarations (read-only, independent of editable automation tracks): the read-only curves the processing produces (e.g. loudness) are exposed as first-class read-only tracks,
    // piecewise (DefaultValue=NaN), with their own Min/Max/Color (the display name rides on the PropertyKey). An engine with no synthesized parameters returns an empty map.
    public IReadOnlyOrderedMap<PropertyKey, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context) => mSynthesizedParameterConfigs;

    // Parameterless: the package directory is self-located via Assembly.Location (no host-passed path). On failure just throw; the host catches at the call boundary → passthrough degradation.
    public void Init() { /* ... load the model ... */ }
    public void Destroy() { /* release resources */ }

    // One persistent thick processor per "effect instance × one upstream audio segment"; the context is host-implemented, exposing this segment's input + parameters/automation + output ports + closure event.
    public IEffectSynthesisSession CreateSession(IEffectSynthesisContext context) => new MyEffectProcessor(context);

    readonly ObjectConfig mPropertyConfig = ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
    {
        { ("amount", "Amount"), SliderConfig.Linear(1.0, 0.0, 2.0) },
    });
    readonly OrderedMap<PropertyKey, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<PropertyKey, AutomationConfig> mSynthesizedParameterConfigs = new()
    {
        { ("loudness", "Loudness"), AutomationConfig.Create(0, 2).WithColor("#00B0FF") },
    };
}
```

Invalidation judgment belongs to the host: it conservatively schedules `Process` on scoped signals (this segment's input re-committed / this effect's parameters settled-changed / this effect's automation edits whose range intersects this segment). The processor carries **no reporting duty** -- `Process` has **level semantics**: "make the output consistent with the current input", not "apply this change"; the synchronous prefix reads the latest truth, and how many edits happened in between is invisible and irrelevant. The **synchronous prefix** (data thread) copies the needed range out via `Input.Read` into its own buffer + pre-samples parameter/automation values, and only then may offload to a worker; the product is written out via `context.CreateAudioSegment` and `Commit`ted. Since host scheduling is conservative, a cache-savvy engine compares against its own caches and returns early without re-committing when the output would not change (downstream is then skipped); **an engine with no internal incremental work just re-processes whenever called.** The granular events (`Input.RangeModified` / `Properties.Modified` / automation `RangeModified`) remain available as optional cache-refresh hints -- the simplest engine subscribes to nothing.

```csharp
class MyEffectProcessor : IEffectSynthesisSession
{
    public MyEffectProcessor(IEffectSynthesisContext context)
    {
        mContext = context;
        // Optional cache-refresh hints (scheduling is the host's job; the simplest engine subscribes to nothing):
        mContext.Input.RangeModified.Subscribe(OnInputRangeModified);   // upstream content-change ledger (absolute sample positions)
        mContext.Properties.Modified.Subscribe(OnDirty);      // this effect's parameters changed
    }

    // Status claim timeline (same vocabulary as the voice session; global seconds; the subject is this processor's OWN
    // product -- not bounded by the input geometry). A Synthesizing segment carries Progress; a Synthesized segment is a
    // "claimed done" (shown as a non-final soft color -- final green only ever comes from actual chain-tail audio).
    // Return an empty list to let the host render a default from scheduling facts. StatusChanged may fire from any thread
    // (report in place from the worker); the host marshals before pulling Status.
    public IReadOnlyList<SynthesisStatusSegment> Status => mStatus;
    public IActionEvent StatusChanged => mStatusChanged;
    readonly ActionEvent mStatusChanged = new();

    // This segment's synthesized parameter curves (keys aligned with GetSynthesizedParameterConfigs): published on the data thread, host read-only, re-read along with the product on wrap-up. Return an empty map if there are none.
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mSynthesizedParameters;

    public Task Process(CancellationToken cancellation = default)
    {
        // —— Synchronous prefix (data thread): grab the input PCM reference + pre-sample parameter/automation values ——
        var input = mContext.Input;
        int rate = input.SampleRate;
        long offset = input.SampleOffset;
        int count = input.SampleCount;
        var src = new float[count];
        input.Read(0, src);                            // copy-out into your own buffer (range reads are fine too)
        double amount = mContext.Properties.GetValue("amount", PropertyValue.Create(1.0)).ToDouble(out var a) ? a : 1.0;

        // Automation (optional): the live ISynthesisAutomation only reports RangeModified — values come from a frozen
        // snapshot, so pull one in the prefix and sample its evaluator. Query axis = global seconds (same time system as audio).
        double[]? env = null;
        if (count > 0)
        {
            double segStart = rate > 0 ? (double)offset / rate : 0;
            double segEnd = rate > 0 ? (double)(offset + count) / rate : 0;
            var snapshot = mContext.GetSnapshot(segStart, segEnd);   // EffectSynthesisSnapshot { Properties; Automations }
            if (snapshot.Automations.TryGetValue("intensity", out var automation))
            {
                var times = new double[count];
                for (int i = 0; i < count; i++) times[i] = segStart + (double)i / rate;
                env = automation.Evaluator.Evaluate(times);   // Span overload available to avoid the array allocation
            }
        }

        // —— After this you may offload to a worker (only reading the immutable values materialized above, never touching host live data) ——
        var dst = DoProcess(src, amount, env);

        // Output: registry semantics (same as voice) — segmentation of the product is free (1-to-N is legal, e.g. a
        // silence splitter); each segment lives independently (Write/Commit/Dispose; to change geometry, Dispose then recreate).
        var outSegment = mContext.CreateAudioSegment(offset, dst.Length, rate);
        outSegment.Write(0, dst);
        outSegment.Commit();

        // Synthesized parameters: swap references on the data thread in sync with the output (in this example Process is fully synchronous, so swap directly).
        mSynthesizedParameters = BuildLoudness(dst, rate, offset);
        return Task.CompletedTask;                      // errors throw (host catches → passthrough); don't swallow here
    }

    void OnDirty() => mDirty = true;   // consumed inside Process to decide what to recompute (cache refinement only)
    void OnInputRangeModified(long start, int count) => mDirty = true;   // narrow the recompute window from the ledger if you keep caches

    public void Dispose()
    {
        mContext.Input.RangeModified.Unsubscribe(OnInputRangeModified);
        mContext.Properties.Modified.Unsubscribe(OnDirty);
        /* release this segment's resident state, output segment handles */
    }

    readonly IEffectSynthesisContext mContext;
    bool mDirty;
    IReadOnlyList<SynthesisStatusSegment> mStatus = [];   // publish by swapping the reference (atomic)
    IReadOnlyMap<string, SynthesizedParameter> mSynthesizedParameters = new Map<string, SynthesizedParameter>();
}
```

Key points:

- **Thick session, host-owned invalidation**: `CreateSession(context)` builds a **session** for "this effect × this upstream segment" (the same family of persistent stateful entity as a voice session -- holds live views, keeps caches across `Process` calls, publishes claims and synthesized parameters; the scope difference is expressed by the context binding); the host `Dispose`s it on segment destruction / effect deletion / re-segmentation / sample-rate change. The host schedules conservatively on scoped signals (it performs the automation-range/segment intersection generically — an edit in another segment's interval never wakes this node); parameter-dependency and value-level dedup are the engine's optional early-out inside `Process` (return without re-committing → downstream skipped).
- **Parameter and automation values come from a frozen snapshot**: `context.GetSnapshot(startTime, endTime)` returns an `EffectSynthesisSnapshot` (`Properties` value copy + `Automations` as frozen `SynthesisAutomationSnapshot`s) — callable **only in the synchronous prefix**, and the only cross-thread-safe way to read values after offload. Audio is not in it: copy that out yourself with `Input.Read`.
- **The input is an indivisible whole segment**: `context.Input` (`IEffectSynthesisAudio`) exposes `SampleOffset/Count/Rate` + `Read(offset, span)` (copy-out; the host storage layout is an implementation detail). There is no "committed" pulse -- being called into `Process` means the input is ready (scheduling is the host's job). `Input.RangeModified(start, count)` is the content-change **ledger** (optional; `start` is an **absolute sample position** -- content is pinned to the absolute axis, so ranges accumulated before an upstream `Resize` need no rebasing): cache-savvy engines accumulate ranges, narrow the recompute window from the ledger (`Read`/recompute/write back only the changed region -- O(changed) work; see the Slow Gain reference implementation for the full pattern), and clear the ledger only after a successful commit of their own output (cancellation refunds it). The ledger is **complete and faithful to trims**: an upstream `Resize` reports its geometric symmetric difference (a trimmed-away region went "from something to nothing" on the absolute axis, and it was context feeding the neighboring output), so ranges may lie outside the current extent; **the recompute scope is the session's own decision** -- after collecting the ledger, expand by your own context margin (so new content joins up with old) and intersect with the extent; a pointwise engine decides zero (just `Resize` its output to follow and commit empty). Only a genuinely change-free commit is silent; whole-segment reports appear only on forced invalidation (first snapshot / project sample-rate change).
- **Output via handles, registry semantics**: `context.CreateAudioSegment(offset, count, rate)` — segmentation of the product is free (**1-to-N is legal**: e.g. a silence **splitter** that re-establishes segment granularity so every downstream effect gets per-segment incrementality and parallelism for free); each output segment lives independently and each committed one feeds a downstream node. The input side stays single-segment (the consumption unit is the host's invalidation/scheduling/identity granularity). The only hard rule: **do not redistribute the time axis** (automation/synthesized parameters and the part display share the global-seconds axis); slight geometry differences (frame padding, added tails) are fine. The sample rate travels with the segment (when it differs from the project rate the host wraps a resample). **Geometry changes carry two distinct intents**: a semantic overhaul goes through `Dispose`+`Create` (downstream identity rebuilds); a content-continuous extension/trim goes through **`Resize(offset, count)`** (identity preserved, downstream caches survive -- intersecting content is kept aligned by absolute position, new regions are zeroed, the segment drops to uncommitted and is re-`Commit`ted after the new regions are written).
- **Status claims (optional)**: publish a status timeline via `Status` (immutable list swap) and fire `StatusChanged` (any thread) -- a Synthesizing segment with `Progress` renders as a vertical fill on the strip; Synthesized segments are "claimed done" (soft, non-final). An engine that reports nothing gets a host default derived from scheduling facts.
- **Synthesized parameter tracks (optional)**: the read-only curves the engine produces are declared via `GetSynthesizedParameterConfigs` + carried per-segment via `IEffectSynthesisSession.SynthesizedParameters` (the host stitches the segments of the same effect by key). Read-only, non-editable, not in the data layer, not serialized; isomorphic to voice synthesized parameters, shown/hidden by source in the parameter-area title bar.
- **Conditional declaration**: `GetPropertyConfig` / `GetAutomationConfigs` / `GetSynthesizedParameterConfigs` are pure functions of the current parameter values — `IEffectSynthesisPropertyContext` is just `IReadOnlyList<IEffectSynthesisView> Effects` (a multi-select shell: one view per selected instance, each carrying `Properties` and the evaluators of its drawn `Automations`; merge tri-state values yourself) — (same input → same output, no side effects, lightweight), recomputed on parameter commit — so controls/tracks may show/hide with parameters. After a track disappears from the declaration the host **keeps its already-drawn curve** (hidden, not deleted), restored as-is when the parameter rolls back.
- **Effect chain**: multiple effects may hang on one MidiPart, **serial** in declaration order — the previous output is the next input; at the chain tail the segments are mixed by absolute time. Chain order, bypass, and add/remove are managed by the user in the property panel.
- **Graceful degradation / cancellation on failure**: when an exception is thrown the host treats that segment as passthrough, without interrupting playback; cancellation is requested via `cancellation` and returns normally (**do not** throw `OperationCanceledException`), and the scheduling slot is released only when the `await` actually returns.
- **Thread discipline**: the `context` (`Input` / `Properties` / automation) may be read only in the **synchronous prefix** (data thread) of `Process`; after offload read only the materialized immutable values; `SynthesizedParameters` and the output segment must be published on the data thread.

The related interfaces are all in `TuneLab.SDK`: `IEffectSynthesisEngine` / `IEffectSynthesisSession` / `IEffectSynthesisContext` / `IEffectSynthesisAudio` / `IAudioSegment` / `IEffectSynthesisPropertyContext` / `ISynthesisAutomation`.

---

## 7. Writing an Instrument Plugin

An instrument is a **polyphonic sound source** (synth / sampler / chord source). It is **mechanically isomorphic to voice** (engine lifecycle, peek/commit scheduling, isolated snapshot, audio-segment delivery, effect chain, extension settings are all the same), with the interface family paralleled by the `IInstrument*` prefix, not inheriting from the voice family. **It differs substantially from voice in only three places**:

- **Notes go to full end, no de-overlap**: `IInstrumentSynthesisNote.EndTime` / `InstrumentSynthesisNoteSnapshot.EndTime` is the note's full end (`Pos+Dur`), and the host does **not** clamp it to the next note's start. `Notes` passes through the original overlappable notes (chords / polyphony), and the engine superimposes voicing itself (the reference implementation adds one waveform segment per note's pitch and mixes by sum).
- **No lyrics / no phonemes**: `IInstrumentSynthesisNote` has no `Lyric`, and no phoneme surface at all (no `LeadingPhonemes` / `BodyPhonemes` / `BodyOffset`); the session has no `DefaultLyric` and produces no `SynthesizedPhonemes`.
- **No pitch curve, product is audio only**: `IInstrumentSynthesisContext` has no `Pitch` / `PitchDeviation` (v1 voices purely by the note's integer `Pitch`); the session produces no `SynthesizedPitch`. It may still declare automation tracks and `SynthesizedParameters` synthesized parameters (none if the engine declares none).

Manifest entry: `{ "type": "instrument", "engine": "MyInstrument", "name": "My Instrument", "class": "My.Ns.MyInstrumentEngine", "assembly": "MyInstrument.dll" }`.

The sound-source catalog is the same shape as voice: `IInstrumentSynthesisEngine.InstrumentSourceInfos` (keyed by id). **One plugin, one instrument** = a single entry; a **container form** (e.g. Kontakt: one engine hosting multiple external resource-package instruments) scans the installed resource packages in `Init()` and fills multiple entries, with `InstrumentId` selecting the specific instrument. When there are many, implement `InstrumentSourceLayout` (isomorphic to voice's `VoiceSourceLayout`) to fold the picker into nested submenus; leave it unimplemented for a flat list.

Declarations that depend on derived state work as in voice (§5.2): `IInstrumentSynthesisContext.InvalidatePart` / `InvalidateNotes`, matched through `IInstrumentSynthesisNote.Id` / `IInstrumentSynthesisNoteView.Id` and `PartId`; instrument has no phonemes, so the cascade has no phoneme step.

> For the full interface contract and design rationale see [instrument-sdk-design.md](instrument-sdk-design.md); for a minimal reference implementation (one engine hosting sine/square timbres, polyphonic additive synthesis) see `tests/plugins/V1.Instrument`.

The related interfaces are all in `TuneLab.SDK`: `IInstrumentSynthesisEngine` / `IInstrumentSynthesisSession` / `IInstrumentSynthesisContext` / `IInstrumentSynthesisNote` / `InstrumentSynthesisSnapshot` / `InstrumentSynthesisNoteSnapshot` / `InstrumentSourceInfo` / `IInstrumentSynthesisPartPropertyContext` / `IInstrumentSynthesisNotePropertyContext` (the leaf types like audio / automation / status / synthesized parameters are shared with voice).

---

## 8. Extension Settings (IExtensionSettings)

Let your extension (an extension = one capability implementation such as a voice/effect) declare a set of settings that are **persisted by the host and shared across projects** — typically an **API key, model path, device selection**. The host renders a panel in the "Settings" window, stores it per extension, and feeds it back at runtime.

> **Difference from the property panel**: voice's `GetPartPropertyConfig`/`GetNotePropertyConfig` and effect's `GetPropertyConfig` declare **instance/segment-level** properties serialized with the project (one per part/note/effect instance, stored in the `.tlp`). The settings in this section are the **extension's own** configuration, independent of any specific project, shared across projects, and stored separately. Both use the same control-config vocabulary (`ObjectConfig`), but their lifecycle and storage location are entirely different.
>
> The granularity is **per extension** (one per voice/effect capability), not per install package (an ExtensionPackage may contain multiple extensions, each with independent settings).

### 8.1 How to plug in

Settings are **opt-in**: just have your capability implementation class **additionally implement** `IExtensionSettings`; an extension with no settings can ignore it. The host probes each registered capability with `x is IExtensionSettings` and shows a settings panel only if it is implemented.

```csharp
public sealed class MyVoiceEngine : IVoiceSynthesisEngine, IExtensionSettings
{
    // —— Declare the schema (reusing the same control-config vocabulary as the property panel) ——
    // It is a pure function of context's current values (the host recomputes and diffs to controls after a value change); and it [must be callable before Init]
    // ("fill the model path first, then Init can load the model" — the schema must not depend on post-Init state).
    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
    {
        var props = new OrderedMap<PropertyKey, IControllerConfig>();
        props.Add(("model_path", "Model Path"), PathPickerConfig.CreateFolder(""));   // path box + browse button (CreateFile for a single file)
        props.Add(("api_key", "API Key"), TextBoxConfig.Create().WithPassword()); // secret: masked display + encrypted storage
        props.Add(("use_gpu", "Use GPU"), CheckBoxConfig.Create(false));
        // Dynamic/conditional items: decide show/hide based on already-filled values (e.g. expose the device field only when GPU is checked).
        if (context.Settings.GetBoolean("use_gpu", false))
            props.Add(("gpu_device", "GPU Device"), TextBoxConfig.Create(""));
        return ObjectConfig.Create(props);
    }

    // —— Receive the persisted values ——
    // The host feeds it once [after loading completes] (before any Init / session), and again after the user [saves] in the settings window. Store and use it yourself.
    public void ApplySettings(PropertyObject settings)
    {
        mModelPath = settings.GetString("model_path", "");
        mApiKey    = settings.GetString("api_key", "");
        mUseGpu    = settings.GetBoolean("use_gpu", false);
        // Then use these values in Init / CreateSession.
    }

    // The rest of IVoiceSynthesisEngine's members……
}
```

### 8.2 Key points

- **Secret fields**: mark them with `TextBoxConfig.Create().WithPassword()`. The host masks the display accordingly and stores securely per platform: Windows uses DPAPI to store the ciphertext in place in the config file (decryptable only by the original user on the original machine); macOS stores it in the Keychain, leaving only an empty string in the config file. **When no secure storage is available, it does not save that secret field (never in plaintext) and warns**. Officially supported on Windows / macOS.
- **The schema must be reachable before Init**: `GetSettingsConfig` must not depend on state that only exists after `Init` — the user must first fill the settings panel (e.g. the model path) before you `Init`. Write it as a pure function (same input → same output, no side effects, lightweight).
- **Dynamic/conditional items**: `GetSettingsConfig(context)` is a pure function of `context.Settings` (the currently-filled values); after the user changes a value the host recomputes by the current values and diffs to the control tree, so it can show/hide fields based on the filled values (e.g. a field that appears only when some switch is on).
- **Feedback timing**: the host feeds it once after loading all extensions at startup (before `Init`), and again after the user saves the settings. Whether a setting change affects **already-running** sessions/processors (whether they need rebuilding) is decided and handled by you.
- **Localization**: the settings items' `DisplayText` is translated by you (the same paradigm as the property panel, producing text by `TuneLabContext.Global.Language`); the host does no lookup.
- **No manifest declaration needed**: the settings schema goes purely through code (`GetSettingsConfig`); `manifest.json` is not involved.

### 8.3 Where the user changes them

The "Settings" window (entered from the top menu) → "Extensions" tab: one "display name + settings panel" section per extension that declares settings. Edits are stored and fed back uniformly when **the window is closed / the tab is switched away**.

> The agent model engine has its own sidebar settings entry, not in the "Extensions" tab.

The related interfaces are in `TuneLab.SDK`: `IExtensionSettings` / `IExtensionSettingsContext` (+ the control configs `ObjectConfig` / `TextBoxConfig` / `PathPickerConfig` / `CheckBoxConfig` / `ComboBoxConfig` / `SliderConfig`).

### 8.4 Format extensions: the unit is the entry, not the suffix

A format entry plugs in the same way — implement `IExtensionSettings` alongside `IImportFormat` / `IExportFormat`. Two things differ from an engine, because a format is registered as a **factory** (the host creates a fresh instance for every import and export) rather than as one long-lived instance:

- **One entry, one set of settings.** An entry may claim several suffix aliases (`"suffixes": ["mid", "midi"]`), but the settings belong to the implementation, so the Extensions page shows **one** row for the entry and stores **one** set of values — never one per suffix. Direction subsets do not split it either: `export-suffixes` narrows what gets registered, not who the implementation is.
- **Two implementations, two sets of settings.** An importer class and an exporter class are two entries (§4.2), so they get two independent buckets, each with its own schema. That is the point of the split: with two classes under one entry there would be one bucket and one of the two schemas would have nowhere to live.
- **`ApplySettings` runs on every import/export**, right after the instance is created — that is the only moment a factory-made instance can be handed its settings. Keep it cheap, and do not treat it as a "settings changed" notification: it fires once per created instance, not once per edit.
- **The host creates one extra probe instance** to ask for your schema, since there is no long-lived instance to ask. It never imports or exports anything. Keep your parameterless constructor light (no model loading, no file I/O) — that is good practice anyway, since the constructor also runs for every import and export.
- **Changing the suffixes starts a fresh set of values.** The bucket is keyed by the entry's directions and its suffixes — `format:mid|midi` for a two-way entry, `format-export:midi` for an export-only one. Adding, removing or reordering a suffix therefore produces a different bucket, and the host does **not** migrate the old values: the entry starts at its defaults and the user re-enters anything they had set, secrets included. Settle the suffix lists before shipping and treat a later change as a user-visible reset.

```csharp
public sealed class MyFormat : IImportFormat, IExportFormat, IExtensionSettings
{
    public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context) { /* as in §8.1 */ }

    // Called right after the host creates this instance, before Deserialize/Serialize is invoked on it.
    public void ApplySettings(PropertyObject settings) => mEncoding = settings.GetString("encoding", "utf-8");

    public ProjectInfo Deserialize(Stream stream) { /* mEncoding is already filled in here */ }
    public void Serialize(Stream output, ProjectInfo info) { /* and here */ }
}
```

---

## 9. Buttons (ButtonConfig)

### 9.1 Declaration and execution

A button can go anywhere a panel is declared: extension settings, part / note / phoneme property panels, effect parameter panels. It is just another member of the control-config vocabulary, and clicking it calls the function you pass in:

```csharp
public ObjectConfig GetSettingsConfig(IExtensionSettingsContext context)
{
    var props = new OrderedMap<PropertyKey, IControllerConfig>();
    // capture values only: read what you need at declaration time, never capture the context
    string apiKey = context.Settings.GetString("api_key", "");
    props.Add(("cache", "Cache"), ButtonConfig.Create($"Clear ({mCacheMB} MB)", ClearCacheAsync));
    props.Add(("test", "Connection"), ButtonConfig.Create("Test", click => TestAsync(click, apiKey)));
    return ObjectConfig.Create(props);
}
```

- **A button holds no value**: it reads and writes no property; reset-to-default, presets and settings persistence skip it, and CLI / agent cannot set it. **A button does not change project data** — it only touches your own state (cache, connection, model…).
- **Capture values only** (§5.2, "Closures in declarations and evolving a field's shape"): a click may come minutes later, long after the declaration context and its views became invalid. On the settings page `context.Settings` holds the **values currently in the window, including unsaved ones**, so a "test connection" button can use the key the user just typed.
- **Thread**: `OnClick` is called on the main thread — the same thread as `Init`, `CreateSession`, declaration functions and session events — and after an `await` it resumes on the main thread by default (unless you write `ConfigureAwait(false)`). Resources you only ever touch on the main thread need no locking inside a button.
- **`await` is an interleaving point**: while you are awaiting, the main thread does other work, and the host may call your other methods (synthesis scheduling, declaration functions, session events). The concern is not multithreading but **whether your state is consistent across an `await`** — e.g. if clearing a cache awaits halfway, synthesis scheduling may read the cache right then: either finish the clearing in one step without awaiting, or keep the state usable before and after each `await`. Concurrency with background threads you start yourself (such as synthesis workers) remains your responsibility.
- **Do not block the main thread**: the host cannot stop you, and the consequences are yours (a frozen UI, the OS reporting "not responding"). Instant operations can simply run synchronously; long ones should `await` or go inside `RunWithProgress` (§9.2). Blocking on a dialog result (`.Result` / `.Wait()`) deadlocks.
- **The host stays usable while it runs**: running a button is not modal — only that button is greyed out and cannot be re-entered (even after the panel is rebuilt); only a dialog, while shown, blocks the window holding the button. The user may edit or even delete project content meanwhile, so PartIds / note ids captured by the closure may no longer exist — tolerate "not found" when looking up your own data by id.
- **Exceptions** are logged and shown to the user, attributed to your plugin.
- **The panel refreshes after the click**: when it finishes the host calls the panel's declaration function again, so the button text and nearby options reflect the new state. If other notes' / parts' declarations are affected, call `InvalidatePart` / `InvalidateNotes` (§5.2).
- **Placement**: only as a member of an `ObjectConfig` (at any depth, including objects inside list elements). Not supported directly as an `ArrayConfig` / `ListConfig` element or an `ExtensibleObjectConfig` addable key — every slot there is a value.
- **Two forms, both one row**: with a title (the key's display name, or its id when no display name is given) the button sits on the right of the title; give the key an **empty** display name — `("run", "")` — and the title is dropped and the button spans the whole row, for actions whose button text already says it all. Buttons have no layout properties beyond that and are not collapsible (they hold no value).

### 9.2 The dialog service during a click (`click.Dialogs`)

**Dialogs can only be shown while a button click is running**, and the only entry point is the callback argument `click.Dialogs`:

```csharp
static async Task ClearCacheAsync(IButtonClickContext click)
{
    if (!await click.Dialogs.Confirm("Clear cache", "Delete all cached files?")) return;
    await click.Dialogs.RunWithProgress("Clearing…", async (progress, cancel) =>
    {
        for (int i = 1; i <= n; i++) { cancel.ThrowIfCancellationRequested(); /* … */ progress.Report((double)i / n, $"File {i} / {n}"); }
    });
    await click.Dialogs.ShowMessage("Clear cache", "Done.");
}

// ask the user for a few parameters after the click: complete values (untouched fields = defaults), null when cancelled
var inputs = await click.Dialogs.PromptInputs("Export preset", ObjectConfig.Create(new OrderedMap<PropertyKey, IControllerConfig>
{
    { ("name", "Preset name"), TextBoxConfig.Create(lastName) },     // initial values go into the defaults
    { ("quality", "Quality"), SliderConfig.Integer(5, 0, 10) },
}));
```

- The host draws them in its own style: consistent theme, OK / Cancel text translated by the host, title and body translated by you. Dialogs are modal over **the window holding the button** and name the plugin they came from.
- **Lifetime**: from the start of the callback until the Task it returns completes. Keeping `click.Dialogs` and using it afterwards throws `InvalidOperationException`.
- Callable from any thread. **Never block synchronously** on the returned Task (`.Result` / `.Wait()`) — dialogs need the UI thread, and blocking it deadlocks.
- `RunWithProgress`: the progress window starts at 0%; in `Report(fraction, status)` a `null` fraction shows indeterminate progress (a moving bar), and `status` is one line of text under the bar (translated by you) — omit it or pass `null` to keep the previous text (pass `""` to clear it); `fraction` has no "keep as is" value, so pass the current progress when only the text changes; `progress.SetCancellable(false)` greys out the cancel button during a critical section that must not be interrupted (replacing files, writing an index…) and `SetCancellable(true)` restores it (cancellable by default; the token is still cancelled when the app exits, so a critical section must still survive the process being terminated); `Report` may be called from any thread at any rate (the host coalesces per frame); the progress window always has a cancel button, and the token is cancelled when the user cancels or the app exits; exceptions thrown by `work` come back to you unchanged.
- `PromptInputs`: renders your config with the same controls as property panels (the same form window as script tools' inputs); form data is isolated from the project and not undoable.

### 9.3 Your own settings UI

For a UI with your own layout and your own data (bypassing the host's config and property-data machinery), **make it a separate process: write your own exe and let a button just launch it**. That UI has nothing to do with the host — the host provides no owner window and no modality; the exe's framework, its runtime and how it exchanges data with the plugin (files, command line, pipes…) are all up to you.

```csharp
props.Add(("editor", "Editor"), ButtonConfig.Create("Open...", async _ =>
{
    var start = new ProcessStartInfo(Path.Combine(packageDir, "editor", "MyEditor.exe")) { UseShellExecute = false };
    start.ArgumentList.Add("--data"); start.ArgumentList.Add(dataPath);
    using var process = Process.Start(start)!;
    await process.WaitForExitAsync();   // wait for it: the button stays greyed and the panel refreshes after it closes (or return right away)
}));
```

**UI frameworks inside the plugin process are not part of the contract**: the host's Avalonia is not available to plugins; WinForms / WPF belong to .NET's desktop runtime, which the host process does not load, so referencing them fails at run time with a missing assembly. If you insist on another in-process UI framework, keeping it from conflicting with the host is on you.

Reference implementations: `tests/plugins/V1.CustomWindow` + `tests/plugins/V1.CustomWindow.Editor` (a button launching a WinForms editor exe, waiting for it or not), `tests/plugins/V1.Settings` (settings page: clear cache / test connection / export preset (PromptInputs) / throwing / buttons inside a nested object and list elements), `tests/plugins/V1.Voice` (buttons on part / note / phoneme panels), `tests/plugins/V1.Effect` (an effect-panel button with progress).

## 10. Canvas (CanvasConfig)

> **Experimental**: the canvas belongs to SDK `1.2`, which hasn't shipped with a released TuneLab yet, and its API may still change before it does. Feel free to experiment, but hold off on releasing plugins that depend on it; when you use the canvas, declare `"sdk-version": "1.2"` in your manifest.

For edits the built-in controls can't express (say, a stretch curve inside a phoneme), declare a **canvas**: you draw it, you handle the mouse, and when the edit is done you commit the result to the field.

```csharp
props.Add(("stretch", "Stretch Curve"), CanvasConfig.Create(PropertyObject.Empty, 96, host => new StretchCurveCanvas(host))   // value = { "points": [...] }, {} = unset
    .WithSummary(value => IsLinear(value) ? "Linear" : "Custom"));

sealed class StretchCurveCanvas(ICanvasHost host) : ICanvasControl
{
    public void Render(ICanvasDrawing d)
    {
        var curve = Read(host.Value);                       // always validate what you read, see §10.1
        d.DrawPolyline(ToPoints(curve), Color.SkyBlue, 2);
    }
    public void OnMouseDown(CanvasMouseButtonEvent e)
    {
        mDragging = HitTest(e.Position);
        if (mDragging < 0) return;
        mEdit = host.EditScope();                            // declare the edit: the panel won't refresh or replace us mid-drag
    }
    public void OnMouseMove(CanvasMouseEvent e)
    {
        if (mDragging < 0) return;
        host.SetValue(Moved(host.Value, mDragging, e.Position));   // update the draft; not in the project yet
    }
    public void OnMouseUp(CanvasMouseButtonEvent e)
    {
        if (mDragging < 0) return;
        mDragging = -1;
        host.CommitValue();                                  // only a drag left a draft: it becomes one undo step; a mere click does nothing
        mEdit?.Dispose();                                    // end the edit
        mEdit = null;
    }
    int mDragging = -1;
    IDisposable? mEdit;
}
```

### 10.1 It is a value field

- **You define the value's shape** (any `PropertyValue`, objects and arrays included); the host doesn't interpret it. Reset to default, presets, undo and multi-selection all work as usual — so **always validate what you read**: undo, presets and paste can bring in a value of any shape; treat whatever doesn't fit as the default.
- **Default value**: it is the "unset" state and also fixes the field's type — a stored value of a different type reads as the default, so **keep the value's type fixed** (its shape may vary). The default can't be `PropertyValue.Null` / `PropertyValue.Multiple`; `Create` throws an argument error.
- **Prefer an object at the top level** (default `{}`, like `{ "points": [...] }` above): adding a feature later is just one more member, old values are still objects and keep reading fine, and missing members fall back to your defaults. With an array or a scalar at the top, extending means changing the shape (which needs a new key — see §5.2, "Closures in declarations and evolving a field's shape"). For something that will only ever be a single number, a scalar is perfectly fine.
- **Multi-selection**: when all targets hold the same value you get that value; if any differ, the **whole value** is the multiple-value marker (`IsMultiple()`) — the host doesn't merge element by element, since only you know the shape. How to draw it is up to you. A commit writes the same value to every target.
- **Collapsed summary**: `WithSummary(format)` — when the field is collapsed the host calls it with the current value and shows the result on the title row; when the whole value differs across targets the host shows the multiple-value marker without calling it. Without it the host shows "Default / Modified".
- **CLI / agent** can read and write the value as is (any JSON shape). The host doesn't know the shape, so it doesn't validate it — values the agent writes go through your read-time validation like any other. The host tells the agent to read your introduction first and, failing that, to infer the shape from the current or default value, so **describe this field's value shape and meaning in your introduction**: what each member is, its range, and how the shape varies with state (say, the element count depends on another setting). The declaration can't carry this; only your description can be exact.
- **Placement**: only as a member of an `ObjectConfig` (same as buttons, see §9.1).

### 10.2 Editing, drafts and commits

Two things are kept apart: the **editing state** (is the canvas being edited) and the **value** (drafts and commits).

- **You declare the editing state** — only you know whether an edit is going on; the host doesn't guess. Call `BeginEdit` / `EndEdit` in pairs; in between, the panel counts as still in use: the property panels in that window don't refresh (declaration invalidations from your plugin and changes made elsewhere wait until the edit ends) and your instance won't be replaced. It counts and nests: several operations may overlap (dragging a point while holding an arrow key), each with its own Begin / End, and the canvas unlocks when all have ended; an extra `EndEdit` does nothing and the host logs an error. For an edit spanning events the scope form is handier: `mEdit = host.EditScope()`, then `mEdit.Dispose()` when done (a second `Dispose` is harmless; see above). **Forget to end it and the panels in that window never refresh.**
- **`SetValue`** writes the draft. The host keeps it for you — outside the project, invisible elsewhere — and `host.Value` returns it from then on, so you keep drawing from `host.Value`. **Write only when the value really changed, and only real values**: when a multi-selection differs `host.Value` is the multiple-value marker (`IsMultiple()`), which isn't a value — `SetValue` returns `false` for it and writes nothing (the host logs an error); to hold the canvas without changing the value, use `BeginEdit`. A draft also keeps the panel in use (alongside `BeginEdit`, neither replacing the other), so operations without a "press" (wheel, keyboard) are held just by writing their draft. A draft may stay as long as you like, say until the user clicks a "confirm" you drew on the canvas.
- **`CommitValue`** writes the draft into the project as one undo step; **`DiscardValue`** drops the draft and goes back to the project value (a "cancel"). Both only deal with the value — they don't end an edit begun with `BeginEdit` — and without a draft they do nothing. Cancelling a drag = `DiscardValue`, then end the edit: two separate things.
- **Committing and ending the edit are only ever up to you**; the host never commits or ends the edit on your behalf. When the instance is invalidated (the field's value was changed from outside — undo, the same data edited elsewhere; the panel was torn down — switching parts, say; the canvas left the screen — another tab, the window closed) the draft and the editing state go with it, and later calls have no effect.
- Continuous input with no "release" (the wheel, say) can **defer the commit**: `SetValue` on every notch, `await Task.Delay(…)` in the callback until the input pauses, then `CommitValue()` (after the `await` you are back on the main thread and can use `host` directly) — one continuous scroll becomes one undo step.

### 10.3 Control instance lifetime

- **The factory is not called at declaration time**: the host calls it when the canvas is about to show (a collapsed field builds no instance).
- **An instance lives for one declaration**: every time the panel updates to a new declaration — after your commit, after an outside change, on a selection change — the host rebuilds the control with the new config's factory. The old instance is discarded and its `host` stops working (`SetValue` returns `false`). So **keep the factory cheap** (no IO, no heavy computation); the control object only carries event and drawing logic, and heavy resources are cached at plugin level.
- **Needing context** (the owning part / note, values of sibling fields, data you computed such as a waveform): read the values at declaration time and let the factory capture them; when the data changes, call `InvalidatePart` / `InvalidateNotes` so the host declares again — see §5.2, "Closures in declarations and evolving a field's shape".
- **Transient state doesn't survive the instance**: hover and the like are lost with it, so make them recomputable from the value and event positions. A drag that neither declared an edit nor wrote a draft may switch instances midway — the new instance never gets that release; treat it as "not dragging".
- **Events come in strict pairs**: each instance's `OnMouseEnter` / `OnMouseLeave` strictly alternate (when the instance is replaced, or the canvas appears under a pointer that isn't moving, the host sends an Enter right away instead of waiting for the user to move the mouse — **compute hover-like state in `OnMouseEnter` from its position**, not only on moves); every `OnMouseUp` has a matching `OnMouseDown` that this instance received; if mouse capture is lost unexpectedly (say, switching windows with the button held), the host sends an `OnMouseUp` with `IsClick` false so you can wrap up and commit as usual.

### 10.4 Drawing and input

- **Size**: the width follows the panel, the height is the declared height; read the current size from `host.Width` / `host.Height`. Coordinates are logical pixels (origin at top-left, DPI-independent), drawing is clipped to the canvas, and the host paints the background first (the same dark rounded fill as its input boxes) — you draw on top of it. `host.DevicePixelRatio` is the number of physical pixels per logical pixel (it follows the screen's scaling, say 1.5 or 2); you need it when you generate bitmaps yourself.
- **Drawing** (`ICanvasDrawing`, valid only during `Render`): `Draw*` strokes lines / outlines, `Fill*` fills; to fill and outline, call both — `DrawLine`, `DrawPolyline`, `DrawPolygon` / `FillPolygon` (triangles and the like), `DrawRectangle` / `FillRectangle` (optionally rounded), `DrawEllipse` / `FillEllipse` (a circle has equal radii), `DrawText`, `MeasureText`. Colors are `System.Drawing.Color` (don't forget the alpha). `DrawText` aligns the text inside the given rectangle on a 3×3 grid (`CanvasAlignment`); a zero-size rectangle means "anchor at this point". `\n` starts a new line, and each line of multi-line text follows the horizontal alignment too (`Center` centers every line); text never wraps on its own. The font is the host's (it follows the UI font) — lay out with `MeasureText`, never hard-code text widths.
- **Images**: create one with `CanvasImage.FromEncodedData(bytes)` (PNG, JPEG, WebP, SVG; SVG never loads external references) or `CanvasImage.FromPixelData(width, height, rgba)` (four bytes per pixel — R, G, B, A, not premultiplied — row by row, no padding), and `DrawImage(image, x, y, w, h)` scales it into the rectangle (the aspect ratio is not kept; to keep it, compute the rectangle from `image.Width` / `image.Height`; SVG is rasterized crisply at the drawn size). **An image can't be changed once created**: to change the content, create a new one and `Dispose` the old one (dispose first, then create, so the host can reclaim memory sooner); a frame already drawn is unaffected by Dispose. You may create and dispose images on any thread (decode in the background, say), but draw them only in `Render`. Decoding takes time, so **cache images at plugin level** — don't decode again in the factory or in every `Render`. When you generate pixels yourself, generate `width × host.DevicePixelRatio` of them to stay crisp; when the ratio changes (the window moved to another screen) the host calls `Render` again.
- **Drawing state**: `PushOpacity(opacity)` (multiplies with the outer one), `PushClip(x, y, w, h, cornerRadius)` (draw only inside the rectangle, intersected with the outer clip), `PushTransform(Matrix3x2)` (a coordinate transform from `System.Numerics`, composed with the outer one) — everything drawn afterwards is affected until you dispose the returned value, most naturally with `using`. They nest as a stack, last in first out, and whatever is still pushed when `Render` ends is popped by the host. Transforms only affect drawing: **mouse coordinates are always canvas coordinates** — invert the transform yourself for hit-testing.
- **Redraw**: the host redraws when the value, the draft or the size changes; when your own state changes (hover, say) call `host.Invalidate()`.
- **Mouse**: `OnMouseDown` / `OnMouseMove` / `OnMouseUp` / `OnMouseEnter` / `OnMouseLeave` / `OnScroll` — these and `Render` all have empty default implementations; implement only what you need. After a press the host captures the mouse (you keep getting moves and the release outside the canvas); double-clicks (`IsDoubleClick`) and clicks (`IsClick`) are decided by the host; modifier keys are normalized across platforms (Cmd on macOS reads as `Ctrl`).
- **Keyboard**: `OnKeyDown` / `OnKeyUp(CanvasKeyEvent)` (`Key`, `Modifiers`, `IsRepeat`), delivered only while the canvas **has focus** — clicking the canvas gives it focus; clicking elsewhere or switching windows takes it away. Only the keys in `CanvasKey` are forwarded (letters, digits, F1–F12, arrows, Enter, Esc, Tab, Backspace, Delete, Home / End, PageUp / PageDown, Space, Ctrl / Alt / Shift — modifier keys get their own down / up too, left and right not distinguished); other keys bypass the canvas. Presses the system repeats while a key is held have `IsRepeat` true, at the system's repeat rate. Keys come in **strict pairs** as well: every `OnKeyUp` has a matching `OnKeyDown` this instance received; if focus is lost or the canvas leaves the screen while a key is held, the host sends the release; after the instance is replaced, the first press the new instance gets has `IsRepeat` false (to it, the key was just pressed). For press-and-hold adjustments, as usual **write a draft on the first press and commit on release** — one held key becomes one undo step. There is no text input (IME / characters).
- **`e.Handled`**: every event argument has it (base class `CanvasEvent`). Set it to `true` when you handled the event and the host skips that event's default action; leave it (the default) to hand the event back to the host. The wheel and the keyboard have default actions: the wheel scrolls the panel the canvas sits in, and a key bubbles to the outer layers (shortcuts, the panel's own key handling). **Set it only when you actually use the event** (say, the pointer is over something adjustable, or it's a key you handle), never swallow wholesale — swallow the wheel and the panel gets stuck whenever the pointer sweeps across the canvas while the user scrolls; swallow keys and shortcuts like Space to play or undo stop working while the canvas has focus. For mouse buttons, moves and the like it makes no difference.
- **Threading**: every callback runs on the main thread, and `host` may only be used there; don't block in callbacks.
- **Exceptions**: the host logs them (naming your plugin) and stops calling that instance until it is rebuilt; its draft is dropped.
- **No dialogs from a canvas**: confirmation / progress / input dialogs are currently available only during a button click (§9.2).

Reference implementation: `tests/plugins/V1.Voice/StretchCurveCanvas.cs` (a stretch curve on the note panel and the core-phoneme panel: a draft on press, drag, double-click to reset, wheel over a point to nudge with a deferred commit / over empty space to scroll the panel, merged multi-selection values; with the canvas focused, ↑ / ↓ nudge the point under the pointer — hold to repeat, commit on release — and Esc cancels a drag) and `tests/plugins/V1.Voice/ImageDemoCanvas.cs` ("Canvas Images" on the note panel: plugin-level caching of an encoded bitmap and an SVG, pixels generated at the device pixel ratio, a rounded clip, opacity, a rotation transform, ← / → to rotate).

## 11. Packaging, Installation, Uninstallation

- **Package format**: zip up the package folder and change the extension to **`.tlx`**, requiring `manifest.json` at the zip's **root**.
- **Installation**: in TuneLab, drag the `.tlx` into the window, or use "Install Extension" in the extensions sidebar. Installing extracts it to the extension directory and **loads it immediately** (no restart needed).
- **Extension directory**: `%AppData%/TuneLab/Extensions/<package name>/` (Windows). "Open Extensions Folder" in the sidebar opens it directly.
- **Uninstallation**: "Uninstall" on each item in the extensions sidebar. Uninstallation is completed **after the editor closes** by a standalone `ExtensionInstaller` (deleting after file locks are released), with an option to "restart now" to take effect.
- **Disabling (without uninstalling)**: open a package's detail window from the extensions sidebar — its header has a switch that turns the **whole package** off, and each tab has one that turns off **that entry alone** (so a package whose voice works and whose effect is broken can keep the working half). A disabled entry is never registered, and a disabled package is not even loaded; the sidebar card then shows a `Disabled` badge. Both take effect **after a restart**. Keep this in mind when your plugin "doesn't load" during testing — check the switch before hunting for a bug.

---

## 12. Loading and Validation Behavior

When TuneLab loads each package: **discover** → read `manifest.json` and **judge the generation** (has `id` = V1) → **validate** (sdk-version compatible? platform matches?) → build a **per-folder ALC** for the package → load each `assembly` in turn, **verifying the declared `class` implements what this `type` requires, then instantiating & registering it** (no longer reflecting over attributes).

- Any step's failure **degrades gracefully**: only the problematic plugin/entry is skipped, **the host never crashes**, and the loading status is reflected in the extensions sidebar and the log.
- **The user's disable switch comes first, before every check above**: a package (or a single entry) the user has switched off is not validated, not loaded, and not registered — the sidebar reports it as `Disabled`, never as an error. Its declared entries are still listed, so the user has somewhere to switch it back on.
- `sdk-version` higher than the host → that package is skipped with a notice.
- `platforms` not including the current platform → that plugin is skipped.
- Entry-level validation failure (`assembly` not found, the declared `class` missing or not implementing what this `type` requires, or lacking a parameterless constructor) → **only that entry fails**, with the reason written into the sidebar tooltip; the rest of the package's entries load as usual (partial loading).

---

## Appendix: Legacy Plugins

Old plugins released before the overhaul (linking the old `TuneLab.Base` / `TuneLab.Extensions.Formats` / `TuneLab.Extensions.Voices`) are **Legacy**: their `manifest.json` has **no `id`** (or no such file at all). TuneLab identifies them as Legacy accordingly and hands them to the compatibility layer.

- **New plugins should not adopt the Legacy form** — always include `id` and the new-format fields, and write per this document.
- The Legacy compatibility layer will be kept long-term, so old plugins are under no forced-migration pressure; but new features (e.g. effect) are provided only in V1.
