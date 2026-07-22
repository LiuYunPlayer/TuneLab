# TuneLab Plugin Development Guide

> For TuneLab's new extension system (V1). This document explains how to write, package, and publish a TuneLab plugin.
> Compatibility notes for old (Legacy) plugins are in the final "Appendix: Legacy Plugins".
>
> This English document is the source of truth. A Chinese translation is at [plugin-development.zh-CN.md](plugin-development.zh-CN.md) (if they diverge, this English version wins).

---

## 1. Core Concepts

- **A plugin package = a folder**. It is the atomic unit of deployment, installation, and uninstallation, placed into TuneLab's extension directory (see §9).
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
| `description` | | One-line summary (shown in the extensions sidebar) |
| `icon` | | An icon at a path relative to the package; bitmap (`.png`/`.jpg` etc.) or vector (`.svg`) both work. **Shown as-is** — the sidebar does not add a background or clip rounded corners, so rounding/transparency/padding are all up to you (draw rounded corners into the icon if you want them). A **square** icon (e.g. 64×64 or larger) is recommended. If omitted, the sidebar uses the name's first letter over a dark rounded square as a placeholder. |
| `sdk-version` | ✅ for plugins with code | The SDK version you compiled against (e.g. `"1.0"`). TuneLab uses it for a compatibility check: a plugin requiring a higher version than the host provides is skipped. Resource packages may omit it. |

Plugin level (describing "what this package provides"). **Identity is inlined into the manifest**: one entry = one concrete registrable capability, carrying its own identity (engine id / file extension) + the full name of the implementing class. After reading the manifest the host knows what the plugin provides without loading assemblies and reflecting.

| Field | Required | Description |
|---|---|---|
| `type` | ✅ | Category: `format` / `voice` / `instrument` / `effect` / resource type (agent-model is not open to external extensions: model adapters are a host-internal module; new adapters go in via PR) |
| `engine` | ✅ for voice/instrument/effect | The engine type **id** (unique identity, e.g. `"MyEngine"`). **Immutable** — it is written into project files, so changing it makes old projects mismatch. Never localize it. |
| `extension` | ✅ for format | The file extension **id** (no dot, e.g. `"myfmt"`). Also an immutable identity. |
| `name` | | The **display name** (for UI), which may differ from the identity id and may be translated. If omitted, the UI falls back to showing the identity id. |
| `localizations` | | Per-language translations of `name`, e.g. `{ "zh-CN": { "name": "增益" } }`. If the current language is missing, it falls back to the base `name`. |
| `classes` | ✅ when it contains code | The **entry candidate class list** (an array of full-name strings, e.g. `["My.Ns.MyVoiceEngine"]`). The host **scans every class** in the array and matches each against the interfaces required by this `type`, registering on a hit (see below). The manifest is only "a description that helps the host load"; you **need not** pin down which class does which job — list all candidates and let the host claim by interface. |
| `assembly` | ✅ when it contains code | The assembly (a single path relative to the package folder) containing the candidate classes above. All candidate classes live in this assembly. Resource packages omit it. |
| `platforms` | | Platform filter, e.g. `["win", "osx", "linux"]` or with architecture `["win-x64"]`. Empty = all platforms. |

**Interface-claim rules for `classes`** (the host decides which interfaces to look for based on `type`):

| `type` | Interfaces the host looks for in `classes` |
|---|---|
| `voice` | `IVoiceSynthesisEngine` (the first hit is registered as the engine) |
| `instrument` | `IInstrumentSynthesisEngine` (the first hit is registered as the engine) |
| `effect` | `IEffectSynthesisEngine` |
| `format` | `IImportFormat` (→ registers import) + `IExportFormat` (→ registers export), each scanned, **at least one must hit**; one class may implement both |

> So a single type can require **multiple entry classes** (e.g. a format's importer + exporter), which the array carries naturally; an import-only/export-only format just lists the one corresponding class. Each candidate class needs a **parameterless constructor**.
>
> **Identity id vs display name are separate**: `engine`/`extension` are immutable identities (registration key + project-serialization reference); `name`/`localizations` are for UI display only and may be renamed/translated freely.
>
> When one assembly has multiple engines/formats, list them one by one in `extensions[]` (same `assembly`, each with its own `engine`/`extension` + `classes`).

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
  "extension": "myfmt",
  "classes": ["My.Ns.MyFormatImporter", "My.Ns.MyFormatExporter"],
  "assembly": "MyFormat.dll"
}
```

> In the shorthand form there is only one top-level `name`: it is **both** the package name and the display name of the single engine/format (`localizations` is shared the same way). If you want the package name and the engine's display name to differ, use the `extensions[]` form below and give the entry its own `name`.

### 2.3 Multiple plugins in one package

Use the `extensions[]` array, where each element is one independent plugin's metadata. Package-level fields (id/name/version/author/description/sdk-version) go at the top level and are shared:

```json
{
  "id": "com.example.suite",
  "name": "Example Suite",
  "version": "2.0.0",
  "sdk-version": "1.0",
  "extensions": [
    { "type": "format", "extension": "exfmt", "classes": ["Example.Format.Importer", "Example.Format.Exporter"], "assembly": "Example.Format.dll" },
    { "type": "voice",  "engine": "ExEngine", "classes": ["Example.Voice.ExVoiceEngine"], "assembly": "Example.Voice.dll", "platforms": ["win"] }
  ]
}
```

> `Example.Format.dll` and `Example.Voice.dll` can both reference the same `Example.Common.dll` (placed in the package), which only needs to be distributed once.
>
> Rule: when `extensions[]` is present it takes precedence, and the top-level identity fields (`type`/`engine`/`classes`/…) are ignored.

### 2.4 Resource package (no code)

Omit the code fields (`assembly`/`classes` etc.) and use only `type` to declare its purpose. TuneLab merely registers it and does not load code; the corresponding engine discovers the in-package resources at runtime:

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

Implement `IImportFormat` (import) and/or `IExportFormat` (export). A **parameterless constructor** is required. The file extension and implementing classes go in `manifest.json` (`extension` + `classes` + `assembly`); you **no longer declare them via attributes** in code. The importer and exporter can be two classes (both listed in `classes`) or a single class implementing both interfaces.

```csharp
using System.IO;
using TuneLab.SDK;

public class MyFormatImporter : IImportFormat   // listed in classes; the host claims it as importer via IImportFormat
{
    public ProjectInfo Deserialize(Stream stream)
    {
        // Parse the stream into TuneLab's project model
        var project = new ProjectInfo();
        // ... populate project ...
        return project;
    }
}

public class MyFormatExporter : IExportFormat   // listed in classes; the host claims it as exporter via IExportFormat
{
    public void Serialize(Stream output, ProjectInfo info)
    {
        // Write info into the host-provided stream. The host owns output's lifecycle
        // (create/position/close); do not Dispose/Close/Seek/reset Position — just write.
        // ... write info into output ...
    }
}
```

The corresponding manifest entry:

```json
{ "type": "format", "extension": "myfmt", "name": "My Format",
  "classes": ["My.Ns.MyFormatImporter", "My.Ns.MyFormatExporter"],
  "assembly": "MyFormat.dll" }
