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
        "  tl.selectedTracks()                      [track]  (tracks selected in the track list)\n" +
        "  tl.trackSelection()                      {startTick, endTick, startTrackNumber, endTrackNumber} | null  (the arrangement RANGE selection: a tick×track area the user dragged out; track numbers 1-based, contiguous; null when none. ORTHOGONAL to selectedParts/selectedNotes — it marks a place, not objects, so use it to bulk-process whatever falls inside)\n" +
        "  tl.pianoSelection()                      {startTick, endTick} | null  (the piano-editor RANGE selection: a tick band the user dragged out inside the current part, spanning all pitches; null when none. Time-only — no track/pitch. Coexists independently with tl.trackSelection(); use it to bulk-process whatever falls in that time span of the current part)\n" +
        "  tl.playhead()                            {tick, seconds, bar, beat, playing}\n" +
        "  tl.snap(tick)                            tick snapped to the editor grid\n" +
        "\n" +
        "project  (tl.currentProject())\n" +
        "  project.tracks()                         [track]\n" +
        "  project.addTrack(name?) -> track         project.removeTrack(track)\n" +
        "  project.importTracks(path) -> [track]    import ALL tracks from a file into this project (additive; keeps current tempo, tracks at raw ticks). path = local file path; formats: tlp/tlpx/mid/midi + installed format plugins. Missing/unsupported/parse error throws. Returns the newly added track handles.\n" +
        "  project.tempos()                         [{bpm, tick}]\n" +
        "  project.timeSignatures()                 [{numerator, denominator, bar}]\n" +
        "  project.setTempo(bpm, atTick?)           project.setTimeSignature(numerator, denominator, atBar?)   // atBar is 1-based\n" +
        "\n" +
        "track\n" +
        "  fields (read/write):  name, isMute, isSolo, gain, pan       // gain is in dB (0 = unity); pan in [-1,1]\n" +
        "  track.parts()                            [part]\n" +
        "  track.addPart({startPos, endPos, name?}) -> part    track.removePart(part)\n" +
        "  track.set({name?, isMute?, isSolo?, gain?, pan?})   // assign several fields at once\n" +
        "\n" +
        "part\n" +
        "  fields (read/write):  name, startPos, endPos    field (read-only): type (\"midi\"/\"audio\")   // startPos/endPos = the part's visible span in absolute ticks; setting startPos moves the whole part (content follows), setting endPos resizes the right edge\n" +
        "  part.soundSource()                       {type, id, name, kind, defaultLyric}   (sound source snapshot; kind=\"voice\"|\"instrument\")\n" +
        "  part.setSoundSource({kind, type, id})    switch the part's sound source (kind=\"voice\"(default)|\"instrument\"; type/id from list_sound_sources); unknown source errors; empty type+id clears to none\n" +
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
        "  // EFFECTS (serial effect chain on this part; order = array index, 0-based):\n" +
        "  part.effects()                           [effect]\n" +
        "  part.addEffect(type) -> effect           part.removeEffect(effect)   // type = an effect engine id from list_effects; appended to the chain end\n" +
        "  part.moveEffect(effect, index)           move an effect to a 0-based position in the chain\n" +
        "  // PART PROPERTIES (voice/instrument-declared per-part params; keys/ranges from list_sound_sources):\n" +
        "  part.getProperty(key)                    current value (number/boolean/string), or null if unset\n" +
        "  part.setProperty(key, value)             set one declared part param (value = number/boolean/string)\n" +
        "  part.set({name?, startPos?, endPos?})\n" +
        "\n" +
        "note\n" +
        "  fields (read/write):  pos, dur, pitch, lyric, pronunciation      field (read-only): pitchName  (e.g. \"C4\")   // pronunciation = a voice G2P override; empty string = derive from lyric\n" +
        "  note.set({pos?, dur?, pitch?, lyric?, pronunciation?})   // assign several fields at once (one re-sort)\n" +
        "  // NOTE PROPERTIES (voice/instrument-declared per-note params; keys/ranges from list_sound_sources):\n" +
        "  note.getProperty(key)  / note.setProperty(key, value)    current value or null / set one declared note param (number/boolean/string)\n" +
        "  // PHONEMES (voice only; leading = pre-vowel consonants, body = vowel+coda). Read anytime; the FIRST write auto-pins (fixes the synthesized phonemes into editable data, like the sidebar's first edit):\n" +
        "  note.phonemes()                          [phoneme]   (leading ++ body, time order; empty until the note has been synthesized)\n" +
        "  field (read-only): hasPinnedPhonemes (bool)      field (read/write): bodyOffset (seconds; leading/body junction offset from note start; writing auto-pins)\n" +
        "  note.addPhoneme({symbol, duration?, stretchWeight?, leading?}) -> phoneme    note.removePhoneme(phoneme)   // appended to leading (leading:true) or body list; auto-pins\n" +
        "  note.pinPhonemes()  / note.clearPhonemes()   // pin = fix synthesized phonemes as editable (usually automatic); clear = drop pinned phonemes, revert to synthesized\n" +
        "\n" +
        "phoneme  (an item in note.phonemes(); positional — its list index shifts when phonemes are added/removed, so re-fetch note.phonemes() after a structural change)\n" +
        "  field (read-only): leading (bool)      fields (read/write): symbol, duration (seconds), stretchWeight (0 = rigid consonant, >0 = stretchable vowel)   // writing any field auto-pins the note's phonemes\n" +
        "  phoneme.getProperty(key)                 current value (number/boolean/string), or null if unset or not yet pinned (keys/ranges from list_sound_sources phoneme slots)\n" +
        "  phoneme.setProperty(key, value)          set one declared phoneme param (auto-pins)\n" +
        "\n" +
        "vibrato\n" +
        "  fields (read/write):  pos, dur, frequency, amplitude, phase, attack, release    // pos/dur in ticks, frequency Hz, amplitude semitones\n" +
        "  vibrato.set({pos?, dur?, frequency?, amplitude?, phase?, attack?, release?})\n" +
        "\n" +
        "effect  (an item in part.effects())\n" +
        "  field (read/write):  isEnabled (bool; false = bypass)      read-only: type, name, id, index\n" +
        "  effect.getProperty(key)                  current value (number/boolean/string), or null if unset (defaults & keys/ranges: list_effects)\n" +
        "  effect.setProperty(key, value)           set one parameter (value = number/boolean/string)\n" +
        "  // this effect's PARAMETER AUTOMATION curves (same shape as part automation; absolute-tick points):\n" +
        "  effect.automationIds()                   [string]   (automatable param ids declared by the effect engine; see list_effects)\n" +
        "  effect.sampleAutomation(id, start, end, samples)   [number]   (NaN = no curve there)\n" +
        "  effect.setAutomation(id, start, end, points, defaultValue?)    effect.clearAutomation(id, start, end)   // points=[{tick,value}], value=absolute param value, created on demand\n" +
        "\n" +
        "print(x) / console.log(x) -> debugging output (returned to you / shown in the panel).\n" +
        "Notes live inside a MIDI part; to write a melody from scratch, tl.currentProject().addTrack() (or pick one), track.addPart({...}), then part.addNote into the returned part.\n" +
        "If the script throws, EVERYTHING rolls back (the project is left unchanged) and the error is returned, so fix the script and re-run rather than patching from a half-applied state.\n" +
        "\n" +
        "EXAMPLE — raise every note in the current part an octave and add a harmony a third above:\n" +
        "  const part = tl.currentPart();\n" +
        "  for (const n of part.notes()) {\n" +
        "    part.addNote({ pos: n.pos, dur: n.dur, pitch: n.pitch + 4, lyric: n.lyric });   // third above\n" +
        "    n.pitch += 12;                                                                  // original up an octave\n" +
        "  }\n" +
        "\n" +
        "TOOL SCRIPTS (for save_script) — register a REUSABLE menu tool the user can click again later. Define two top-level functions; the top level must have NO side effects (it is evaluated just to read metadata):\n" +
        "  function getScriptInfo() { return { name, category, author, version, context, id?, defaultGesture? }; }   // metadata only; read tl.language here to localize `name`\n" +
        "  function main() { /* the action — use `tl` exactly like a run_script body */ }\n" +
        "  context decides where it appears, what it targets, AND the shortcut's active area:\n" +
        "    'global'      -> top Scripts menu (grouped by category). Act on tl.currentPart() / whole project. Shortcut works anywhere in the editor.\n" +
        "    'note'        -> piano-roll right-click ON a note.   Target = tl.currentPart().selectedNotes() (the clicked note is always selected).\n" +
        "    'partContent' -> piano-roll right-click on BLANK.    Target = tl.currentPart() (its content).\n" +
        "    'pianoSelection' -> piano-roll right-click ON the range selection.  Target = tl.pianoSelection() (a tick band; null when none).\n" +
        "    'part'        -> arrangement right-click ON a part.  Target = tl.selectedParts() (the clicked part is always selected; may be many).\n" +
        "    'track'        -> track-header right-click.          Target = tl.selectedTracks() (the clicked track is always selected; may be many).\n" +
        "    'trackContent' -> arrangement right-click on a track's BLANK lane.  Target = tl.selectedTracks().\n" +
        "    'trackSelection' -> arrangement right-click ON the range selection.  Target = tl.trackSelection() (tick x track; null when none).\n" +
        "  piano* contexts' shortcuts fire only in the piano roll, arrangement contexts only in the arrangement (global anywhere). Triggered by a shortcut (no click), the target is the CURRENT selection (empty selection -> main() should no-op).\n" +
        "  id (optional): a STABLE keybinding/settings anchor, independent of the filename — set it once, never change it after publishing, so renaming/reinstalling keeps the user's shortcut. Chars: A-Z a-z 0-9 . _ -. Omit it and the filename is the id (renaming then drops the binding).\n" +
        "  defaultGesture (optional): a suggested shortcut like 'mod+shift+k' (mod = Cmd on macOS / Ctrl on Windows; or write ctrl/cmd/alt/shift literally). Applied only if that key is free in the script's area; it NEVER overrides a built-in. Users can rebind in Settings.\n" +
        "  main() runs as ONE undoable change; on any error EVERYTHING rolls back. A script WITHOUT getScriptInfo is a plain run-once script (Script side panel only, never in menus).\n" +
        "\n" +
        "INPUTS (optional) — to prompt the user for parameters before the tool runs, define getInputConfig; the host renders a form from it and passes the filled values to main(inputs):\n" +
        "  function getInputConfig(ctx) { return { semitones: SliderConfig.integer(12, -24, 24), harmony: CheckBoxConfig.create(true) }; }\n" +
        "  function main(inputs) { for (const n of tl.currentPart().selectedNotes()) n.pitch += inputs.semitones; }\n" +
        "  · Returns a MAP of input-name -> a config built with the builders below (NOT plain data). The key is the field's label.\n" +
        "  · ctx.values = the values entered so far, and it is SPARSE: only keys the user actually changed are present; a key not yet set reads `undefined`. So in getInputConfig ALWAYS default it: `const mode = ctx.values.mode ?? 'transpose'`. getInputConfig is re-run on every change, so branch on ctx.values to add/remove fields (conditional inputs). You may also read tl.currentPart()/selectedNotes() etc. here as context. It MUST be side-effect-free (declaration only; do the work in main).\n" +
        "  · main's `inputs` is the opposite — FULL: every field you declared is present (the user's value, or its config default). Read them directly, no presence check.\n" +
        "  Config builders (names mirror the C# config classes; methods are camelCase). ComboBox default is the VALUE, not an index:\n" +
        "    SliderConfig.linear(default, min, max) / SliderConfig.integer(default, min, max)   [.withFormat(NumberFormat.decimals(n))]\n" +
        "    DraggableNumberBoxConfig.create(default) / .integer(default)   [.withMin(x) / .withMax(x) / .withRange(a,b) / .withStep(s)]\n" +
        "    ComboBoxConfig.create(['a','b']) or .create()   [.append(x) / .appendSeparator() / .withDefault('a')]\n" +
        "    CheckBoxConfig.create(false)      TextBoxConfig.create('')   [.withPassword()]\n" +
        "  Advanced (log/exp axes, units): SliderConfig.create(default, NormalizedScale.custom(p=>value, value=>p)) — two inverse JS functions on 0..1; .withFormat(NumberFormat.custom(v=>string, s=>number|null)) — parse returns null on failure. These run live while the input form is open (keep them pure & cheap; errors degrade gracefully, they never throw into the UI).\n" +
        "  A tool WITHOUT getInputConfig just runs main() with no dialog (main may ignore its argument).\n" +
        "  Once saved, a tool with inputs can be re-run later WITHOUT rewriting it: call get_script_inputs(name) to see its fields (and the user's last values), then run_saved_script(name, inputs?) to run it.\n" +
        "  EXAMPLE tool with inputs — 'Transpose' asking for the interval:\n" +
        "    function getScriptInfo() { return { name: 'Transpose', context: 'note' }; }\n" +
        "    function getInputConfig(ctx) { return { semitones: SliderConfig.integer(12, -24, 24) }; }\n" +
        "    function main(inputs) { for (const n of tl.currentPart().selectedNotes()) n.pitch += inputs.semitones; }\n" +
        "  EXAMPLE tool — 'Add Third Harmony' on selected notes:\n" +
        "    function getScriptInfo() { return { name: tl.language === 'zh-CN' ? '加三度和声' : 'Add Third Harmony', context: 'note' }; }\n" +
        "    function main() { const p = tl.currentPart(); for (const n of p.selectedNotes()) p.addNote({ pos: n.pos, dur: n.dur, pitch: n.pitch + 4, lyric: n.lyric }); }";
}
