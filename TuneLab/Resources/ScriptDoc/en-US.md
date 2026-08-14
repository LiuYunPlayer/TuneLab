# Script API Manual

Run a short JavaScript program to read and edit the current project — ideal for **bulk, looping, conditional, or computed** edits ("for every note in bars 5–8, raise it an octave and add a harmony a third above" is one loop, far less work than dozens of manual operations).

There are two entry points, sharing the same **object-style** API:

- **The "Script" side panel**: type a script on the **Code** face and click Run (or Ctrl+Enter); the output area shows `print` and the result. Read this manual on the **Doc** face.
- **The built-in AI Agent**: the model writes scripts against the same API automatically.

The global object `tl` is the **editor**; the project data hangs off `tl.currentProject()`. Tracks/parts/notes/vibratos are **object handles** with their own fields and methods.

---

## Core model (read this first)

- **Object-style, two shapes.** One rule of thumb:
  - **Bare property** = a single **scalar field** you can read and write: `n.pitch`, `n.pitch += 12`, `track.isMute = true`.
  - **Method with `()`** = a **query, create, delete, or compute**: `part.notes()`, `track.addPart({...})`, `part.removeNote(n)`.
- **Create and delete both hang off the parent.** `project.addTrack()` / `removeTrack(track)`, `track.addPart()` / `removePart(part)`, `part.addNote()` / `removeNote(note)`, `part.addVibrato()` / `removeVibrato(vibrato)`. (There is no `x.remove()` — the parent owns its children both ways.)
- **The whole run is one undoable change.** Every edit folds into a single commit; `Ctrl+Z` reverts it all in one step. Assigning a field or calling a write method takes effect **immediately**, but you **don't** (and can't) commit or save yourself.
- **Get a handle, use it right away.** A handle is an opaque reference to one object, with readable/writable scalar fields and methods, but no id.
  - Collection methods (`project.tracks()`, `track.parts()`, `part.notes()`, `part.vibratos()`) return a plain **array** — iterate with `for-of` or index, has `.length`; each call is a **new snapshot**, so store it in a variable if you use it more than once. It is **not** a linked list — no `.first` / `.next`.
  - A handle is **valid only for the current run** (objects have no persistent id and are lost when the app closes): **never write a handle literal** — always get it and use it on the spot. After `removeX` a handle stays readable (see "Move vs copy" below), it just can't be written.
- **Coordinates are always absolute ticks.** All positions/durations are absolute (global) ticks (`tl.ppq` is ticks per quarter note, default 480) — the same coordinate system as the playhead and bars. You **never** do any conversion.
- **Pitch is a MIDI number**, 60 = C4 (fractional values for cents).
- **On error, everything rolls back.** If the script throws partway, all changes it made are undone (the project is left unchanged) and the error is returned — so you fix the script and re-run, never patching from a half-applied state.
- **Debug output.** `print(x)` / `console.log(x)` is collected and shown in the output area below.

### Info objects — full-fidelity copying, and creating with any field set

Every handle has `getInfo()`, which returns a **plain JS object** holding **everything** about it — nested, too: a part's info carries its sound source, notes, pitch line, automation curves, vibratos, effect chain, both levels of properties, and phonemes. Every parent's `addX(info)` takes that same shape back. So:

- **Copy** (full fidelity, nothing dropped): `track.addPart(otherPart.getInfo())`, `project.addTrack(otherTrack.getInfo())`, `part.addNote(n.getInfo())`
- **Create** with whichever fields you want: `part.addNote({pos, dur, pitch, lyric, pronunciation, properties, bodyPhonemes})`

An info is **pure data**: mutate it freely before adding it (no undo entry, no side effect), and add the same info as many times as you like (each add makes a **new** object).

> ⛔ **Never hand-copy an object field by field to "duplicate" it** — you will silently drop the sound source, curves, effects, properties and phonemes. It looks like it worked, but only the skeleton survives. To copy, use `getInfo()` → `addX()`.

Fields you omit from an info fall back to the **stored default** (e.g. `name` becomes an empty string); nothing is invented for you.

> ⚠️ **To place a copy somewhere else, move the handle — not the info:**
> ```js
> const copy = track.addPart(p.getInfo());
> copy.pos += 4 * tl.ppq;      // ✅ moves the whole part, content follows
> ```
> Every tick in an info is **absolute**, including the notes and curve points nested inside a part info.
> So bumping `info.pos` alone slides the part's **window** while its content stays at the old absolute
> ticks (the content ends up outside the window). "Move the part and its content together" is what
> assigning `part.pos` on the **handle** means; editing an info can't give you that.

### Move vs copy — `removeX` returns a **detached** handle

`removeX(child)` only **detaches** the child from its parent and **returns** its handle; the object is still alive and still readable (`getInfo()` works) — it simply has no parent for the moment.

- **Delete** = remove and don't put it back.
- **Move** = remove, then `insertX(child)` — it's the **same object**, so its notes/curves/effects/phonemes travel with it, and undo sees a single move.

A detached handle is **read-only**: assigning a field throws and tells you to insert it back first.