```

> `extension` is the immutable identity (routing + serialization); `name` is an optional display name (add `localizations` for translations).

The project model (`ProjectInfo`/`TrackInfo`/`PartInfo`/`NoteInfo`…) is defined in `TuneLab.SDK`.

---

## 5. Writing a Voice Plugin

A voice is a **singing synthesis engine** (e.g. an SVS model). This chapter is organized as "build the mental model first → walk each interface's responsibilities and pitfalls → five error-prone topics (phoneme I/O, pitch sampling, snapshots, property conventions, native-dependency packaging)". After following this chapter you should be able to deliver a voice plugin that is **thread-safe, correct under incremental re-synthesis, and unambiguous in product attribution**.

### 5.0 Mental model (read this section first)

- **Session-hosted thick model**: You implement `IVoiceSynthesisEngine` (one per engine type, parameterless constructor required; the engine id goes in `manifest.json`'s `engine`, the implementing class is listed in `classes`, and the host claims it via the `IVoiceSynthesisEngine` interface). The host calls `CreateSession` once **per MidiPart** in the project to build an `IVoiceSynthesisSession`. **All synthesis state is hosted by the session itself** — chunking, scheduling state, audio buffers, synthesis progress, and dirty (invalidation) decisions are all on your side. The reason: the invalidation dependency graph (e.g. the tiered pipeline "phoneme duration → pitch → audio", where changing an automation only requires re-rendering audio and not recomputing phonemes) is understood only by the engine; the host cannot replicate it. The host does only three things: push the change stream of project data to you, drive scheduling, and read your products to display them.
- **Declaration vs execution layering**: The session has two kinds of outward responsibilities — *declaration* (which automation tracks / readback tracks / property panels / default lyric this sound source exposes) and *execution* (synthesis). Declaration is entirely a **pure function of the current part/note parameter values**; the host recomputes it on parameter commit and diffs it to the UI (see §5.2).
- **All time quantities on the plugin side are global seconds**: note boundaries, curve query points, windowing intervals, status-segment ranges, audio-segment alignment — **all are seconds** (`double`). Ticks are the host's internal score representation and are **never exposed** to the plugin. Global time 0s = sample 0. Tempo changes (and part shifts) do not need explicit handling on your side, and there is **no incremental notification**: the host simply rebuilds the whole session (old session `Dispose`d, a new session with a new context), and the new session reads the new second values — implement `Dispose` correctly and it is naturally correct (§5.9).
- **Two views + thread discipline (the most important pitfall)**:
  - **Live view** (`IVoiceSynthesisContext` and its `IVoiceSynthesisNote` / `ISynthesisAutomation`): subscribable, **accessible only on the data thread**. Used for "receive change notifications → mark dirty", "`GetNextPendingSynthesisRange` chunking decisions", and "the `SynthesizeNext` synchronous prefix pulling a snapshot".
  - **Frozen snapshot** (`VoiceSynthesisSnapshot` and the `*Snapshot` family, `IAutomationEvaluator`): immutable, event-free, **cross-thread safe**. Background workers **only read snapshots** and never touch any live-view object.
  - Naming is the discipline: live views (`IVoiceSynthesisContext` / `IVoiceSynthesisNote` / `ISynthesisAutomation`) are data-thread only; `*Snapshot` = frozen (cross-thread). **Violating this is the most common and hardest-to-debug bug in voice plugins** (a worker thread reading a live note → data races with the editing thread). During development the host asserts the data thread at live-view entry points, so cross-thread access throws immediately to help you locate it.

Manifest entry: `{ "type": "voice", "engine": "MyEngine", "name": "My Engine", "classes": ["My.Ns.MyVoiceEngine"], "assembly": "MyVoice.dll" }` (`engine` is the immutable identity written into project files, so changing it makes old projects mismatch; `name` is an optional display name that can be translated via `localizations`; the host looks in `classes` for a class implementing `IVoiceSynthesisEngine`).

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

    // One session per part: voiceId is a key of VoiceSourceInfos (which voicebank is selected);
    // context is that part's input live view, living and dying with the session. The session is a lightweight handle — heavy model loading should be lazy.
    public IVoiceSynthesisSession CreateSession(string voiceId, IVoiceSynthesisContext context)
        => new MySession(voiceId, context);

    // —— Declaration side (property panels / automation tracks): see §5.2, all on the engine, independent of session instances ——
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => mAutomationConfigs;
    public IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => mReadbackConfigs;
    public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => mPartConfig;
    public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => mNoteConfig;

    readonly OrderedMap<string, VoiceSourceInfo> mVoiceInfos = new();
}
```

**`VoiceSourceInfo` fields** (voicebank catalog metadata; the session does not re-carry these):

```csharp
public struct VoiceSourceInfo
{
    public string Name;             // voicebank display name (localizable, see "Localization" at the end of §5.2)
    public string Description;      // one-line summary
    public ImageResource? Portrait; // optional portrait (shown in the piano window); null = none
}
```

`Portrait` uses `FileImageResource` (`TuneLab.Foundation`) and takes an **absolute path** — build it from your own package directory (see §5.10). It can point to a single image or to a frame-sequence directory, which the host decodes as needed:

```csharp
var portrait = new FileImageResource(System.IO.Path.Combine(packageDir, "voices", voiceId, "portrait.png"));
mVoiceInfos.Add(voiceId, new VoiceSourceInfo { Name = "Alice", Description = "...", Portrait = portrait });
```

> When switching the sound source (switching engines) the host discards the old session and rebuilds it with the new `voiceId` (the context is rebuilt too); the engine object itself lives on, and `Init` is called only once on first use.

### 5.2 Engine declaration side: property panels and automation tracks

The four declaration-side methods are **on `IVoiceSynthesisEngine`** (not on the session) and are **all pure functions** (same input → same output, no side effects, lightweight); the host calls them on every parameter commit and diffs the result to the UI. A statically-declared plugin ignores `context` and returns a fixed map/config; for conditional UI (a control/track that appears only when some switch is on) read the current values from `context` to decide what to return; multi-voicebank engines (one engine with multiple `voiceId`s) branch on **`context.VoiceId`**.

> **Why on the engine and not the session**: Declaration depends only on `(voiceId, current part values)` and touches no synthesis runtime state — it is inherently a pure function. Putting it on the engine lets the host compute declarations (track set / panel) **before creating a session**, so the session returned by `CreateSession` can **subscribe in its constructor to the automation tracks it declared** (during construction `context.Automations` already contains the tracks you declared, retrievable via `TryGetValue` / enumerable — the declaration is ready). If declaration lived on the session it would be a deadlock: the session wants to subscribe to the tracks it declared, but the declaration is only available after the session finishes constructing — during construction `context.Automations` would not yet be filled with your declared tracks. `DefaultLyric` is the only value that stays on the session (it is used at runtime, see §5.3).

```csharp
// Automation track set (part level): continuous tracks and piecewise tracks share one ordered map, declaration order = presentation order. context.VoiceId selects which voicebank.
public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IVoiceSynthesisPartPropertyContext context) => mAutomationConfigs;
// Read-only readback track declarations (engine-produced, non-editable curves such as energy). Return an empty map if there is no readback.
public IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IVoiceSynthesisPartPropertyContext context) => mReadbackConfigs;
// Part-level property panel (depends only on the part's own sparse values + context.VoiceId).
public ObjectConfig GetPartPropertyConfig(IVoiceSynthesisPartPropertyContext context) => mPartConfig;
// Note-level property panel (depends on part settings + the merged values of the selected notes).
public ObjectConfig GetNotePropertyConfig(IVoiceSynthesisNotePropertyContext context) => mNoteConfig;
```

> `IVoiceSynthesisPartPropertyContext` (part panel / automation): `VoiceId` + **`IReadOnlyList<PropertyObject> PartProperties`** (the sparse snapshot of each selected part; multiple parts may be selected). `IVoiceSynthesisNotePropertyContext` (note panel, a **separate interface, not inheriting**): `VoiceId` + **`PropertyObject PartProperties`** (the **single** part the note belongs to — a note always belongs to one part) + **`IReadOnlyList<PropertyObject> NoteProperties`** (each selected note). If a list member doesn't care about multi-selection, call `.Merge()` (the `PropertyObjectExtensions` extension method, in `TuneLab.Foundation`) to reduce it to a single tri-state `PropertyObject` (same key all-equal → the value, unequal/partly-missing → `Multiple`) and write as if single-selected; if you need per-member truth (e.g. combining seeds of unequal-length arrays) iterate the list directly. Having voiceId in the context makes the voice context permanently diverge from effect's `IEffectSynthesisPropertyContext` (which has no equivalent) — this is intentional: effect is a single-type engine and has no notion of "which bank to pick".

