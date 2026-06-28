namespace TuneLab.Scripting;

// 脚本 API 喂给 LLM 的【精简速查文本】，是 agent `get_script_api` 工具的唯一返回（按需拉取、渐进式披露、不常驻 prompt）。
// 这是给模型省 token 的精简版；给人类看的【完整手册】在 Resources/ScriptDoc/{文化码}.md（Script 侧栏 Doc 面渲染）。
// 两者覆盖同一套 API、措辞须一致，且 ⚠️ 绝不用"链表(linked list)"等会诱导 `.first/.next` 错误遍历的措辞——集合方法返回的是普通数组。
internal static class ScriptApiReference
{
    public const string Text =
        "TuneLab Script API — OBJECT-STYLE. Global `tl` is the EDITOR; project data hangs off tl.currentProject(). tracks/parts/notes/vibratos are handles with their own fields and methods.\n" +
        "TWO SHAPES, one rule of thumb:\n" +
        "  · bare property  = a single scalar field you can READ and ASSIGN:  n.pitch,  n.pitch += 12,  track.isMute = true\n" +
        "  · method with () = a query / create / delete / compute:  part.notes(),  track.addPart({...}),  part.removeNote(n)\n" +
        "Create and delete BOTH hang off the parent: project.addTrack/removeTrack, track.addPart/removePart, part.addNote/removeNote, part.addVibrato/removeVibrato. There is NO x.remove().\n" +
        "Collection methods (project.tracks(), part.notes()) return a plain ARRAY (for-of or index, has .length) — a NEW snapshot each call, so store it in a var; it is NOT a linked list (no .first/.next).\n" +
        "Positions/durations are ABSOLUTE ticks (tl.ppq = ticks per quarter). Pitch = MIDI number (60=C4). The whole run is ONE undoable change (you never call commit).\n" +
        "A handle is an opaque reference to one object: no id, valid only this run — get it via a read, never write a handle literal. Assigning a field or calling a write method takes effect immediately and folds into the single commit.\n" +
        "\n" +
        "tl  (the editor)\n" +
        "  tl.ppq                                   ticks per quarter note (scalar property)\n" +
        "  tl.language                              current UI culture code, e.g. \"zh-CN\"/\"en-US\" (for localized text)\n" +
        "  tl.currentProject()                      -> project   (your data entry point)\n" +
        "  tl.currentPart()                         part | null   (the part open in the piano editor)\n" +
        "  tl.selectedParts()                       [part]   (parts selected in the arrangement)\n" +
        "  tl.playhead()                            {tick, seconds, bar, beat, playing}\n" +
        "  tl.snap(tick)                            tick snapped to the editor grid\n" +
        "\n" +
        "project  (tl.currentProject())\n" +
        "  project.tracks()                         [track]\n" +
        "  project.addTrack(name?) -> track         project.removeTrack(track)\n" +
        "  project.tempos()                         [{bpm, tick}]\n" +
        "  project.timeSignatures()                 [{numerator, denominator, bar}]\n" +
        "  project.setTempo(bpm, atTick?)           project.setTimeSignature(numerator, denominator, atBar?)   // atBar is 1-based\n" +
        "\n" +
        "track\n" +
        "  fields (read/write):  name, isMute, isSolo, gain, pan       // gain is in dB (0 = unity); pan in [-1,1]\n" +
        "  track.parts()                            [part]\n" +
        "  track.addPart({pos, dur, name?}) -> part    track.removePart(part)\n" +
        "  track.set({name?, isMute?, isSolo?, gain?, pan?})   // assign several fields at once\n" +
        "\n" +
        "part\n" +
        "  fields (read/write):  name, pos, dur     field (read-only): type (\"midi\"/\"audio\")\n" +
        "  part.soundSource()                       {type, id, name, kind, defaultLyric}   (sound source snapshot; kind=\"voice\"|\"instrument\")\n" +
        "  part.notes()                             [note]\n" +
        "  part.selectedNotes()                     [note]   (currently selected in the piano editor)\n" +
        "  part.notesInRange(start, end)            [note]   (absolute ticks, [start,end), by note start)\n" +
        "  part.addNote({pos, dur, pitch, lyric?}) -> note    part.removeNote(note)\n" +
        "  // PITCH (its own curve, MIDI scale):\n" +
        "  part.samplePitch(start, end, samples)    [number]\n" +
        "  part.setPitchLine(start, end, points)    part.clearPitch(start, end)        // points=[{tick,value}], value=absolute MIDI pitch\n" +
        "  // AUTOMATION (voice-declared params like \"Volume\"; pitch is NOT one of these):\n" +
        "  part.automationIds()                     [string]\n" +
        "  part.sampleAutomation(id, start, end, samples)   [number]   (NaN = no curve there)\n" +
        "  part.setAutomation(id, start, end, points, defaultValue?)    part.clearAutomation(id, start, end)   // value=absolute parameter value, created on demand\n" +
        "  part.vibratos()                          [vibrato]\n" +
        "  part.addVibrato({pos, dur, frequency?, amplitude?, phase?, attack?, release?}) -> vibrato    part.removeVibrato(vibrato)\n" +
        "  part.set({name?, pos?, dur?})\n" +
        "\n" +
        "note\n" +
        "  fields (read/write):  pos, dur, pitch, lyric             field (read-only): pitchName  (e.g. \"C4\")\n" +
        "  note.set({pos?, dur?, pitch?, lyric?})   // assign several fields at once (one re-sort)\n" +
        "\n" +
        "vibrato\n" +
        "  fields (read/write):  pos, dur, frequency, amplitude, phase, attack, release    // pos/dur in ticks, frequency Hz, amplitude semitones\n" +
        "  vibrato.set({pos?, dur?, frequency?, amplitude?, phase?, attack?, release?})\n" +
        "\n" +
        "print(x) / console.log(x) -> debugging output (returned to you / shown in the panel).\n" +
        "Notes live inside a MIDI part; to write a melody from scratch, tl.currentProject().addTrack() (or pick one), track.addPart({...}), then part.addNote into the returned part.\n" +
        "If the script throws, edits made before the error are still applied as one undoable change and the error is returned, so you can fix and retry.\n" +
        "\n" +
        "EXAMPLE — raise every note in the current part an octave and add a harmony a third above:\n" +
        "  const part = tl.currentPart();\n" +
        "  for (const n of part.notes()) {\n" +
        "    part.addNote({ pos: n.pos, dur: n.dur, pitch: n.pitch + 4, lyric: n.lyric });   // third above\n" +
        "    n.pitch += 12;                                                                  // original up an octave\n" +
        "  }";
}
