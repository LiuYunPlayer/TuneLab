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
        "*** INFO OBJECTS — how to copy anything, and how to create with full control ***\n" +
        "Every handle has getInfo() -> a PLAIN JS object holding EVERYTHING about it (nested: a part info carries its sound source, notes, pitch line, automation curves, vibratos, effect chain, properties, phonemes...).\n" +
        "Every parent has addX(info) which takes that same shape back. So:\n" +
        "  · COPY (full fidelity, nothing lost):   track.addPart(otherPart.getInfo())   project.addTrack(otherTrack.getInfo())   part.addNote(n.getInfo())\n" +
        "  · CREATE with any field set:            part.addNote({pos, dur, pitch, lyric, pronunciation, properties, bodyPhonemes})\n" +
        "An info is PURE DATA — mutate it freely before adding (no undo entry, no side effect), and add the same info as many times as you like (each add makes a NEW object).\n" +
        "NEVER hand-copy an object field by field to \"duplicate\" it — you will silently drop the sound source, curves, effects, properties and phonemes. Use getInfo() -> addX().\n" +
        "Omitted info fields fall back to the stored default (e.g. name -> empty string), not to something invented for you.\n" +
        "!! TO PLACE A COPY SOMEWHERE ELSE, MOVE THE HANDLE, NOT THE INFO: `const c = t.addPart(p.getInfo()); c.pos += 1920;`\n" +
        "   Every tick in an info is ABSOLUTE — including the notes/curve points nested inside a part info. So bumping `info.pos` alone slides the part's WINDOW while its content stays at the old absolute ticks (the content ends up outside the window). Assigning `part.pos` on the handle is the operation that moves a part and its content together.\n" +
        "\n" +
        "*** MOVE vs COPY (removeX returns a DETACHED handle) ***\n" +
        "removeX(child) detaches the child and RETURNS its handle; the object is still alive and READABLE (getInfo() works) — it just has no parent.\n" +
        "removeX is the only one that hands something back (like the DOM's parent.removeChild(child)), so a move is one expression: b.insertPart(a.removePart(p)). insertX returns nothing — echoing back the handle you just passed in carries no information.\n" +
        "Removing a child that is NOT this parent's child THROWS (a programming error, not a query) — there is no boolean 'was it there' result.\n" +
        "  · delete = remove and don't put it back.\n" +
        "  · move   = remove, then insertX(child) — SAME object, so its notes/curves/effects/phonemes travel with it and undo sees one move.\n" +
        "A detached handle is READ-ONLY: writing a field throws and tells you to insert it back first.\n" +
        "Only a part can change parent: track.removePart(p) then otherTrack.insertPart(p) moves it ACROSS TRACKS. A note/vibrato/effect belongs to the part it was created on — insertX only puts it back on that same part; to get one onto another part use otherPart.addX(x.getInfo()).\n" +
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
        "  project.addTrack(info?, index?) -> track  new track from a track info (omit info for an empty one); index = 0-based slot, omitted = append\n" +
        "  project.insertTrack(track, index?)       put a DETACHED track back (this is how you reorder)\n" +
        "  project.removeTrack(track) -> track       detach it and hand the handle back\n" +
        "  project.importTracks(path) -> [track]    import ALL tracks from a file into this project (additive; keeps current tempo, tracks at raw ticks). path = local file path; formats: tlp/tlpx/mid/midi + installed format plugins. Missing/unsupported/parse error throws. Returns the newly added track handles.\n" +
        "  project.tempos()                         [{bpm, tick}]\n" +
        "  project.timeSignatures()                 [{numerator, denominator, bar}]\n" +
        "  project.setTempo(bpm, atTick?)           project.setTimeSignature(numerator, denominator, atBar?)   // atBar is 1-based\n" +
        "  project.removeTempo(atTick)              project.removeTimeSignature(atBar)   // throws if no marker is there; the FIRST marker is the project's base tempo/meter and can't be removed (change it with setTempo/setTimeSignature)\n" +
        "  // EXPORT SETTINGS (read/write fields; per-track switches live on the track handle):\n" +
        "  project.exportPath, project.exportFileName, project.exportFormat (\"wav\"|\"mp3\"|\"flac\"|\"ogg\"), project.exportSampleRate, project.exportBitDepth (wav/flac), project.exportBitrate (mp3/ogg, kbps), project.masterExportEnabled, project.masterExportChannels\n" +
        "  These are SETTINGS, not project data: assigning them does NOT go on the undo stack (Ctrl+Z won't put the old export path back), exactly like changing them in the export panel. A failed or previewed run still restores them, so \"the whole run is atomic\" still holds. Writing the audio file itself is the export_project tool, not this.\n" +
        "\n" +
        "track\n" +
        "  fields (read/write):  name, isMute, isSolo, gain, pan, asRefer, color   // gain in dB (0 = unity); pan in [-1,1]; asRefer = other sound sources may hear this track; color = hex like \"#FF8800\" (empty = theme default)\n" +
        "  export settings (read/write):  exportEnabled, exportChannels (1 = mono, 2 = stereo)   // SETTINGS, not project data — see the note under `project`\n" +
        "  track.getInfo()                          {name, gain, pan, mute, solo, asRefer, color, parts:[part info]}   // the export switches are deliberately NOT in here (they are settings, not part of the track's content), so a copied track gets the defaults\n" +
        "  track.parts()                            [part]\n" +
        "  track.addPart(info) -> part              new part from a part info (fields below)\n" +
        "  track.insertPart(part)                   put a DETACHED part on this track — the track may be a DIFFERENT one, which is how you MOVE a part across tracks\n" +
        "  track.removePart(part) -> part           detach it and hand the handle back\n" +
        "\n" +
        "part\n" +
        "  GEOMETRY — three RAW fields (read/write) plus three DERIVED ones (read-only), same model as the data layer:\n" +
        "    pos          the anchor's absolute tick — AND the origin every bit of content (notes/curves/vibratos) is measured from, so assigning pos MOVES the whole part (content follows, length unchanged)\n" +
        "    startOffset  left edge relative to the anchor (>0 trims the front, <0 extends it)\n" +
        "    endOffset    right edge relative to the anchor\n" +
        "    startPos = pos + startOffset,   endPos = pos + endOffset,   dur = endOffset - startOffset      (read-only)\n" +
        "    -> an empty part covering ticks 1920..3840 is  track.addPart({pos: 1920, endOffset: 1920})\n" +
        "  other fields (read/write):  name, gain (dB, part-level, adds to the track's)    field (read-only): type (\"midi\"/\"audio\")\n" +
        "  part.getInfo()                           {type, name, pos, startOffset, endOffset, gain, soundSource, notes, vibratos, effects, automations, piecewiseAutomations, pitch, properties}; an audio part instead has {type:\"audio\", …, path}\n" +
        "  part.track()                             the track this part is on (read-only; to change it, removePart + insertPart on the other track)\n" +
        "  part.soundSource()                       {type, id, name, kind, defaultLyric}   (sound source snapshot; kind=\"voice\"|\"instrument\")\n" +
        "  part.setSoundSource({kind, type, id})    switch the part's sound source (kind=\"voice\"(default)|\"instrument\"; type/id from list_sound_sources); unknown source errors; empty type+id clears to none\n" +
        "  part.notes()                             [note]\n" +
        "  part.selectedNotes()                     [note]   (currently selected in the piano editor)\n" +
        "  part.addNote(info) -> note                info = {pos, dur, pitch, lyric?, pronunciation?, properties?, leadingPhonemes?, bodyPhonemes?, bodyOffset?}\n" +
        "  part.insertNote(note)                     part.removeNote(note) -> note\n" +
        "  // PITCH (its own curve, MIDI scale):\n" +
        "  part.samplePitch(start, end, samples)    [number]\n" +
        "  part.setPitchLine(start, end, points)    part.clearPitch(start, end)        // points=[{tick,value}], value=absolute MIDI pitch\n" +
        "  // AUTOMATION — CONTINUOUS curves (sound-source-declared params like \"Volume\"; they have a baseline default value; pitch is NOT one of these):\n" +
        "  part.automationIds()                     [string]\n" +
        "  part.sampleAutomation(id, start, end, samples)   [number]   (NaN = no curve there)\n" +
        "  part.setAutomation(id, start, end, points, defaultValue?)    part.clearAutomation(id, start, end)   // value=absolute parameter value, created on demand\n" +
        "  // AUTOMATION — PIECEWISE curves (no baseline: gaps between segments mean \"no value\", exactly like the pitch line). Separate id list because the two families read/write differently:\n" +
        "  part.piecewiseAutomationIds()            [string]\n" +
        "  part.samplePiecewiseAutomation(id, start, end, samples)   [number]\n" +
        "  part.setPiecewiseAutomationLine(id, start, end, points)   part.clearPiecewiseAutomation(id, start, end)\n" +
        "  // LOCK — freeze the engine's READ-ONLY output into your own editable curve (the same thing the lock brush does in the editor). Once locked, that data is YOURS: keep editing it, the engine no longer overwrites it. This is how you keep the model's line and only change part of it — without it you'd be drawing from blank and losing every detail the model produced.\n" +
        "  part.lockPitch(start?, end?)             -> bool   synthesized pitch -> the pitch curve\n" +
        "  part.lockAutomation(id, start?, end?)    -> bool   that track's synthesized parameter -> the editable track with the SAME id (continuous vs piecewise is handled for you)\n" +
        "  part.hasSynthesizedParameter(id)                     bool      does the sound source publish a synthesized parameter with this id? only such a track has anything to lock\n" +
        "  Ranges are optional but come in PAIRS: pass BOTH start and end, or NEITHER (= the whole part). The returned bool is DID IT ACTUALLY LOCK ANYTHING — false means there was no synthesis output in that range (usually: not synthesized yet), a no-op rather than an error, so check it instead of assuming success. An unknown id, or a track with no paired synthesized parameter, THROWS (which rolls the whole run back) — call hasSynthesizedParameter(id) first if unsure. Locking is ONE-SHOT, not a live link: later re-synthesis does not update what you locked.\n" +
        "  part.vibratos()                          [vibrato]\n" +
        "  part.addVibrato(info) -> vibrato          info = {pos, dur, frequency?, amplitude?, phase?, attack?, release?, affectedAutomations?, affectedEffectAutomations?}\n" +
        "  part.insertVibrato(vibrato)                part.removeVibrato(vibrato) -> vibrato\n" +
        "  // EFFECTS (serial effect chain on this part; order = array index, 0-based):\n" +
        "  part.effects()                           [effect]\n" +
        "  part.addEffect(info, index?) -> effect    info.type = an effect engine id from list_effects (required, must exist); index omitted = append to the chain end\n" +
        "  part.insertEffect(effect, index?)               part.removeEffect(effect) -> effect\n" +
        "  part.moveEffect(effect, index)           move an effect to a 0-based position in the chain\n" +
        "  // PART PROPERTIES (voice/instrument-declared per-part params; keys/ranges from list_sound_sources):\n" +
        "  part.getProperty(key)                    current value (number/boolean/string), or null if unset\n" +
        "  part.setProperty(key, value)             set one declared part param (value = number/boolean/string)\n" +
        "\n" +
        "note\n" +
        "  fields (read/write):  pos, dur, pitch, lyric, pronunciation      field (read-only): pitchName  (e.g. \"C4\")   // pronunciation = an explicit voice pronunciation override; empty = the lyric text itself reaches the engine, which does its own G2P\n" +
        "  note.getInfo()                           {pos, dur, pitch, lyric, pronunciation, properties, leadingPhonemes, bodyPhonemes, bodyOffset}\n" +
        "  note.part()                              the part this note is on (read-only)      // vibrato.part() and effect.part() exist too\n" +
        "  // NOTE PROPERTIES (voice/instrument-declared per-note params; keys/ranges from list_sound_sources):\n" +
        "  note.getProperty(key)  / note.setProperty(key, value)    current value or null / set one declared note param (number/boolean/string)\n" +
        "  // PHONEMES (voice only). TWO SEPARATE LISTS: leading = pre-vowel consonants, body = vowel+coda. Read anytime; the FIRST write auto-locks (fixes the synthesized phonemes into editable data, like the sidebar's first edit) — SAME verb as part.lockPitch/lockAutomation, same idea applied to phonemes:\n" +
        "  note.phonemes()                          [phoneme]   (leading ++ body, time order; empty until the note has been synthesized)\n" +
        "  field (read-only): hasLockedPhonemes (bool)      field (read/write): bodyOffset (seconds; leading/body junction offset from note start; writing auto-locks)\n" +
        "  note.addLeadingPhoneme(info) -> phoneme   note.addBodyPhoneme(info) -> phoneme    // info = {symbol, duration?, stretchWeight?, properties?}; appended to that list; auto-locks\n" +
        "  note.removePhoneme(phoneme)               // phonemes have no parent pointer, so to get one onto another note use otherNote.addBodyPhoneme(ph.getInfo()) then removePhoneme\n" +
        "  note.lockPhonemes()  / note.clearPhonemes()   // lock = fix the synthesized phonemes as editable data (usually automatic); clear = drop them and revert to synthesized\n" +
        "\n" +
        "phoneme  (an item in note.phonemes(); positional — its list index shifts when phonemes are added/removed, so re-fetch note.phonemes() after a structural change)\n" +
        "  field (read-only): leading (bool)      fields (read/write): symbol, duration (seconds), stretchWeight (0 = rigid consonant, >0 = stretchable vowel)   // writing any field auto-locks the note's phonemes\n" +
        "  phoneme.getInfo()                        {symbol, duration, stretchWeight, properties}   (properties is null while not locked)\n" +
        "  phoneme.getProperty(key)                 current value (number/boolean/string), or null if unset or not yet locked (keys/ranges from list_sound_sources phoneme slots)\n" +
        "  phoneme.setProperty(key, value)          set one declared phoneme param (auto-locks)\n" +
        "\n" +
        "vibrato\n" +
        "  fields (read/write):  pos, dur, frequency, amplitude, phase, attack, release    // pos/dur in ticks, frequency Hz, amplitude semitones, phase in units of PI, attack/release seconds\n" +
        "  vibrato.getInfo()                        {pos, dur, frequency, amplitude, phase, attack, release, affectedAutomations, affectedEffectAutomations}\n" +
        "  // WHICH parameter tracks this vibrato modulates, and by how much:\n" +
        "  vibrato.affectedAutomations()            {automationId: amplitude}                 (sound-source-level tracks)\n" +
        "  vibrato.affectedEffectAutomations()      {effectId: {automationId: amplitude}}     (effect-level tracks, outer key = effect.id)\n" +
        "  vibrato.setAmplitude(id, amplitude, effect?)     vibrato.removeAmplitude(id, effect?)   // pass an effect handle (same part) to target that effect's track instead of the sound source's\n" +
        "\n" +
        "effect  (an item in part.effects())\n" +
        "  field (read/write):  isEnabled (bool; false = bypass)      read-only: type, name, id, index\n" +
        "  effect.getInfo()                         {id, type, isEnabled, automations, piecewiseAutomations, properties}\n" +
        "  effect.getProperty(key)                  current value (number/boolean/string), or null if unset (defaults & keys/ranges: list_effects)\n" +
        "  effect.setProperty(key, value)           set one parameter (value = number/boolean/string)\n" +
        "  // this effect's PARAMETER AUTOMATION curves (same shape as part automation; absolute-tick points):\n" +
        "  effect.automationIds()                   [string]   (automatable param ids declared by the effect engine; see list_effects)\n" +
        "  effect.sampleAutomation(id, start, end, samples)   [number]   (NaN = no curve there)\n" +
        "  effect.setAutomation(id, start, end, points, defaultValue?)    effect.clearAutomation(id, start, end)   // points=[{tick,value}], value=absolute param value, created on demand\n" +
        "  effect.piecewiseAutomationIds()          [string]\n" +
        "  effect.samplePiecewiseAutomation(id, start, end, samples)   [number]\n" +
        "  effect.setPiecewiseAutomationLine(id, start, end, points)   effect.clearPiecewiseAutomation(id, start, end)\n" +
        "  effect.lockAutomation(id, start?, end?) -> bool    effect.hasSynthesizedParameter(id) -> bool   // exactly part.lockAutomation/hasSynthesizedParameter, scoped to this effect's own tracks\n" +
        "\n" +
        "print(x) / console.log(x) -> debugging output (returned to you / shown in the panel).\n" +
        "Notes live inside a MIDI part; to write a melody from scratch, tl.currentProject().addTrack() (or pick one), track.addPart({pos, endOffset}), then part.addNote into the returned part.\n" +
        "If the script throws, EVERYTHING rolls back (the project is left unchanged) and the error is returned, so fix the script and re-run rather than patching from a half-applied state.\n" +
        "\n" +
        "EXAMPLE — raise every note in the current part an octave and add a harmony a third above:\n" +
        "  const part = tl.currentPart();\n" +
        "  for (const n of part.notes()) {\n" +
        "    const info = n.getInfo();          // full copy of the note (properties, phonemes and all)\n" +
        "    info.pitch += 4;                   // an info is pure data — edit it freely\n" +
        "    part.addNote(info);                // third above\n" +
        "    n.pitch += 12;                     // original up an octave\n" +
        "  }\n" +
        "\n" +
        "EXAMPLE — duplicate the first track and transpose the copy up an octave:\n" +
        "  const project = tl.currentProject();\n" +
        "  const info = project.tracks()[0].getInfo();   // EVERYTHING: sound source, curves, effects, properties, phonemes\n" +
        "  info.name = 'Harmony +8';\n" +
        "  const copy = project.addTrack(info);\n" +
        "  for (const p of copy.parts()) for (const n of p.notes()) n.pitch += 12;\n" +
        "\n" +
        "EXAMPLE — move a part to another track, keeping everything (a MOVE, not a copy):\n" +
        "  const [a, b] = tl.currentProject().tracks();\n" +
        "  const p = a.parts()[0];\n" +
        "  a.removePart(p);                     // p is now detached: readable, not writable\n" +
        "  b.insertPart(p);                     // same object, now on track b\n" +
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