**Note / part property conventions (the keyed `Properties`, the sole channel for per-note/per-part parameters)**:

- The fixed fields of `IVoiceSynthesisNote` are only the minimal common musical quantities (`StartTime`/`EndTime`/`Pitch`/`Lyric`/`Phonemes`). **All voice-specific per-note parameters (tension, breathiness, gender, etc.) go through `note.Properties` (keyed)** — adding a new parameter = adding a key in `GetNotePropertyConfig`'s `ObjectConfig.Properties`, without touching the fixed interface surface. Part-level specific parameters go through `GetPartPropertyConfig` the same way.
- Build the panel from the control-config vocabulary (all in `TuneLab.SDK`): `SliderConfig` (constructors are sealed, use static factories only: `SliderConfig.Linear(default, min, max)` for continuous, `SliderConfig.Integer(default, min, max)` for integer, `SliderConfig.Create(default, scale)` for a custom `INormalizedScale`; fluent `.WithFormat(INumberFormat)` to customize value display/read-back, `.WithRandomizable()` to declare it randomizable — the host gives a random entry on the right, and clicking it re-picks the value uniformly on the scale in normalized space, suitable for random seeds and the like; `.WithMinLabel(text)` / `.WithMaxLabel(text)` add descriptive text at the ends of the range (e.g. min="Soft", max="Hard", translated by the plugin, may set only one end) — shown at the slider's ends, and when the user pins this property onto the parameter panel the same text serves as the bounds, sharing semantics with `AutomationConfig`'s same-named fields), `ComboBoxConfig.Create(options)` (value/display separated, so "UI in Chinese / stores the enum value underneath"; `.WithDefault(option)` sets the default selection, otherwise the first item), `CheckBoxConfig.Create(default)`, `TextBoxConfig.Create(default)` (`.WithPassword()` for masking); all the above constructors are sealed, use static factories only. The container is `ObjectConfig { Properties = OrderedMap<string, IControllerConfig> }` (the composite type is not yet factory-ized).

```csharp
readonly ObjectConfig mNoteConfig = new()
{
    Properties = new OrderedMap<string, IControllerConfig>
    {
        { "tension",   SliderConfig.Linear(0, -1, 1) },
        { "breathiness", SliderConfig.Linear(0, 0, 1) },
    },
};
```

