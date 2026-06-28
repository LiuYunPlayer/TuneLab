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
  - A handle is **valid only for the current run** (objects have no persistent id and are lost when the app closes): **never write a handle literal** — always get it and use it on the spot. Using a removed handle throws.
- **Coordinates are always absolute ticks.** All positions/durations are absolute (global) ticks (`tl.ppq` is ticks per quarter note, default 480) — the same coordinate system as the playhead and bars. You **never** do any conversion.
- **Pitch is a MIDI number**, 60 = C4 (fractional values for cents).
- **On error, everything rolls back.** If the script throws partway, all changes it made are undone (the project is left unchanged) and the error is returned — so you fix the script and re-run, never patching from a half-applied state.
- **Debug output.** `print(x)` / `console.log(x)` is collected and shown in the output area below.

---

## `tl` (the editor)

Editor-level entry points — a system constant, the current project, and the editor's transient state.

| Member | Returns | Notes |
|---|---|---|
| `tl.ppq` | number | Ticks per quarter note (default 480). |
| `tl.currentProject()` | `project` | The current project (your data entry point; see below). |
| `tl.currentPart()` | `part \| null` | The MIDI part open in the piano editor. |
| `tl.playhead()` | `{tick, seconds, bar, beat, playing}` | Playhead position (bar/beat are 1-based). |
| `tl.snap(tick)` | number | Snap an absolute tick to the editor's grid. |

---

## `project` — `tl.currentProject()`

The project's data: tracks, tempo, time signatures.

| Member | Returns | Notes |
|---|---|---|
| `project.tracks()` | `[track]` | All track handles. |
| `project.addTrack(name?)` | `track` | Append a new track, returns its handle. |
| `project.removeTrack(track)` | — | Remove a track. |
| `project.tempos()` | `[{bpm, tick}]` | All tempo markers. |
| `project.timeSignatures()` | `[{numerator, denominator, bar}]` | All time-signature markers (bar is 1-based). |
| `project.setTempo(bpm, atTick?)` | — | Set tempo; if `atTick` is omitted, sets the base tempo at tick 0 (edits an existing marker there, else adds one). |
| `project.setTimeSignature(numerator, denominator, atBar?)` | — | Set time signature; `atBar` is a 1-based bar number (default 1). |

---

## `track`

**Fields** (bare properties, read/write): `name`, `isMute`, `isSolo`, `gain` (in dB, 0 = unity), `pan` ([-1, 1]).

| Method | Returns | Notes |
|---|---|---|
| `track.parts()` | `[part]` | All part handles on this track (sorted by start). |
| `track.addPart({pos, dur, name?})` | `part` | Add an empty MIDI part (absolute ticks), returns its handle. |
| `track.removePart(part)` | — | Remove a part from this track. |
| `track.set({name?, isMute?, isSolo?, gain?, pan?})` | — | Assign several fields at once; `pan` is clamped to [-1, 1]. |

---

## `part`

**Fields** (bare properties, read/write): `name`, `pos`, `dur`; **read-only**: `type` (`"midi"`/`"audio"`).