`removeX` is the only one that hands something back (like the DOM's `parent.removeChild(child)`), so a move is a single expression: `b.insertPart(a.removePart(p))`. `insertX` returns nothing — echoing back the handle you just passed in carries no information.

Removing a child that is **not this parent's child** *throws* — that is a programming error, not a query, so there is no boolean "was it there" result. (`Set.delete` and friends return `bool` because in a value collection "not present" is a legitimate outcome; parent-child ownership is not.)

Only a **part** can change parent: `track.removePart(p)` then `otherTrack.insertPart(p)` **moves it across tracks**. A note/vibrato/effect belongs to the part it was created on, and `insertX` only puts it back on that same part; to get one onto another part, go through an info: `otherPart.addX(x.getInfo())`.

---

## `tl` (the editor)

Editor-level entry points — a system constant, the current project, and the editor's transient state.

| Member | Returns | Notes |
|---|---|---|
| `tl.ppq` | number | Ticks per quarter note (default 480). |
| `tl.language` | string | The current UI culture code (e.g. `"zh-CN"` / `"en-US"`). Use it to return a localized tool name from `getScriptInfo`, or to localize dialog text in an action; unrelated to the project, readable even with none open. |
| `tl.currentProject()` | `project` | The current project (your data entry point; see below). |
| `tl.currentPart()` | `part \| null` | The MIDI part open in the piano editor. |
| `tl.selectedParts()` | `[part]` | The parts currently selected in the arrangement (across all tracks, multi-select); empty when none. Right-clicking a part always selects it, so this is the target entry point for `part` / `partContent` tool scripts. |
| `tl.selectedTracks()` | `[track]` | The currently selected tracks (multi-select); empty when none. Right-clicking a track header or an empty lane always selects that track, so this is the target entry point for `track` / `trackContent` tool scripts. |
| `tl.trackSelection()` | `{startTick, endTick, startTrackNumber, endTrackNumber} \| null` | The arrangement **range selection** — a tick×track area dragged out in the arranger (Shift+drag); track numbers 1-based and contiguous; `null` when there is none. **Orthogonal** to `selectedParts`/`selectedNotes` (selected *objects*): it marks a *place*, not objects, so use it to bulk-process whatever falls inside. |
| `tl.pianoSelection()` | `{startTick, endTick} \| null` | The piano-editor **range selection** — a tick band dragged out in the piano window (note area or parameter lane) via Shift+drag, within the current part and spanning all pitches; time-only (no track, no pitch); `null` when there is none. Coexists independently with `trackSelection()`; use it to bulk-process whatever falls in that time span of the current part. |
| `tl.playhead()` | `{tick, seconds, bar, beat, playing}` | Playhead position (bar/beat are 1-based). |
| `tl.snap(tick)` | number | Snap an absolute tick to the editor's grid. |

---

## `project` — `tl.currentProject()`

The project's data: tracks, tempo, time signatures.

| Member | Returns | Notes |
|---|---|---|
| `project.tracks()` | `[track]` | All track handles. |
| `project.addTrack(info?, index?)` | `track` | Create a track from a track info and insert it at 0-based `index`; omit `info` for a blank track, omit `index` to append. Returns its handle. |
| `project.insertTrack(track, index?)` | — | Put a **detached** track back at `index` (this is how you reorder; the object keeps its identity). |
| `project.removeTrack(track)` | `track` | Detach the track from the project and hand its (now detached) handle back: don't put it back = delete. |
| `project.importTracks(path)` | `[track]` | Import **all** tracks from a file into this project (additive), returning the newly added track handles. `path` is a local file path; formats are `tlp`/`tlpx`/`mid`/`midi` plus any installed format plugins. Each track brings its parts/notes/sound source/effects/automation (a missing sound source degrades to none, as in the UI). **Tempo:** the project's current tempo/time-signature is kept and tracks land at their **raw ticks** (bar-aligned, no time-remap) — the predictable additive default; tempo-align / import-tempo modes may come later. A missing/unsupported/unparseable file throws (and the whole script rolls back). |
| `project.tempos()` | `[{bpm, tick}]` | All tempo markers. |
| `project.timeSignatures()` | `[{numerator, denominator, bar}]` | All time-signature markers (bar is 1-based). |
| `project.setTempo(bpm, atTick?)` | — | Set tempo; if `atTick` is omitted, sets the base tempo at tick 0 (edits an existing marker there, else adds one). |
| `project.setTimeSignature(numerator, denominator, atBar?)` | — | Set time signature; `atBar` is a 1-based bar number (default 1). |
| `project.removeTempo(atTick)` | — | Remove the tempo marker at `atTick` (the dual of `setTempo`). **Throws** if there is no marker there rather than silently doing nothing; the one at the project start is the base tempo and can't be removed — change it with `setTempo`. |
| `project.removeTimeSignature(atBar)` | — | Remove the time-signature marker at bar `atBar` (1-based). Same rules as `removeTempo`. |

### Export settings (fields on `project`, plus `track.exportEnabled` / `track.exportChannels`)

| Field | Type | Notes |
|---|---|---|
| `project.exportPath` | string | Directory to export into. |
| `project.exportFileName` | string | File name (without extension). |
| `project.exportFormat` | string | `"wav"` / `"mp3"` / `"flac"` / `"ogg"`. An unknown value **throws** (no silent fallback). |
| `project.exportSampleRate` | number | Sample rate in Hz. |
| `project.exportBitDepth` | number | Bit depth; only the lossless formats (wav/flac) use it. |
| `project.exportBitrate` | number | Target bitrate in kbps; only the lossy formats (mp3/ogg) use it. |
| `project.masterExportEnabled` | bool | Whether to export the master output. |
| `project.masterExportChannels` | number | Master channel count: 1 = mono, 2 = stereo. |
| `track.exportEnabled` | bool | Whether to include this track when exporting. |
| `track.exportChannels` | number | This track's channel count: 1 or 2. |

This family is on the script surface precisely so that "run a script that sets every export option to my preset" works as a **reusable command** (which you can also bind to a shortcut).

> ⚠️ **These are *settings*, and they do not go on the undo stack.** Just like changing them in the export
> panel: after the script runs, `Ctrl+Z` will **not** put the old export path back (the undo stack holds
> project data only). "The whole run is atomic" still holds, though — if the script **throws**, or is run as a
> **preview**, the host **restores** them.
> Also: these only say *what* to export with; actually **writing the audio file** is a separate thing
> (the agent's `export_project` tool) and is not part of the script surface.

---

## `track`

**Fields** (bare properties, read/write): `name`, `isMute`, `isSolo`, `gain` (in dB, 0 = unity), `pan` ([-1, 1]), `asRefer` (whether other sound sources may "hear" this track as a reference), `color` (hex string like `"#FF8800"`; empty = theme default).

**Export settings** (read/write): `exportEnabled` (include this track when exporting), `exportChannels` (1 = mono, 2 = stereo). These are **settings** — see the note in the `project` section: they do not go on the undo stack.

| Method | Returns | Notes |
|---|---|---|
| `track.getInfo()` | info | A full snapshot of this track (pure data): `{name, gain, pan, mute, solo, asRefer, color, parts:[part info]}`. Feed it to `project.addTrack(info)` to duplicate the whole track. The export switches are **deliberately not included** — they are settings, not part of the track's content, so a duplicated track gets the defaults (copy them explicitly with `dst.exportEnabled = src.exportEnabled` if you want them carried over). |
| `track.parts()` | `[part]` | All part handles on this track (sorted by start). |
| `track.addPart(info)` | `part` | Create a part from a part info (geometry below, plus the midi/audio content fields), returns its handle. |
| `track.insertPart(part)` | — | Put a **detached** part on this track — the track **may be a different one**, which is how you **move a part across tracks** (identity preserved, so its sound source/notes/curves/effects/phonemes travel with it). |
| `track.removePart(part)` | `part` | Detach the part from this track and hand its (now detached) handle back: don't put it back = delete; put it on another track = move. |

> `parts` sorts itself by start position, so `addPart`/`insertPart` take no `index` — the position follows from `pos`.

---

## `part`

### Geometry: three raw fields (writable), three derived ones (read-only)

Same model as the data layer:

| Field | Access | Meaning |
|---|---|---|
| `pos` | read/write | The anchor's absolute tick — **and the origin every bit of content (notes/curves/vibratos) is measured from**, so assigning `pos` **moves the whole part** (content follows, length unchanged). |
| `startOffset` | read/write | Left edge relative to the anchor: `>0` trims the front, `<0` extends it. |
| `endOffset` | read/write | Right edge relative to the anchor (dragging the right edge is exactly this). |
| `startPos` | read-only | `= pos + startOffset` |
| `endPos` | read-only | `= pos + endOffset` |
| `dur` | read-only | `= endOffset - startOffset` |

So "an empty part covering ticks 1920..3840" is `track.addPart({ pos: 1920, endOffset: 1920 })`.

**Other fields** (read/write): `name`, `gain` (dB, part-level, adds to the track's); **read-only**: `type` (`"midi"`/`"audio"`).

| Method | Returns | Notes |
|---|---|---|
| `part.getInfo()` | info | A full snapshot of this part (pure data): `{type, name, pos, startOffset, endOffset, gain, soundSource, notes, vibratos, effects, automations, piecewiseAutomations, pitch, properties}`; an audio part is `{type:"audio", name, pos, startOffset, endOffset, path}`. Feed it to `track.addPart(info)` to duplicate the whole part. |
| `part.track()` | `track` | The track this part is on (read-only — to change it, `removePart` then `insertPart` on the other track). This is how you get from `tl.selectedParts()` / `tl.currentPart()` up to a track. |
| `part.soundSource()` | `{type, id, name, kind, defaultLyric}` | The part's sound source info (read-only snapshot); `kind` is `"voice"` or `"instrument"`. MIDI parts only. |
| `part.setSoundSource({kind, type, id})` | — | Switch the part's sound source (`kind` = `"voice"` (default) or `"instrument"`; `type`/`id` from `list_sound_sources`). An unknown source errors rather than silently clearing; empty `type`+`id` clears it to no source. MIDI parts only. |
| `part.notes()` | `[note]` | All note handles in this MIDI part. |
| `part.selectedNotes()` | `[note]` | Notes currently selected in the piano editor (empty if none). |
| `part.addNote(info)` | `note` | Add a note from a note info: `{pos, dur, pitch, lyric?, pronunciation?, properties?, leadingPhonemes?, bodyPhonemes?, bodyOffset?}` (pos absolute ticks, pitch MIDI). Returns its handle. |
| `part.insertNote(note)` | — | Put a **detached** note back on this part (identity preserved). A note belongs to the part it was created on and can't change parent — for another part use `otherPart.addNote(n.getInfo())`. |
| `part.removeNote(note)` | `note` | Detach the note from this part and hand its (now detached) handle back: don't put it back = delete. |
| `part.samplePitch(startTick, endTick, samples)` | `[number]` | Evenly sample the final pitch curve (MIDI scale) over the range. |
| `part.setPitchLine(startTick, endTick, points)` | — | Clear `[start, end)` then lay a pitch line; `points = [{tick, value}]`, value = absolute MIDI pitch (fractional ok). |
| `part.clearPitch(startTick, endTick)` | — | Clear a span of the pitch curve. |
| `part.automationIds()` | `[string]` | Editable **continuous** automation ids declared by the sound source (e.g. `"Volume"`; they have a baseline default). Pitch is separate, and piecewise tracks are listed on their own. |
| `part.sampleAutomation(id, startTick, endTick, samples)` | `[number]` | Evenly sample an automation curve; `NaN` = no curve there. |
| `part.setAutomation(id, startTick, endTick, points, defaultValue?)` | — | Clear then lay an automation curve; value = absolute parameter value; created on demand, `defaultValue` optional. |
| `part.clearAutomation(id, startTick, endTick)` | — | Clear a span of an automation curve. |
| `part.piecewiseAutomationIds()` | `[string]` | Editable **piecewise** automation ids declared by the sound source. Piecewise tracks have no baseline: the gaps between segments mean *no value* — the same family the pitch line belongs to. The two families read and write differently, hence two id lists: whatever you get from either one works with that family's methods. |
| `part.samplePiecewiseAutomation(id, startTick, endTick, samples)` | `[number]` | Evenly sample a piecewise track. |
| `part.setPiecewiseAutomationLine(id, startTick, endTick, points)` | — | Clear `[start, end)` then lay a piecewise curve (same shape as `setPitchLine`). |
| `part.clearPiecewiseAutomation(id, startTick, endTick)` | — | Clear a span of a piecewise curve. |
| `part.lockPitch(startTick?, endTick?)` | `bool` | **Lock the synthesized pitch**: write the engine's pitch output, at its real values, into this part's pitch curve (the same thing the lock brush does in the editor). The range arguments come in a **pair — pass both or neither** (neither = the whole part). |
| `part.lockAutomation(id, startTick?, endTick?)` | `bool` | **Lock a synthesized parameter**: write that track's engine-produced curve into the editable track with the same id (continuous vs piecewise is dispatched for you from the declaration). An unknown id, or a track with no paired synthesized parameter, **throws** (which rolls the whole run back) — call `hasSynthesizedParameter(id)` first if unsure. |
| `part.hasSynthesizedParameter(id)` | `bool` | Whether that track has a **paired synthesized parameter** — i.e. besides the editable track, the sound source also publishes a synthesized parameter with the same id. No pairing, nothing to lock. |
| `part.vibratos()` | `[vibrato]` | All vibrato handles in this part. |
| `part.addVibrato(info)` | `vibrato` | Add a vibrato from a vibrato info (overlaid on the pitch curve): `{pos, dur, frequency?(6), amplitude?(1), phase?(0), attack?(0.2), release?(0.2), affectedAutomations?, affectedEffectAutomations?}`. Returns its handle. |
| `part.insertVibrato(vibrato)` | — | Put a **detached** vibrato back on this part (identity preserved). Like notes, it can't change parent. |
| `part.removeVibrato(vibrato)` | `vibrato` | Detach the vibrato and hand its (now detached) handle back. |
| `part.effects()` | `[effect]` | The serial effect chain on this part, in processing order. |
| `part.addEffect(info, index?)` | `effect` | Create an effect from an effect info and insert it at 0-based `index` in the chain; omit `index` to append. `info.type` is required and must be an effect engine id that exists (see `list_effects`); an unknown type errors. Returns its handle. |
| `part.insertEffect(effect, index?)` | — | Put a **detached** effect back into the chain at `index` (identity preserved, so its automation curves and the vibrato amplitude table still point at it). |
| `part.removeEffect(effect)` | `effect` | Detach the effect from the chain and hand its (now detached) handle back. |
| `part.moveEffect(effect, index)` | — | Move an effect to a 0-based position in the chain. |
| `part.getProperty(key)` | value | The current value of one voice/instrument-declared per-part parameter (`number`/`boolean`/`string`), or `null` if unset. Keys, ranges and defaults come from `list_sound_sources`. |
| `part.setProperty(key, value)` | — | Set one declared per-part parameter (`value` = `number`/`boolean`/`string`). |

### About locking (`lockPitch` / `lockAutomation`)

Engine output is always **read-only** and user edits always land in the data layer; locking is the one explicit bridge between them — once locked, that stretch of data is **yours**: keep editing it, and the engine no longer overwrites it. It is how you keep the model's line and change only part of it: without it, drawing over an uncovered stretch starts from blank and every detail the model produced is lost.

The return value says **whether anything was actually locked**: `false` means there was no output in that range (usually: it hasn't been synthesized yet) — a no-op, not an error, so **check it** rather than assuming success. Values are written as-is and are *not* clamped to the target track's range (clamping would silently alter data).

Locking is deliberately a **one-shot action**, never a live link: re-synthesizing later does not update what you locked (auto-following would form a cover → re-synthesize → the synthesized parameter changes → write again feedback loop, and would mix engine results into the undo stack). It is the same paradigm as `note.lockPhonemes` on the phoneme side (engine output read-only, user data writable, locking the one explicit bridge), which is why the script surface uses the one verb *lock* for both.

```js
// Lock this part's model pitch into an editable curve, plus any parameter track that has a synthesized parameter
const p = tl.currentPart();
if (!p.lockPitch()) print('no synthesized pitch to lock yet — synthesize first');
for (const id of p.automationIds()) {
  if (p.hasSynthesizedParameter(id)) p.lockAutomation(id);
}
```

---

## `note`

**Fields** (bare properties, read/write): `pos`, `dur`, `pitch`, `lyric`, `pronunciation`; **read-only**: `pitchName` (e.g. `"C4"`), `hasLockedPhonemes` (bool). `pronunciation` is an explicit voice pronunciation override — set it to force a pronunciation; an empty string means no override, so the lyric text itself reaches the engine and the engine does its own G2P. (Whether entering a lyric auto-fills this field with editor G2P is the `AutoGeneratePronunciation` setting.) `bodyOffset` (seconds) is read/write (the leading/body junction offset from the note start; writing it auto-locks).

| Method | Returns | Notes |
|---|---|---|
| `note.getInfo()` | info | A full snapshot of this note (pure data): `{pos, dur, pitch, lyric, pronunciation, properties, leadingPhonemes, bodyPhonemes, bodyOffset}`. Feed it to `part.addNote(info)` to copy it. |
| `note.part()` | `part` | The part this note is on (read-only; the data layer doesn't allow changing it). `vibrato.part()` / `effect.part()` work the same way. |
| `note.getProperty(key)` | value | The current value of one voice/instrument-declared per-note parameter (`number`/`boolean`/`string`), or `null` if unset. Keys, ranges and defaults come from `list_sound_sources`. |
| `note.setProperty(key, value)` | — | Set one declared per-note parameter (`value` = `number`/`boolean`/`string`). |
| `note.phonemes()` | `[phoneme]` | The note's phonemes (leading ++ body, in time order); empty until the note has been synthesized. Voice parts only. |
| `note.addLeadingPhoneme(info)` | `phoneme` | Append a phoneme to the **leading** list (pre-vowel consonants); auto-locks. `info = {symbol, duration?(seconds, default 0), stretchWeight?(default 0), properties?}`, where `stretchWeight` 0 = rigid consonant / >0 = stretchable vowel. |
| `note.addBodyPhoneme(info)` | `phoneme` | Append a phoneme to the **body** list (vowel + coda); same argument. |
| `note.removePhoneme(phoneme)` | — | Remove a phoneme; auto-locks. Phonemes have no parent pointer in the data layer, so to get one onto another note go through an info: `otherNote.addBodyPhoneme(ph.getInfo())` then `removePhoneme(ph)`. |
| `note.lockPhonemes()` | — | Fix the synthesized phonemes as editable user data (idempotent; usually automatic on the first phoneme write). Same verb and same meaning as `part.lockPitch` / `lockAutomation`, scoped to this note's phonemes. |
| `note.clearPhonemes()` | — | Drop the locked phonemes and revert to the synthesized ones (the counterpart of `clearPitch` / `clearAutomation` on the curve side). |

Phonemes come from the engine (read-only) until you edit them; the first write **auto-locks** them into editable data (exactly like the sidebar's first phoneme edit).

---

## `phoneme`

An item in `note.phonemes()`. **Fields** — read-only: `leading` (bool; leading = pre-vowel consonants, body = vowel+coda); read/write: `symbol`, `duration` (seconds), `stretchWeight` (0 = rigid consonant, >0 = stretchable vowel — its duration is a derived fill, ignored by layout). Writing any field auto-locks the note's phonemes.

A phoneme handle is **positional**: its list index shifts when phonemes are added or removed, so re-fetch `note.phonemes()` after a structural change.

| Method | Returns | Notes |
|---|---|---|
| `phoneme.getInfo()` | info | A full snapshot of this phoneme (pure data): `{symbol, duration, stretchWeight, properties}` (`properties` is `null` while the note isn't locked). |
| `phoneme.getProperty(key)` | value | The current value of one voice-declared per-phoneme parameter (`number`/`boolean`/`string`), or `null` if unset or the note is not yet locked. Keys/ranges come from the phoneme slots in `list_sound_sources`. |
| `phoneme.setProperty(key, value)` | — | Set one declared per-phoneme parameter (`value` = `number`/`boolean`/`string`); auto-locks. |

---

## `vibrato`

**Fields** (bare properties, read/write): `pos`, `dur` (absolute ticks), `frequency` (Hz), `amplitude` (semitones), `phase` (in units of π), `attack`, `release` (seconds).

| Method | Returns | Notes |
|---|---|---|
| `vibrato.getInfo()` | info | A full snapshot of this vibrato (pure data, both amplitude tables included): `{pos, dur, frequency, amplitude, phase, attack, release, affectedAutomations, affectedEffectAutomations}`. |
| `vibrato.affectedAutomations()` | `{automationId: amplitude}` | Read-only snapshot of how much this vibrato modulates each **sound-source-level** parameter track. |
| `vibrato.affectedEffectAutomations()` | `{effectId: {automationId: amplitude}}` | The same for **effect-level** tracks. The outer key is `effect.id` (a stable instance identity, not a chain position), so reordering the effect chain never scrambles this table. |
| `vibrato.setAmplitude(id, amplitude, effect?)` | — | Set the modulation amplitude for one track (creating the association if there wasn't one). Omit `effect` for a sound-source-level track; pass an effect handle (on the same part) to target that effect's track. |
| `vibrato.removeAmplitude(id, effect?)` | — | Drop the association for one track (the dual of `setAmplitude`). |

---

## `effect`

An item in `part.effects()`. **Fields** — read/write: `isEnabled` (bool; `false` = bypass); **read-only**: `type` (engine id), `name` (display name), `id` (stable instance id), `index` (0-based position in the chain).

| Method | Returns | Notes |
|---|---|---|
| `effect.getInfo()` | info | A full snapshot of this effect (pure data, parameters and automation curves included): `{id, type, isEnabled, automations, piecewiseAutomations, properties}`. Feed it to `part.addEffect(info)` to copy it (dropped into the same chain, the `id` is reissued so the copy doesn't collide with the source). |
| `effect.getProperty(key)` | value | The current value of one parameter (`number`/`boolean`/`string`), or `null` if unset. Keys, ranges and defaults come from `list_effects`. |
| `effect.setProperty(key, value)` | — | Set one parameter (`value` = `number`/`boolean`/`string`). |
| `effect.automationIds()` | `[string]` | The automatable parameter ids declared by this effect's engine (see `list_effects`). |
| `effect.sampleAutomation(id, startTick, endTick, samples)` | `[number]` | Evenly sample one of this effect's automation curves; `NaN` = no curve there. |
| `effect.setAutomation(id, startTick, endTick, points, defaultValue?)` | — | Clear `[start, end)` then lay a curve on this effect; `points = [{tick, value}]`, value = absolute parameter value; created on demand, `defaultValue` optional. Same shape as `part.setAutomation`, but scoped to this effect. |
| `effect.clearAutomation(id, startTick, endTick)` | — | Clear a span of one of this effect's automation curves. |
| `effect.piecewiseAutomationIds()` | `[string]` | The **piecewise** parameter track ids declared by this effect's engine (no baseline, gaps between segments). |
| `effect.samplePiecewiseAutomation(id, startTick, endTick, samples)` | `[number]` | Evenly sample one of this effect's piecewise tracks. |
| `effect.setPiecewiseAutomationLine(id, startTick, endTick, points)` | — | Clear `[start, end)` then lay a piecewise curve. |
| `effect.lockAutomation(id, startTick?, endTick?)` | `bool` | Lock one of this effect's synthesized parameters (exactly `part.lockAutomation`, scoped to this effect). |
| `effect.hasSynthesizedParameter(id)` | `bool` | Whether that track of this effect has a paired synthesized parameter. Always `false` while detached (synthesized parameters are addressed by chain index, and a detached effect has no place in the chain). |
| `effect.clearPiecewiseAutomation(id, startTick, endTick)` | — | Clear a span of one of this effect's piecewise curves. |

Effect automation mirrors part-level automation exactly — same absolute-tick `points` and value semantics, same split into continuous and piecewise families — only the target differs (an effect in the chain rather than the sound source).

---

## Examples

**Raise every note in the current part an octave, and add a harmony a third above each:**
```js
const part = tl.currentPart();
for (const n of part.notes()) {
  const info = n.getInfo();   // a full copy of the note (properties and phonemes included)
  info.pitch += 4;            // an info is pure data — edit it freely
  part.addNote(info);         // third above
  n.pitch += 12;              // original up an octave
}
print("processed " + part.notes().length + " notes");
```

**Operate on selected notes only (double their duration):**
```js
const part = tl.currentPart();
for (const n of part.selectedNotes()) n.dur *= 2;
```

**Duplicate the first track into a new one, an octave up:**
```js
const project = tl.currentProject();
const info = project.tracks()[0].getInfo();   // everything: sound source, curves, effects, properties, phonemes
info.name = "Harmony +8";
const dst = project.addTrack(info);
for (const p of dst.parts())
  for (const n of p.notes()) n.pitch += 12;
```
> Copy with `getInfo()`, then edit the copy. Rebuilding a track with `addPart` + `addNote` copies only
> `pos`/`dur`/`pitch`/`lyric` and **silently loses** the sound source, pitch line, automation curves, vibratos,
> effect chain, part/note properties and phonemes — it looks like it worked, but only the note skeleton survives.

**Move the first part of track 1 onto track 2 (a move, not a copy):**
```js
const [a, b] = tl.currentProject().tracks();
const p = a.parts()[0];
a.removePart(p);   // p is now detached: readable, not writable
b.insertPart(p);   // same object, now on track b — sound source/curves/effects/phonemes came along
```

**Shift a part two bars later (4/4):**
```js
const p = tl.selectedParts()[0];
p.pos += 2 * 4 * tl.ppq;   // moving the anchor moves the whole part; content follows, length unchanged
```

**Draw a volume crescendo over a range:**
```js
const part = tl.currentPart();
const a = 0, b = 4 * 4 * tl.ppq; // first 4 bars (4/4)
part.setAutomation("Volume", a, b, [{tick: a, value: 0.2}, {tick: b, value: 1.0}]);
```

**Delete every note below C2:**
```js
const part = tl.currentPart();
for (const n of part.notes()) if (n.pitch < 36) part.removeNote(n);
```

---

## Tool scripts — save them, put them in menus, bind shortcuts

Everything above is a **run-once script**: hit Run and you're done. Add a `getScriptInfo()` and the script becomes a **tool** — once saved to your script library it shows up in menus, can be bound to a shortcut, and can be reused.

```js
function getScriptInfo() {
  return { name: "Add Third Harmony", context: "note" };
}
function main() {                      // the action; the body is exactly like a run-once script
  const p = tl.currentPart();
  for (const n of p.selectedNotes())
    p.addNote({ pos: n.pos, dur: n.dur, pitch: n.pitch + 4, lyric: n.lyric });
}
```

- A script **without `getScriptInfo`** belongs to the Script side panel only and never appears in menus.
- **`main()` as a whole is one undoable change**; on any error everything rolls back (same rule as a run-once script).
- To follow the UI language, branch on `tl.language`: `name: tl.language === 'zh-CN' ? '加三度和声' : 'Add Third Harmony'`.

### Fields of `getScriptInfo()`

| Field | Required | Meaning |
|---|---|---|
| `name` | ✔ | The name shown in menus. |
| `context` | | Decides **where it appears and what it targets**, and also which area its shortcut is active in. Defaults to `'global'`. See the table below. |
| `id` | | A **stable anchor** for remembering the user's shortcut and settings. Allowed chars: `A-Z a-z 0-9 . _ -`. **Never change it after publishing**; omit it and the filename becomes the id — renaming then drops the user's binding. |
| `defaultGesture` | | A suggested shortcut such as `'mod+shift+k'` (`mod` = Cmd on macOS / Ctrl on Windows; you may also write `ctrl`/`cmd`/`alt`/`shift`). Applied **only if that key is free, and it never overrides a built-in**; users can rebind it in Settings. |

Values for `context`:

| `context` | Where it appears | Target |
|---|---|---|
| `'global'` | Top Scripts menu | `tl.currentPart()` or the whole project |
| `'note'` | Piano roll, **right-click on a note** | `tl.currentPart().selectedNotes()` (the clicked note is always selected) |
| `'partContent'` | Piano roll, right-click on **blank space** | the content of `tl.currentPart()` |
| `'pianoSelection'` | Piano roll, **right-click on the range selection** | `tl.pianoSelection()` (a tick band; `null` when there is none) |
| `'part'` | Arrangement, **right-click on a part** | `tl.selectedParts()` (possibly several) |
| `'track'` | **Right-click on a track header** | `tl.selectedTracks()` (possibly several) |
| `'trackContent'` | Arrangement, right-click on a track's **blank lane** | `tl.selectedTracks()` |
| `'trackSelection'` | Arrangement, **right-click on the range selection** | `tl.trackSelection()` (tick × track; `null` when there is none) |

The shortcut's active area follows the context: `piano*` fires only in the piano roll, the arrangement ones only in the arrangement, `global` anywhere in the editor. **When triggered by a shortcut there is no "the one you right-clicked"** — the target is simply the current selection, so `main()` should do nothing when the selection is empty.

### `getInputConfig(ctx)` — ask for parameters before running

Add a `getInputConfig` and the host shows a form before running `main`, then hands the filled values to `main(inputs)`:

```js
function getScriptInfo() { return { name: "Transpose", context: "note" }; }
function getInputConfig(ctx) {
  return { semitones: SliderConfig.integer(12, -24, 24) };   // key = the field's label
}
function main(inputs) {
  for (const n of tl.currentPart().selectedNotes()) n.pitch += inputs.semitones;
}
```

It returns a **map of `key → config`** (not plain data). The key is the field's label in the form; build the value with the constructors below.

**Don't mix up the two conventions** (the easiest thing to get wrong):

| | Content |
|---|---|
| `ctx.values` in `getInputConfig` | **Sparse** — only keys the user actually **changed** are present; untouched ones read `undefined`. So always supply a fallback here: `const mode = ctx.values.mode ?? 'transpose'` |
| `inputs` in `main` | **Complete** — every field you declared is present (the user's value, or that config's default). Read it directly, no presence check needed |

**Conditional fields**: `getInputConfig` is **re-run after every change**, so just branch on `ctx.values` to add or drop fields:

```js
function getInputConfig(ctx) {
  const mode = ctx.values.mode ?? 'transpose';
  const cfg = { mode: ComboBoxConfig.create(['transpose', 'setPitch']) };
  if (mode === 'transpose') cfg.semitones = SliderConfig.integer(12, -24, 24);
  else                      cfg.targetPitch = SliderConfig.integer(60, 0, 127);
  return cfg;
}
```

**Side-effect-free rule**: `getInputConfig` is called repeatedly (when the form opens and on every change), so it may only **declare** — never act. All real edits belong in `main`. Reading the project as context is fine here (`tl.currentPart()`, `selectedNotes()`, …); changing it is not.

### Input control constructors

Method names mirror the control types; each `withX(...)` returns a new config, so they chain.

| Constructor | Meaning |
|---|---|
| `SliderConfig.linear(default, min, max)` | Slider (continuous) |
| `SliderConfig.integer(default, min, max)` | Slider (integer) |
| `SliderConfig.create(default, scale)` | Slider with a custom scale — see below |
| ↳ `.withFormat(fmt)` `.withMinLabel(s)` `.withMaxLabel(s)` `.withRandomizable()` | Number formatting / end labels / allow randomizing |
| `DraggableNumberBoxConfig.create(default?)` `.integer(default?)` | Draggable number box |
| ↳ `.withMin(x)` `.withMax(x)` `.withRange(a,b)` `.withStep(s)` `.withSensitivity(s)` `.withFormat(fmt)` `.withRandomizable()` | Range / step / drag sensitivity / format / allow randomizing (needs both bounds) |
| `ComboBoxConfig.create(['a','b'])` or `.create()` | Dropdown. **The default is the VALUE itself, not an index** |
| ↳ `.append(x)` `.appendSeparator(label?)` `.withDefault('a')` | Add an item / a separator / set the default |
| `CheckBoxConfig.create(default?)` | Check box |
| `TextBoxConfig.create(default?)` | Text box |
| ↳ `.withPassword()` | Password style (content masked) |

**Scales and formats** (arguments of `SliderConfig.create` / `.withFormat`):

| | Meaning |
|---|---|
| `NormalizedScale.linear(min, max)` `.integer(min, max)` | Linear / integer scale |
| `NormalizedScale.rounded(s)` `.floor(s)` `.ceil(s)` | Round an existing scale |
| `NormalizedScale.custom(p => value, value => p)` | **Custom scale**: two inverse functions where `p` is a 0..1 position — this is how you get a log/exp axis |
| `NumberFormat.decimals(n)` | Fixed decimal places |
| `NumberFormat.custom(v => string, s => number or null)` | **Custom display/parse**, e.g. for units; return `null` when parsing fails |

Those custom functions are called **live** while the form is open, so keep them pure and cheap. If one throws or returns something invalid it degrades safely — it never throws into the UI.

```js
// Frequency slider: 20Hz–20kHz on a log axis, displayed with units
function getInputConfig(ctx) {
  return {
    freq: SliderConfig.create(1000, NormalizedScale.custom(
        p => 20 * Math.pow(1000, p),                // 0..1 -> 20..20000
        v => Math.log(v / 20) / Math.log(1000)))    // and back
      .withFormat(NumberFormat.custom(
        v => v >= 1000 ? (v / 1000).toFixed(2) + " kHz" : v.toFixed(0) + " Hz",
        s => { const m = /^([\d.]+)\s*k?/i.exec(s.trim()); return m ? parseFloat(m[1]) * (/k/i.test(s) ? 1000 : 1) : null; }))
  };
}
```

---

## Notes

- **Handles can't be hard-coded or reused across runs.** Always get one and use it on the spot.
- **Collection methods return arrays, not linked lists.** Use `for-of` / index; there is no `.first` / `.next`. Each call is a new snapshot — store it if you reuse it.
- **Create and delete both go through the parent** (`track.addPart`/`removePart`, `part.addNote`/`removeNote`, …) — there is no `x.remove()`.
- **To copy, use `getInfo()` → `addX(info)`**, never a field-by-field rebuild (that silently drops the sound source, curves, effects, properties and phonemes).
- **The handle `removeX` returns is detached: readable, not writable.** Assigning to it throws and tells you to insert it back. Only a part can change parent (across tracks).
- **Changing `pos`/`dur` may change ordering.** Parts/notes/vibratos keep start order — handle addressing is unaffected (still the same object), but if you are iterating that collection at the same time, remember you hold the array snapshot taken when iteration began.
- **Notes live inside a MIDI part.** To write a melody from scratch, `tl.currentProject().addTrack()` (or pick a track), `track.addPart({pos, endOffset})`, then `part.addNote` into it.
- **Error handling.** A thrown script returns its message (syntax/type errors usually carry a line number) and **rolls back everything it changed** — the project is left untouched, so fix the script and re-run.
