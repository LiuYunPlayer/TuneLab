namespace TuneLab.Scripting;

// 脚本 API 的【权威参考文本，单一来源】。三处共用：
//  · agent 的 get_script_api 工具（模型按需拉取，渐进式披露，不常驻 prompt）；
//  · Script 侧栏的「API」按钮（人类手写脚本时查阅）；
//  · 与 docs/scripting-api.md（人类散文版）保持一致。
// ⚠️ 改动时务必保持准确：绝不用"链表(linked list)"等会诱导 `.first/.next` 错误遍历的措辞——读方法返回的是普通数组。
internal static class ScriptApiReference
{
    public const string Text =
        "TuneLab Script API — global `tl`. Reads return plain ARRAYS of handles (iterate with for-of or index, use .length; NOT a linked list — no .first/.next).\n" +
        "Positions/durations are ABSOLUTE ticks (tl.ppq = ticks per quarter). Pitch = MIDI number (60=C4). The whole run is ONE undoable change (you never call commit).\n" +
        "A handle is an opaque reference to one track/part/note: it exposes data fields but has no id, is valid only this run — get it via a tl.* read, never write a handle literal.\n" +
        "\n" +
        "READ\n" +
        "  tl.ppq                                   ticks per quarter note\n" +
        "  tl.tracks()                              [track]\n" +
        "  tl.parts(track)                          [part]\n" +
        "  tl.notes(part)                           [note]\n" +
        "  tl.notesInRange(part, start, end)        [note]   (absolute ticks, [start,end), by note start)\n" +
        "  tl.selectedNotes(part)                   [note]   (currently selected in the piano editor)\n" +
        "  tl.currentPart()                         part | null   (the part open in the piano editor)\n" +
        "  tl.playhead()                            {tick, seconds, bar, beat, playing}\n" +
        "  tl.snap(tick)                            tick snapped to the editor grid\n" +
        "  tl.tempos()                              [{bpm, tick}]\n" +
        "  tl.timeSignatures()                      [{numerator, denominator, bar}]\n" +
        "  tl.parameterIds(part)                    [string]   (e.g. \"pitch\", \"Volume\")\n" +
        "  tl.sampleParameter(part, id, start, end, samples)   [number]   (NaN = no curve there)\n" +
        "\n" +
        "WRITE (deferred into the single commit — do NOT call commit yourself)\n" +
        "  tl.addTrack(name?) -> track      tl.removeTrack(track)      tl.setTrack(track, {name?,mute?,solo?,gainDb?,pan?})\n" +
        "  tl.addPart(track, {pos,dur,name?}) -> part    tl.removePart(part)    tl.setPart(part, {name?,pos?,dur?})\n" +
        "  tl.addNote(part, {pos,dur,pitch,lyric?}) -> note    tl.setNote(note, {pitch?,pos?,dur?,lyric?})    tl.removeNote(note)\n" +
        "  tl.setPitchLine(part, start, end, points)     tl.clearPitch(part, start, end)        // points=[{tick,value}], value=absolute MIDI pitch\n" +
        "  tl.setAutomation(part, id, start, end, points, defaultValue?)    tl.clearAutomation(part, id, start, end)   // value=absolute parameter value\n" +
        "  tl.addVibrato(part, {start,end,frequency?,amplitude?})\n" +
        "  tl.setTempo(bpm, atTick?)         tl.setTimeSignature(numerator, denominator, atBar?)   // atBar is 1-based\n" +
        "\n" +
        "HANDLE FIELDS (read live)\n" +
        "  track: name, mute, solo, gainDb, pan, partCount\n" +
        "  part:  name, pos, dur, isMidi, type, voice, noteCount\n" +
        "  note:  pos, dur, pitch, pitchName, lyric\n" +
        "\n" +
        "print(x) / console.log(x) -> debugging output (returned to you / shown in the panel).\n" +
        "Notes live inside a MIDI part; to write a melody from scratch, tl.addPart(track, {...}) first, then tl.addNote into the returned part.\n" +
        "If the script throws, edits made before the error are still applied as one undoable change and the error is returned, so you can fix and retry.\n" +
        "\n" +
        "EXAMPLE — raise every note in the current part an octave and add a harmony a third above:\n" +
        "  const part = tl.currentPart();\n" +
        "  for (const n of tl.notes(part)) {\n" +
        "    tl.addNote(part, { pos: n.pos, dur: n.dur, pitch: n.pitch + 4, lyric: n.lyric });\n" +
        "    tl.setNote(n, { pitch: n.pitch + 12 });\n" +
        "  }";
}
