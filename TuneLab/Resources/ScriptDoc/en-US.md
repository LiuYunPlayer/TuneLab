# Script

Run a short JavaScript program to read and edit the current project — ideal for bulk, looping, conditional, or computed edits. The whole run is **one** undoable change (Ctrl+Z reverts it). The global object `tl` is the entry point.

## Core rules

- **Handles**: tracks/parts/notes are referenced by handles. `tl.notes()` and similar return arrays of handles — iterate with `for-of`. Get a handle and use it right away; never write a handle literal and don't reuse one across runs.
- **Coordinates**: positions/durations are absolute ticks; `tl.ppq` is ticks per quarter note.
- **Pitch**: MIDI number, 60 = C4.
- **Debug**: `print(x)` writes to the output area below.

## Read

- `tl.tracks()` → array of tracks
- `tl.parts(track)` → array of parts
- `tl.notes(part)` → array of notes
- `tl.notesInRange(part, start, end)` → notes in a range
- `tl.selectedNotes(part)` → selected notes
- `tl.currentPart()` → the part open in the piano editor (or null)
- `tl.playhead()` → `{tick, seconds, bar, beat, playing}`
- `tl.snap(tick)` → snap to the editor grid
- `tl.tempos()` / `tl.timeSignatures()`
- `tl.parameterIds(part)` → editable parameter ids
- `tl.sampleParameter(part, id, start, end, samples)` → sampled curve

Handle fields: `note.pos / dur / pitch / pitchName / lyric`; `part.name / pos / dur / type / voice / noteCount`; `track.name / mute / solo / gainDb / pan`.

## Write (no commit needed)

- Tracks: `tl.addTrack(name?)`, `tl.removeTrack(track)`, `tl.setTrack(track, {name?,mute?,solo?,gainDb?,pan?})`
- Parts: `tl.addPart(track, {pos,dur,name?})`, `tl.removePart(part)`, `tl.setPart(part, {name?,pos?,dur?})`
- Notes: `tl.addNote(part, {pos,dur,pitch,lyric?})`, `tl.setNote(note, {pitch?,pos?,dur?,lyric?})`, `tl.removeNote(note)`
- Curves: `tl.setPitchLine(part, start, end, points)`, `tl.setAutomation(part, id, start, end, points)`, `tl.addVibrato(part, {start,end,frequency?,amplitude?})`
- Tempo/meter: `tl.setTempo(bpm, atTick?)`, `tl.setTimeSignature(numerator, denominator, atBar?)`

Point format: `[{tick, value}]`.

## Example

Raise every note in the current part an octave:

```js
const part = tl.currentPart();
for (const n of tl.notes(part)) {
  tl.setNote(n, { pitch: n.pitch + 12 });
}
```

## Notes

- A read result is a plain array, not a linked list (no `.first/.next`).
- Don't reuse a handle after deleting it.
- If the script errors, edits made before the error are still applied as one undoable change.