- **Reading values**: at synthesis time, read from `VoiceSynthesisNoteSnapshot.Properties` (a `PropertyObject` value copy) using `GetDouble(key, default)` / `GetBoolean` / `GetString`. **Sparse storage** — only fields the user has changed are present; if not found use the default you declared (`PropertyObject`'s `Get*` second argument is the fallback; pass the same default as declared).
- **`AutomationConfig`**: `DisplayText` / `DefaultValue` / `MinValue` / `MaxValue` / `Color` (e.g. `"#E5A573"`) / `Randomizable` (adds a random entry to the right of the default-value panel's slider, best for continuous tracks). **`DefaultValue = double.NaN` ⇒ a piecewise track** (no default baseline, disconnected between segments, e.g. pitch-like, bend); a real number ⇒ a continuous track (has a value everywhere, has a baseline, e.g. growl). Readback tracks are always piecewise (`DefaultValue = NaN`).
- **Value-axis scale**: `AutomationConfig.Create(minValue, maxValue)` is a linear axis; the `Create(INormalizedScale)` overload takes a custom scale (mirroring `SliderConfig.Create`) — e.g. `NormalizedScale.Integer(min, max)` makes it an integer track, or implement your own log axis etc. **A discrete scale ⇒ the signal lands on the grid everywhere**: beyond snapping anchors on write, the host projects the continuous Hermite output back onto the scale at **evaluation and rendering** time (the curve renders as a staircase, and `Evaluate` returns already-gridded final values), so the **engine need not round** and every edit path (load / preset / fed-back data) is covered. On a linear scale the projection is pure range-clamping (out-of-range values clamped to `[min,max]`).
- **Binary interval track (band / toggle)**: for an on/off interval track (e.g. a breathiness switch, section mute), declare a **piecewise track with a degenerate range** — `AutomationConfig.Create(v, v)` (`Create` defaults to `DefaultValue=NaN` ⇒ piecewise; `min==max` ⇒ no value axis). The host recognizes this form and renders it as a **full-height toggle band** (segment = on-interval highlighted, gap = off/blank) instead of a curve, with interaction switched to horizontal drag = paint on / right-drag = paint off (vertical ignored, there is no height). Consumption is pure segment presence: `!double.IsNaN(evaluator.Evaluate(t)[i])` means "on"; the value inside a segment is irrelevant and need not be read. Interval boundaries = the segment's anchor span, dragged precisely by the user.
- **Conditional declaration + orphaned data**: the track set may show/hide with parameters (e.g. a Growl track exposed only when some switch is checked). After a track disappears from the declaration, the host **keeps its already-drawn curve (hidden, not deleted, not participating in synthesis)**, and rolling the parameter back to make the track reappear restores it as-is — you need not worry about losing user data when toggling conditional tracks.

> ⚠️ **Automation parameter names must avoid host reserved names**: the keys of `GetAutomationConfigs` are merged and displayed with the host's built-in automations, and **a name clash with a built-in is claimed by the built-in and your parameter won't show**. Known reserved names: **`Volume`**, **`VibratoEnvelope`**. Use your own distinctive names (e.g. `Breathiness` / `Growl` / add a prefix).

> **Localization**: `DisplayText`, `ComboBox` option text, track names, voicebank names/summaries are all translated by you — read `TuneLabContext.Global.Language` (e.g. `"zh-CN"`) and produce text from your own dictionary; the host does not do any lookup. When untranslated, just return the English as-is. The manifest's `name`/`description` are localized via the `localizations` field.

#### Phoneme properties (`GetPhonemePropertyConfigs`)

Beyond notes, the engine can also declare user-editable custom properties on **phonemes** — parallel to `GetNotePropertyConfig`, but both the declaration side and the value-reading side drop down to a single phoneme. Typical use cases: attaching an "articulation strength" slider to the vowel (nucleus), or a different set of properties to consonants.

```csharp
// per-phoneme property declaration (required, must be implemented just like GetNotePropertyConfig):
// **reuses the note declaration context IVoiceSynthesisNotePropertyContext** (there is no separate phoneme context) —
// each IVoiceSynthesisNoteView now carries Phonemes (that note's ordered phonemes). Return a schema map **keyed by
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
- The schema still uses the same control-config vocabulary (`SliderConfig` / `ComboBoxConfig` / `CheckBoxConfig` / `TextBoxConfig`, container `ObjectConfig`), written the same way as note properties.

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
- **Editing UI**: the sidebar phoneme-property panel is done — **one row per slot** (a symbol label + that slot's controllers); with multiple notes selected, each note's phoneme at the same slot merges into that row (three-state values, edits fan out). Still to be done is a phoneme **selection model** (a phoneme currently has no `ISelectable` selection state); until the selection model is complete, the host uses all phonemes of the selected notes as the panel scope.

### 5.3 Input live view `IVoiceSynthesisContext` and `IVoiceSynthesisNote`

> The session itself (`IVoiceSynthesisSession`) now declares only **`DefaultLyric`** (`string`, the default lyric for a new note) — it is a runtime value consumed only after creation and does not participate in pre-construction declaration, so it stays on the session instance; all other declarations (tracks/panels) are on the engine (§5.2).

The context is implemented by the host, is session-scoped (dies with the session), and is **accessible only on the data thread**. You subscribe to it to perceive input changes, and in the `SynthesizeNext` synchronous prefix you materialize a snapshot from it via `GetSnapshot`.

```csharp
public interface IVoiceSynthesisContext
{
    IReadOnlyNotifiableLinkedList<IVoiceSynthesisNote> Notes { get; }   // linked list: consume in enumeration order, First/Last, note.Next/Previous neighbor navigation; WhenAny auto-wires member add/remove
    IReadOnlyNotifiablePropertyObject PartProperties { get; }
    IReadOnlyMap<string, ISynthesisAutomation> Automations { get; }   // get the editable tracks you declared (TryGetValue / enumerate)
    ISynthesisAutomation Pitch { get; }            // absolute pitch constraint (piecewise: has value = pinned, NaN = free), see §5.6
    ISynthesisAutomation PitchDeviation { get; }   // additive deviation (continuous, default 0, never NaN), see §5.6
    VoiceSynthesisSnapshot GetSnapshot(IReadOnlyList<IVoiceSynthesisNote> notes, double startTime, double endTime);  // see §5.5
    IAudioSegment CreateAudioSegment(long sampleOffset, int sampleCount, int sampleRate);             // see §5.8
    IActionEvent Committed { get; }            // logical-edit closure point, see §5.9
}
```

**`Notes` ordering and overlap**: total-order deterministic — `StartTime` ascending → same start, `EndTime` descending (long note first) → still tied, keep insertion order. Notes **may overlap** (chords): the sequence passes overlapping notes through as-is, and de-overlapping (e.g. "later covers earlier") is **your responsibility** (a monophonic plugin truncates as needed; a chord plugin consumes overlaps as-is).

**`IVoiceSynthesisNote` fields** are all subscribable properties (`IReadOnlyNotifiableProperty<T>`, with `Value` / `WillModify` / `Modified`): `StartTime`/`EndTime` (global seconds), `Pitch` (`int` semitones), `Lyric` (`string`), `Phonemes` (`IReadOnlyList<SynthesizedPhoneme>`, see §5.7), `Properties` (keyed per-note parameters). There is also a `Next`/`Last` neighbor chain — **for data-thread chunking decisions only** (inside an event handler you have only the note's own reference, no list index); at synthesis time you must navigate neighbors by index over the snapshot's ordered list and never touch live notes again.

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
    var snapshot = mContext.GetSnapshot(piece.Notes, piece.Notes[0].StartTime.Value, piece.Notes[^1].EndTime.Value);
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
VoiceSynthesisSnapshot GetSnapshot(IReadOnlyList<IVoiceSynthesisNote> notes, double startTime, double endTime);
```

- **`notes`**: the notes this synthesis needs — **in-segment notes + coarticulation neighbors** — which you pin down freely (e.g. if you want to see the previous note's trailing consonant, include it too). The returned `snapshot.Notes` is **index-aligned** with the `notes` you passed in — this is the product-attribution contract (see §5.7: the `SynthesizedPhonemes` map keys back with `origins[i]`).
- **`[startTime, endTime]`**: the windowing interval (seconds) for automation curves.
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
    double StartTime { get; }  double EndTime { get; }    // global seconds. EndTime = effective end (after the host de-overlaps by clamping later-covers-earlier to the next note's start, a monophonic audio measure); the host owns phoneme layout exclusively and does not expose the full end
    int Pitch { get; }         string Lyric { get; }
    IReadOnlyList<VoiceSynthesisPhonemeSnapshot> LeadingPhonemes { get; }  BodyPhonemes { get; }   // pinned phonemes as two structured lists (leading = onset consonants / body = nucleus + coda); both empty = not pinned. Element { Symbol; Duration; StretchWeight; Properties } (geometry flat), see §5.7. Phonemes = Leading ++ Body read-only view
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
- The pitch readback you **produce** goes through `SynthesizedPitch` (the named rich type `SynthesizedPitch { IReadOnlyList<IReadOnlyList<Point>> Segments }`, a piecewise polyline, `Point = (global second, semitone)`), for the host to draw the readback line on the pitch track. Empty = `new() { Segments = [] }`. Other acoustic quantities (e.g. energy) go through readback tracks (§5.2 + §5.8).

### 5.7 Phoneme I/O: `SynthesizedPhoneme` (same type read-in / output)

The phoneme descriptor is **direction-agnostic** — read-in (the user's pinned constraints) and output (synthesis products) both use the same `SynthesizedPhoneme`: it reports only "nominal duration + weight", and **not absolute position, not before/after attribution**. Leading / body attribution is a **structured pair of lists** (`LeadingPhonemes` / `BodyPhonemes`, membership *is* the classification — engine-declared, jitter-proof, cross-beat phonemes attributed explicitly, no alternating-flag illegal states); geometry is a single signed **`BodyOffset`** (the body start = the junction between the two lists, relative to the note head). Classification and geometry are orthogonal. Positioning / cross-note de-overlap compression / melisma layout are all derived by the **host** via the same duration model (an engine reporting already-compressed absolute positions would make the host misjudge the adjacency criterion, so report only natural durations and let the host own layout exclusively).

```csharp
public struct SynthesizedPhoneme { public string Symbol; public double Duration; public double StretchWeight; }
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

Calling: materialize each note into a `PhonemeLayoutNote` (`FillStart` = the note head; `FillEnd` see above; `Phonemes` = that note's phonemes, ordered leading consonant → nucleus → trailing consonant), pass the whole segment to `Resolve`, and it returns an isomorphic jagged array `PhonemeTiming[][]` (`{ Start, End, Duration }`, destructurable as `var (s,e)=`) — `result[i][j]` = the real placement of `notes[i].Phonemes[j]`. `Resolve` holds for any contiguous note range; the host display passes a window, you pass the whole segment, same function. What's frozen is only the I/O shape; the compression body can evolve host-side, and since you bind that copy at runtime it doesn't drift.

**Pinning override**: `snapshot.Notes[i]`'s `LeadingPhonemes` / `BodyPhonemes` non-empty = that note is user-pinned, so when materializing `PhonemeLayoutNote` use its pinned two lists + `BodyOffset` rather than your G2P prediction; only use the prediction when both are empty. The snapshot's list elements are `VoiceSynthesisPhonemeSnapshot`, so rebuild the geometry fed to layout as `SynthesizedPhoneme` from `ph`'s fields (`{ Symbol = ph.Symbol, Duration = ph.Duration, StretchWeight = ph.StretchWeight }`), set `PhonemeLayoutNote.LeadingPhonemes` / `BodyPhonemes` / `BodyOffset` from `snapshot.Notes[i]`, and take per-phoneme properties from `ph.Properties` (see "Phoneme properties" in §5.2 and the end of §5.7).

- **Global allocation semantics (multiplication within available space / proportional allocation, per-note independent, no single/cross two paths)**: each note takes its available space `[note head … nucleus fill end FillEnd]`, collects the phonemes falling within it — this note's non-leading (nucleus + trailing consonants), plus **only when adjacent to the next note** the next note's leading consonants — and allocates uniformly by the **scale ratio `len/d = r^w`**: a stretchable phoneme (w>0) has its length multiplied by `r^w` of its original length, a rigid phoneme (w=0) always stays at its original length; `r` is the single global reference scale ratio uniquely determined by "total length after allocation = available space" (>1 stretch / <1 compress / =1 unchanged). **Same weight ⇒ same scale ratio (proportional shape preservation, relative proportions unchanged)**, and larger `w` means more drastic stretching (`w=2` is the square of `w=1`). Two degenerate cases: ① the available space is squeezed to where even the rigid nominal lengths don't fit (`space ≤ Σ_{w=0}d`) → stretchable phonemes all go to 0, and rigid phonemes compress proportionally to their nominal lengths to fill it; ② all `w=0` (no stretchable phonemes) → everything scales proportionally by nominal length as a whole. Example `kas`+`bus` adjacent: the nucleus `a` absorbs the stretch exponentially, the consonants `s`/`b` are rigid, and only when squeezed to the limit do the consonants get proportionally compressed. **Invariant: a phoneme never overflows the available space.**
- **Gap (silence) = phonemes don't affect each other**: only **adjacent / overlapping** neighbor notes cooperate across notes (sharing that available-space allocation above). When two notes **have a gap** (the earlier note's content end < the later note's nucleus start), the two notes' phonemes **each keep their natural geometry and do not push/compress each other** — the later note's leading consonant may naturally reach into the gap and overlap the earlier note in the display, but nothing moves. This way fixed phonemes don't jump because a neighbor moves within the gap; if the user **wants it adjustable (leading consonant pushing the earlier note's vowel), drag the notes to be adjacent**. **How the synthesis side handles the gap is up to you (the engine)**: merged continuous inference (→ pushing) or producing chunks independently and overlap-mixing on the timeline (→ overlapping voicing) is your audio decision; the host only displays the ideal form (adjacent = pushing, gap = independent). If you drive audio with `Resolve` and want WYSIWYG, keep `FillEnd` on the same measure (stop at your own end in the gap); deviating makes audio and display diverge at the gap, which **is audible when driving audio, not "non-fatal"** ("non-fatal" holds only for display-only misalignment, see "Two uses" above).
- **Continuation and rest (the decision is yours)**: the host treats a **note you produce no phonemes for** as transparent — the previous note's phonemes flow forward over it (melisma). To make a note voice **silence / a breath**, output a still phoneme for it (e.g. `sp` / `AP`) — it is then no longer transparent and forms a boundary. **Which notes are continuations (legato / melisma passengers) is your decision**: implement `bool IsContinuation(IVoiceSynthesisNote note)` on the session — **it must be implemented, deliberately with no default body**: the decision and your synthesis behavior are a paired promise, and any default body would promise, on behalf of your synthesis, a semantics it might not implement (a `"-"`-fills-end default lies about engines that don't do melisma; an always-`false` default masks an engine that does melisma but forgot to implement this method), so silent inheritance is silent divergence, and the interface forces you to state it explicitly. **An engine that does no legato semantics honestly returns `=> false`** (every note is content). **The host adds none of its own criteria and consumes it as given** (display layout / editing gestures share the source) — your markers (`"-"`, `"+"`, "ー", dictionary-driven syllable layout) automatically get host UI support. Reference semantics (the decision corresponding to the editor's `"-"` entry convention, which the host also uses to display parts with no sound source; full code in the sample plugin `tests/plugins/V1.Voice`, a ten-line chain walk-back): the lyric is `"-"` ∧ walking back through the unbroken adjacency chain reaches a content note (adjacency = earlier end ≥ later start, strict comparison — boundaries share the source tick conversion, so adjacency is exact equality; a gap breaks the chain / a missing chain head → an orphan `false`) ∧ this note has no pinned phonemes (a pin is content, exiting the passenger role and becoming a legitimate chain head). Chains / adjacency / markers are all your semantic space (e.g. an engine policy like "a small gap counts as adjacent"), and the SDK deliberately provides no decision helper — the decision is bound to synthesis behavior, so you must fully own its semantics. Contract constraints: synchronous on the data thread, retain no note reference, deterministic observation (same current data → same answer), **must not depend on synthesis progress/products** (the decision must not change based on "whether it has been synthesized"). **Binding**: a note you decide is a continuation **must not** return phonemes in `SynthesizedPhonemes` — the whole-region voicing (including a trailing consonant glued to the end of the melisma) is all returned attributed to the **chain-head note**; a violation is not corrected or discarded by the host, and the display faithfully falls back to the content you returned (self-contained rendering + skeleton correction), so it's your bug. Pinned phonemes are **peers of** lyric/position — they are inputs to your decision, not host-enforced conditions: `Standard`'s default semantics choose "a pin is content, exiting the passenger role" (a pinned note is a legitimate chain head), and your custom decision may freely decide the status of a pin in the continuation decision — the host respects the return value as given, and a note decided to be a continuation displays transparent (**even with pins**, its semantics defined by you to the user). **The decision domain is live data**: the snapshot window may cut off the chain head, so when you need to carry the continuation identity into the worker, call your own decision on the live notes in the `SynthesizeNext` synchronous prefix and freeze it along with your own snapshot structure (an engine that chunks by note gaps and decides within the chunk is equivalent to live).

**Output (engine → host, returned at synthesis time): a phoneme map keyed by attribution note**

```csharp
IReadOnlyMap<IVoiceSynthesisNote, SynthesizedSyllable> SynthesizedPhonemes { get; }
// SynthesizedSyllable = { IReadOnlyList<SynthesizedPhoneme> LeadingPhonemes; IReadOnlyList<SynthesizedPhoneme> BodyPhonemes; double BodyOffset; IReadOnlyList<SynthesizedPhoneme> Phonemes /* = Leading ++ Body view */ }
// Each attribution note → its leading / body phoneme lists (reporting only Symbol / Duration / StretchWeight, no absolute position) + BodyOffset (junction = noteStart + BodyOffset; 0 ⇒ body start exactly on the head, supports cross-beat phonemes)
```

- **Keyed by attribution note** (rather than a flat timeline + an origin field): the phoneme descriptor reports no absolute position, an ownerless phoneme has no anchor to be positioned and can't fall into a note's invalidation chain, so **there is no "ownerless phoneme" contract** — `SynthesizedPhoneme` has no `Note` field, and attribution is entirely expressed by the map key. Boundary crossings like a consonant invading the previous note's tail arise naturally when the host derives positions by the duration model, without you declaring positions. (Breath etc. will later be carried by "the attribution note's leading / trailing phonemes" or a dedicated event channel.)
- **How to fill the map key**: use the **live note list** (`origins`) you passed to `GetSnapshot`, retrieved by **snapshot index alignment** — the product of `snapshot.Notes[i]` is attributed to `origins[i]`, so add that note's set of phonemes to the map keyed by `origins[i]`. The key is used only as an identity token (attribution); **you must not read its properties during synthesis** (that is the live view, and on the worker thread it is a violation). A dirty / mid-synthesis block **should not** report its notes' phonemes in the map (the host leaves it blank accordingly).
- **`StretchWeight` (stretch / compress weight)**: after the user locks phonemes, the host fixes the product to the pinned geometry (`note.Phonemes`'s `StretchWeight` comes from this), and thereafter display / synthesis de-overlap allocates by the **scale ratio `len/d = r^w`**: **the vowel (w>0) absorbs stretch exponentially**, **the consonant (w=0) is rigid and doesn't move** (compressed proportionally to nominal length only when the space is too tight even for the consonants). A typical syllable is `[lead consonant w0, vowel w1, trailing consonant w0]`. **With no phonological knowledge, just fill the same positive value everywhere (e.g. `w = 1`)** — all phonemes scale proportionally, a safe default; distinguishing consonant=0/vowel=1 is an optimization done only with phonological knowledge, and to make a vowel stretch more drastically give a larger `w` (e.g. `w=2` for a diphthong's main vowel). All `w` = 0 (including unset, the struct default of all zeros) degenerates to scaling the whole thing proportionally by nominal length, with no division by zero.
- **The nucleus duration is a base ratio, cancelled out when single**: a nucleus's (`StretchWeight>0`) `Duration` is its original length — with **multiple nuclei** it sets the base ratio between them (each multiplied by `r^w`); with a **single nucleus** the original length is cancelled out and it degenerates to filling the nucleus space, so whatever you report is the same. A consonant's (w=0) `Duration` is its fixed length. Leading / body attribution is the list membership (structural, not derived), and `BodyOffset` places the junction (leading accumulates leftward from it, body rightward). Therefore the output **needs no positioning by you** — just honestly report each phoneme's duration + weight, its list, and the note's `BodyOffset`, and the host derives positions uniformly — zero jumping.
- **Preview is display-only, never fed back to you as a constraint**: the authoritative durations are returned by a full re-timing synthesis (with new weights), overriding the preview. You just honestly output the current durations + weights each synthesis.

### 5.8 Audio product and status

**Audio is delivered via the segment handle `IAudioSegment`** (not a flat pull) — because the downstream effect chain re-renders incrementally per segment, and the segment is the effect's invalidation/re-render unit.

```csharp
public interface IAudioSegment : IDisposable   // Dispose() = delete this segment (rebuilt on re-chunk / length or position change)
{
    void Write(int offset, ReadOnlySpan<float> samples);  // write in place within the segment [offset, offset+len); the span is borrow-semantics, reusable after return
    void Commit();                                        // mark this segment's audio as fixed — the [only gate] to the effect
}
```

- A segment is requested via `context.CreateAudioSegment(sampleOffset, sampleCount, sampleRate)`: `sampleOffset` = the global start sample position (**the plugin's native rate**, global 0s = sample 0); `sampleCount` = segment length (in samples); `sampleRate` = **that segment's native sample rate** (you pass it, the host interprets accordingly — equal to the project rate is read directly, unequal wraps a resample, centralized in one place in the host). **The sample rate travels with the segment and may differ per segment** (e.g. offering a synthesis-sample-rate dropdown).
- A segment's **start and length are fixed at creation** (the host allocates the buffer once, you write in place, and progressive synthesis doesn't accumulate re-copies); to change position/length → `Dispose()` the old segment and `CreateAudioSegment` a new one. Each re-render of a segment is "drop the old, build the new".
- **`Commit()` is the only gate to the effect**: `Write`s before Commit are only for progress/waveform display; only frozen data (Commit) enters the effect. So a synthesis burst does not drag expensive effects into frequent reruns.
- Writing/committing/releasing are **all on the data thread** (after the worker renders, write in the continuation marshaled back to the data thread).
- **Silent segments**: the host buffer is zero-initialized, so after `CreateAudioSegment` just `Commit()` directly, no `Write` needed.

**The status band `SynthesisStatusSegment`** (returned by `Status`, used by the host to color/progress/report errors):

```csharp
public struct SynthesisStatusSegment
{
    public double StartTime; public double EndTime;       // seconds
    public SynthesisSegmentStatus Status;                 // Pending / Synthesizing / Synthesized / Failed
    public string? Message;                               // Failed = error message; Synthesizing = optional stage text (e.g. "computing phoneme durations"), shown by the host as-is
    public double Progress;                               // [0,1] while Synthesizing, kept at 0 if not reported
}
```

- The status segments and audio segments are **decoupled**: the former is the UI status band, the latter is the effect invalidation unit; the two partitions may differ and the host does not assume alignment.
- **`StatusChanged` is the only refresh signal**: whenever the product (audio/pitch/readback/phonemes) or the status has any update, fire it, and the host re-reads and re-draws on receipt. Outbound events may be fired from any thread and the host marshals — but your product fields must swap references on the data thread (swapping references = immutable publication).

**Readback track data** goes through `SynthesizedParameters` (`IReadOnlyMap<string, SynthesizedParameter>`, keys aligned with `GetSynthesizedParameterConfigs`):

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
  - **Let the user configure the model path via extension settings**: the engine implements `IExtensionSettings`, exposes a "model directory" setting with `TextBoxConfig`, the user fills the path in "Settings → Extensions", and you receive it in `ApplySettings` and load from that path in `Init`/`CreateSession` (see §8). Secrets like API keys use `TextBoxConfig { IsPassword = true }`, which the host masks and stores securely.
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
| `IVoiceSynthesisEngine.GetSynthesizedParameterConfigs` | data thread | read-only readback track declarations (always piecewise) |
| `IVoiceSynthesisSession.DefaultLyric` | data thread | default lyric for a new note (a session-level runtime value) |
| `GetNextPendingSynthesisRange` | data thread | peek the next dirty block boundary (no side effects, deterministic) |
| `SynthesizeNext` | synchronous prefix = data thread; then worker | pull snapshot → offload render → publish back on the data thread |
| `GetSnapshot` | **synchronous prefix only** | materialize an immutable snapshot (pin notes + window) |
| `CreateAudioSegment` / `IAudioSegment.Write/Commit` | data thread | request and write an audio segment; Commit is the gate to the effect |
| `SynthesizedPitch/Parameters/Phonemes`, `Status` | published on the data thread, readable cross-thread | products; publication = immutable |
| `StatusChanged` | fired from any thread, host marshals | the only refresh signal |
| `Dispose` | data thread | unsubscribe, release models and segment handles |

---

## 6. Writing an Effect Plugin

An effect transforms **already-synthesized whole-segment audio**. It targets **relatively slow offline models** (e.g. SVC voice conversion, neural timbre conversion), not real-time VST-style effects.

Implement `IEffectSynthesisEngine`. A **parameterless constructor** is required. The effect id goes in `manifest.json`'s `engine`, and the implementing class is listed in `classes` (the host claims it via the `IEffectSynthesisEngine` interface, no longer using attributes). There is one engine per effect type; the host creates a **persistent thick processor** `IEffectSynthesisSession` per "effect instance × upstream audio segment" in the project to drive it. The processor holds its own segment's context `IEffectSynthesisContext`, **subscribes itself, and manages invalidation and reprocessing itself** — the engine-private invalidation graph (which parameter / which automation segment marks dirty and triggers which internal recomputes) lives inside the processor, which the host cannot replicate, hence a thick model.

Manifest entry: `{ "type": "effect", "engine": "MyEffect", "name": "My Effect", "classes": ["My.Ns.MyEffectEngine"], "assembly": "MyEffect.dll" }` (`engine` is the immutable identity; `name` is an optional display name that can be translated via `localizations`; the host looks in `classes` for a class implementing `IEffectSynthesisEngine`).

```csharp
using TuneLab.Foundation;
using TuneLab.SDK;

public class MyEffectEngine : IEffectSynthesisEngine   // engine id is declared in the manifest's "engine"
{
    // Property panel / automation tracks / readback tracks: all pure functions of the current parameter values (context.Properties) — the host recomputes on parameter commit
    // and diffs to the UI, so controls/tracks may show/hide with parameters (conditional declaration). A static one ignores context and returns fixed values (as below).
    public ObjectConfig GetPropertyConfig(IEffectSynthesisPropertyContext context) => mPropertyConfig;
    public IReadOnlyOrderedMap<string, AutomationConfig> GetAutomationConfigs(IEffectSynthesisPropertyContext context) => mAutomationConfigs;

    // Synthesized-parameter readback track declarations (read-only, independent of editable automation tracks): the read-only curves the processing produces (e.g. loudness) are exposed as first-class read-only tracks,
    // piecewise (DefaultValue=NaN), with their own DisplayText/Min/Max/Color. An engine with no readback returns an empty map.
    public IReadOnlyOrderedMap<string, AutomationConfig> GetSynthesizedParameterConfigs(IEffectSynthesisPropertyContext context) => mReadbackConfigs;

    // Parameterless: the package directory is self-located via Assembly.Location (no host-passed path). On failure just throw; the host catches at the call boundary → passthrough degradation.
    public void Init() { /* ... load the model ... */ }
    public void Destroy() { /* release resources */ }

    // One persistent thick processor per "effect instance × one upstream audio segment"; the context is host-implemented, exposing this segment's input + parameters/automation + output ports + closure event.
    public IEffectSynthesisSession CreateSession(IEffectSynthesisContext context) => new MyEffectProcessor(context);

    readonly ObjectConfig mPropertyConfig = new()
    {
        Properties = new OrderedMap<string, IControllerConfig>
        {
            { "amount", SliderConfig.Linear(1.0, 0.0, 2.0) },
        },
    };
    readonly OrderedMap<PropertyKey, AutomationConfig> mAutomationConfigs = new();
    readonly OrderedMap<PropertyKey, AutomationConfig> mReadbackConfigs = new()
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
        mContext.Input.Committed.Subscribe(OnDirty);          // upstream audio re-committed
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

    // This segment's readback curves (keys aligned with GetSynthesizedParameterConfigs): published on the data thread, host read-only, re-read along with the product on wrap-up. Return an empty map if there is no readback.
    public IReadOnlyMap<string, SynthesizedParameter> SynthesizedParameters => mReadback;

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

        // Automation (optional): sample values at sample time points, query axis = global seconds (same time system as audio).
        double[]? env = null;
        if (mContext.Automations.TryGetValue("intensity", out var automation) && count > 0)
        {
            double segStart = rate > 0 ? (double)offset / rate : 0;
            var times = new double[count];
            for (int i = 0; i < count; i++) times[i] = segStart + (double)i / rate;
            env = automation.Evaluate(times);
        }

        // —— After this you may offload to a worker (only reading the immutable values materialized above, never touching host live data) ——
        var dst = DoProcess(src, amount, env);

        // Output: registry semantics (same as voice) — segmentation of the product is free (1-to-N is legal, e.g. a
        // silence splitter); each segment lives independently (Write/Commit/Dispose; to change geometry, Dispose then recreate).
        var outSegment = mContext.CreateAudioSegment(offset, dst.Length, rate);
        outSegment.Write(0, dst);
        outSegment.Commit();

        // Readback: swap references on the data thread in sync with the output (in this example Process is fully synchronous, so swap directly).
        mReadback = BuildLoudness(dst, rate, offset);
        return Task.CompletedTask;                      // errors throw (host catches → passthrough); don't swallow here
    }

    void OnDirty() => mDirty = true;   // consumed inside Process to decide what to recompute (cache refinement only)

    public void Dispose()
    {
        mContext.Input.Committed.Unsubscribe(OnDirty);
        mContext.Properties.Modified.Unsubscribe(OnDirty);
        /* release this segment's resident state, output segment handles */
    }

    readonly IEffectSynthesisContext mContext;
    bool mDirty;
    IReadOnlyList<SynthesisStatusSegment> mStatus = [];   // publish by swapping the reference (atomic)
    IReadOnlyMap<string, SynthesizedParameter> mReadback = new Map<string, SynthesizedParameter>();
}
```

Key points:

- **Thick session, host-owned invalidation**: `CreateSession(context)` builds a **session** for "this effect × this upstream segment" (the same family of persistent stateful entity as a voice session -- holds live views, keeps caches across `Process` calls, publishes claims and readback; the scope difference is expressed by the context binding); the host `Dispose`s it on segment destruction / effect deletion / re-segmentation / sample-rate change. The host schedules conservatively on scoped signals (it performs the automation-range/segment intersection generically — an edit in another segment's interval never wakes this node); parameter-dependency and value-level dedup are the engine's optional early-out inside `Process` (return without re-committing → downstream skipped).
- **The input is an indivisible whole segment**: `context.Input` (`IEffectSynthesisAudio`) exposes `SampleOffset/Count/Rate` + `Read(offset, span)` (copy-out; the host storage layout is an implementation detail). There is no "committed" pulse -- being called into `Process` means the input is ready (scheduling is the host's job). `Input.RangeModified(start, count)` is the content-change **ledger** (optional; `start` is an **absolute sample position** -- content is pinned to the absolute axis, so ranges accumulated before an upstream `Resize` need no rebasing): cache-savvy engines accumulate ranges, narrow the recompute window from the ledger (`Read`/recompute/write back only the changed region -- O(changed) work; see the Slow Gain reference implementation for the full pattern), and clear the ledger only after a successful commit of their own output (cancellation refunds it). The ledger is **complete and faithful to trims**: an upstream `Resize` reports its geometric symmetric difference (a trimmed-away region went "from something to nothing" on the absolute axis, and it was context feeding the neighboring output), so ranges may lie outside the current extent; **the recompute scope is the session's own decision** -- after collecting the ledger, expand by your own context margin (so new content joins up with old) and intersect with the extent; a pointwise engine decides zero (just `Resize` its output to follow and commit empty). Only a genuinely change-free commit is silent; whole-segment reports appear only on forced invalidation (first snapshot / project sample-rate change).
- **Output via handles, registry semantics**: `context.CreateAudioSegment(offset, count, rate)` — segmentation of the product is free (**1-to-N is legal**: e.g. a silence **splitter** that re-establishes segment granularity so every downstream effect gets per-segment incrementality and parallelism for free); each output segment lives independently and each committed one feeds a downstream node. The input side stays single-segment (the consumption unit is the host's invalidation/scheduling/identity granularity). The only hard rule: **do not redistribute the time axis** (automation/readback and the part display share the global-seconds axis); slight geometry differences (frame padding, added tails) are fine. The sample rate travels with the segment (when it differs from the project rate the host wraps a resample). **Geometry changes carry two distinct intents**: a semantic overhaul goes through `Dispose`+`Create` (downstream identity rebuilds); a content-continuous extension/trim goes through **`Resize(offset, count)`** (identity preserved, downstream caches survive -- intersecting content is kept aligned by absolute position, new regions are zeroed, the segment drops to uncommitted and is re-`Commit`ted after the new regions are written).
- **Status claims (optional)**: publish a status timeline via `Status` (immutable list swap) and fire `StatusChanged` (any thread) -- a Synthesizing segment with `Progress` renders as a vertical fill on the strip; Synthesized segments are "claimed done" (soft, non-final). An engine that reports nothing gets a host default derived from scheduling facts.
- **Readback tracks (optional)**: the read-only curves the engine produces are declared via `GetSynthesizedParameterConfigs` + carried per-segment via `IEffectSynthesisSession.SynthesizedParameters` (the host stitches the segments of the same effect by key). Read-only, non-editable, not in the data layer, not serialized; isomorphic to voice readback, shown/hidden by source in the parameter-area title bar.
- **Conditional declaration**: `GetPropertyConfig` / `GetAutomationConfigs` / `GetSynthesizedParameterConfigs` are pure functions of the current parameter values (same input → same output, no side effects, lightweight), recomputed on parameter commit — so controls/tracks may show/hide with parameters. After a track disappears from the declaration the host **keeps its already-drawn curve** (hidden, not deleted), restored as-is when the parameter rolls back.
- **Effect chain**: multiple effects may hang on one MidiPart, **serial** in declaration order — the previous output is the next input; at the chain tail the segments are mixed by absolute time. Chain order, bypass, and add/remove are managed by the user in the property panel.
- **Graceful degradation / cancellation on failure**: when an exception is thrown the host treats that segment as passthrough, without interrupting playback; cancellation is requested via `cancellation` and returns normally (**do not** throw `OperationCanceledException`), and the scheduling slot is released only when the `await` actually returns.
- **Thread discipline**: the `context` (`Input` / `Properties` / automation) may be read only in the **synchronous prefix** (data thread) of `Process`; after offload read only the materialized immutable values; `SynthesizedParameters` and the output segment must be published on the data thread.

The related interfaces are all in `TuneLab.SDK`: `IEffectSynthesisEngine` / `IEffectSynthesisSession` / `IEffectSynthesisContext` / `IEffectSynthesisAudio` / `IAudioSegment` / `IEffectSynthesisPropertyContext` / `ISynthesisAutomation`.

---

## 7. Writing an Instrument Plugin

An instrument is a **polyphonic sound source** (synth / sampler / chord source). It is **mechanically isomorphic to voice** (engine lifecycle, peek/commit scheduling, isolated snapshot, audio-segment delivery, effect chain, extension settings are all the same), with the interface family paralleled by the `IInstrument*` prefix, not inheriting from the voice family. **It differs substantially from voice in only three places**:

- **Notes go to full end, no de-overlap**: `IInstrumentSynthesisNote.EndTime` / `InstrumentSynthesisNoteSnapshot.EndTime` is the note's full end (`Pos+Dur`), and the host does **not** clamp it to the next note's start. `Notes` passes through the original overlappable notes (chords / polyphony), and the engine superimposes voicing itself (the reference implementation adds one waveform segment per note's pitch and mixes by sum).
- **No lyrics / no phonemes**: `IInstrumentSynthesisNote` has no `Lyric` / `Phonemes`; the session has no `DefaultLyric` and produces no `SynthesizedPhonemes`.
- **No pitch curve, product is audio only**: `IInstrumentSynthesisContext` has no `Pitch` / `PitchDeviation` (v1 voices purely by the note's integer `Pitch`); the session produces no `SynthesizedPitch`. It may still declare automation tracks and `SynthesizedParameters` readback (none if the engine declares none).

Manifest entry: `{ "type": "instrument", "engine": "MyInstrument", "name": "My Instrument", "classes": ["My.Ns.MyInstrumentEngine"], "assembly": "MyInstrument.dll" }`.

The sound-source catalog is the same shape as voice: `IInstrumentSynthesisEngine.InstrumentSourceInfos` (keyed by id). **One plugin, one instrument** = a single entry; a **container form** (e.g. Kontakt: one engine hosting multiple external resource-package instruments) scans the installed resource packages in `Init()` and fills multiple entries, with `InstrumentId` selecting the specific instrument. When there are many, implement `InstrumentSourceLayout` (isomorphic to voice's `VoiceSourceLayout`) to fold the picker into nested submenus; leave it unimplemented for a flat list.

> For the full interface contract and design rationale see [instrument-sdk-design.md](instrument-sdk-design.md); for a minimal reference implementation (one engine hosting sine/square timbres, polyphonic additive synthesis) see `tests/plugins/V1.Instrument`.

The related interfaces are all in `TuneLab.SDK`: `IInstrumentSynthesisEngine` / `IInstrumentSynthesisSession` / `IInstrumentSynthesisContext` / `IInstrumentSynthesisNote` / `InstrumentSynthesisSnapshot` / `InstrumentSynthesisNoteSnapshot` / `InstrumentSourceInfo` / `IInstrumentSynthesisPartPropertyContext` / `IInstrumentSynthesisNotePropertyContext` (the leaf types like audio / automation / status / readback are shared with voice).

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
        props.Add(("model_path", "Model Path"), TextBoxConfig.Create(""));
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
        // Then use these values in Init / CreateSession / CreateSession.
    }

    // The rest of IVoiceSynthesisEngine's members……
}
```

### 8.2 Key points

- **Secret fields**: mark them with `TextBoxConfig { IsPassword = true }`. The host masks the display accordingly and stores securely per platform: Windows uses DPAPI to store the ciphertext in place in the config file (decryptable only by the original user on the original machine); macOS stores it in the Keychain, leaving only an empty string in the config file. **When no secure storage is available, it does not save that secret field (never in plaintext) and warns**. Officially supported on Windows / macOS.
- **The schema must be reachable before Init**: `GetSettingsConfig` must not depend on state that only exists after `Init` — the user must first fill the settings panel (e.g. the model path) before you `Init`. Write it as a pure function (same input → same output, no side effects, lightweight).
- **Dynamic/conditional items**: `GetSettingsConfig(context)` is a pure function of `context.Settings` (the currently-filled values); after the user changes a value the host recomputes by the current values and diffs to the control tree, so it can show/hide fields based on the filled values (e.g. a field that appears only when some switch is on).
- **Feedback timing**: the host feeds it once after loading all extensions at startup (before `Init`), and again after the user saves the settings. Whether a setting change affects **already-running** sessions/processors (whether they need rebuilding) is decided and handled by you.
- **Localization**: the settings items' `DisplayText` is translated by you (the same paradigm as the property panel, producing text by `TuneLabContext.Global.Language`); the host does no lookup.
- **No manifest declaration needed**: the settings schema goes purely through code (`GetSettingsConfig`); `manifest.json` is not involved.

### 8.3 Where the user changes them

The "Settings" window (entered from the top menu) → "Extensions" tab: one "display name + settings panel" section per extension that declares settings. Edits are stored and fed back uniformly when **the window is closed / the tab is switched away**.

> The agent model engine has its own sidebar settings entry, not in the "Extensions" tab.

The related interfaces are in `TuneLab.SDK`: `IExtensionSettings` / `IExtensionSettingsContext` (+ the control configs `ObjectConfig` / `TextBoxConfig` / `CheckBoxConfig` / `ComboBoxConfig` / `SliderConfig`).

---

## 9. Packaging, Installation, Uninstallation

- **Package format**: zip up the package folder and change the extension to **`.tlx`**, requiring `manifest.json` at the zip's **root**.
- **Installation**: in TuneLab, drag the `.tlx` into the window, or use "Install Extension" in the extensions sidebar. Installing extracts it to the extension directory and **loads it immediately** (no restart needed).
- **Extension directory**: `%AppData%/TuneLab/Extensions/<package name>/` (Windows). "Open Extensions Folder" in the sidebar opens it directly.
- **Uninstallation**: "Uninstall" on each item in the extensions sidebar. Uninstallation is completed **after the editor closes** by a standalone `ExtensionInstaller` (deleting after file locks are released), with an option to "restart now" to take effect.

---

## 10. Loading and Validation Behavior

When TuneLab loads each package: **discover** → read `manifest.json` and **judge the generation** (has `id` = V1) → **validate** (sdk-version compatible? platform matches?) → build a **per-folder ALC** for the package → load each `assembly` in turn, **scanning the `classes` candidates to claim by the interfaces required by this `type` and instantiate & register** (no longer reflecting over attributes).

- Any step's failure **degrades gracefully**: only the problematic plugin/entry is skipped, **the host never crashes**, and the loading status is reflected in the extensions sidebar and the log.
- `sdk-version` higher than the host → that package is skipped with a notice.
- `platforms` not including the current platform → that plugin is skipped.
- Entry-level validation failure (`assembly` not found, no class in `classes` implements the interface required by this `type`, the hit class lacks a parameterless constructor) → **only that entry fails**, with the reason written into the sidebar tooltip; the rest of the package's entries load as usual (partial loading).

---

## Appendix: Legacy Plugins

Old plugins released before the overhaul (linking the old `TuneLab.Base` / `TuneLab.Extensions.Formats` / `TuneLab.Extensions.Voices`) are **Legacy**: their `manifest.json` has **no `id`** (or no such file at all). TuneLab identifies them as Legacy accordingly and hands them to the compatibility layer.

- **New plugins should not adopt the Legacy form** — always include `id` and the new-format fields, and write per this document.
- The Legacy compatibility layer will be kept long-term, so old plugins are under no forced-migration pressure; but new features (e.g. effect) are provided only in V1.