| Method | Returns | Notes |
|---|---|---|
| `part.soundSource()` | `{type, id, name, kind, defaultLyric}` | The part's sound source info (read-only snapshot); `kind` is `"voice"` or `"instrument"`. MIDI parts only. |
| `part.notes()` | `[note]` | All note handles in this MIDI part. |
| `part.selectedNotes()` | `[note]` | Notes currently selected in the piano editor (empty if none). |
| `part.notesInRange(startTick, endTick)` | `[note]` | Notes within absolute ticks `[start, end)` (by note start). |
| `part.addNote({pos, dur, pitch, lyric?})` | `note` | Add a note (pos absolute ticks, pitch MIDI), returns its handle. |
| `part.removeNote(note)` | — | Remove a note from this part. |
| `part.samplePitch(startTick, endTick, samples)` | `[number]` | Evenly sample the final pitch curve (MIDI scale) over the range. |
| `part.setPitchLine(startTick, endTick, points)` | — | Clear `[start, end)` then lay a pitch line; `points = [{tick, value}]`, value = absolute MIDI pitch (fractional ok). |
| `part.clearPitch(startTick, endTick)` | — | Clear a span of the pitch curve. |
| `part.automationIds()` | `[string]` | Editable automation ids declared by the voice (e.g. `"Volume"`; pitch is separate). |
| `part.sampleAutomation(id, startTick, endTick, samples)` | `[number]` | Evenly sample an automation curve; `NaN` = no curve there. |
| `part.setAutomation(id, startTick, endTick, points, defaultValue?)` | — | Clear then lay an automation curve; value = absolute parameter value; created on demand, `defaultValue` optional. |
| `part.clearAutomation(id, startTick, endTick)` | — | Clear a span of an automation curve. |
| `part.vibratos()` | `[vibrato]` | All vibrato handles in this part. |
| `part.addVibrato({pos, dur, frequency?, amplitude?, phase?, attack?, release?})` | `vibrato` | Add a vibrato (overlaid on the pitch curve; defaults 6Hz / 1 semitone), returns its handle. |
| `part.removeVibrato(vibrato)` | — | Remove a vibrato from this part. |
| `part.set({name?, pos?, dur?})` | — | Assign several fields at once (rename / move / resize). |

---

## `note`

**Fields** (bare properties, read/write): `pos`, `dur`, `pitch`, `lyric`; **read-only**: `pitchName` (e.g. `"C4"`).

| Method | Returns | Notes |
|---|---|---|
| `note.set({pos?, dur?, pitch?, lyric?})` | — | Assign several fields at once (re-sorts only once when pos/dur change). |

---

## `vibrato`

**Fields** (bare properties, read/write): `pos`, `dur` (absolute ticks), `frequency` (Hz), `amplitude` (semitones), `phase`, `attack`, `release` (seconds).

| Method | Returns | Notes |
|---|---|---|
| `vibrato.set({pos?, dur?, frequency?, amplitude?, phase?, attack?, release?})` | — | Assign several fields at once. |

---

## Examples

**Raise every note in the current part an octave, and add a harmony a third above each:**
```js
const part = tl.currentPart();
for (const n of part.notes()) {
  part.addNote({ pos: n.pos, dur: n.dur, pitch: n.pitch + 4, lyric: n.lyric }); // third above
  n.pitch += 12;                                                                 // original up an octave
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
const src = project.tracks()[0];
const dst = project.addTrack("Harmony +8");
for (const p of src.parts()) {
  const np = dst.addPart({ pos: p.pos, dur: p.dur, name: p.name });
  for (const n of p.notes())
    np.addNote({ pos: n.pos, dur: n.dur, pitch: n.pitch + 12, lyric: n.lyric });
}
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

## Notes

- **Handles can't be hard-coded or reused across runs.** Always get one and use it on the spot.
- **Collection methods return arrays, not linked lists.** Use `for-of` / index; there is no `.first` / `.next`. Each call is a new snapshot — store it if you reuse it.
- **Create and delete both go through the parent** (`track.addPart`/`removePart`, `part.addNote`/`removeNote`, …) — there is no `x.remove()`.
- **Changing `pos`/`dur` may change ordering.** Parts/notes/vibratos keep start order — handle addressing is unaffected (still the same object), but if you are iterating that collection at the same time, remember you hold the array snapshot taken when iteration began.
- **Notes live inside a MIDI part.** To write a melody from scratch, `tl.currentProject().addTrack()` (or pick a track), `track.addPart({...})`, then `part.addNote` into it.
- **Error handling.** A thrown script returns its message (syntax/type errors usually carry a line number) and **rolls back everything it changed** — the project is left untouched, so fix the script and re-run.
