// NodeFormat sample — a .ntxt file is one note per line: "<pitch> <pos> <dur> <lyric>".
// Real-interpreter E2E target (ScriptedNodeE2ETests); NODE_PATH supplies the facade.
"use strict";
const fs = require("fs");
const { format, serve } = require("tunelab");

format({
  importProject(path) {
    const lines = fs.readFileSync(path, "utf8").split(/\r?\n/).filter((l) => l && !l.startsWith("#"));
    const notes = lines.map((l) => {
      const [p, pos, dur, ...rest] = l.split(/\s+/);
      return {
        pos: Number(pos), dur: Number(dur), pitch: Number(p), lyric: rest.join(" ") || "la",
        pronunciation: "", properties: { source: "ntxt" },
        leadingPhonemes: [], bodyPhonemes: [], bodyOffset: 0.0,
      };
    });
    return {
      tempos: [{ pos: 0.0, bpm: 120.0 }],
      timeSignatures: [{ barIndex: 0, numerator: 4, denominator: 4 }],
      tracks: [{
        name: "Node Track", gain: 0.0, pan: 0.0, mute: false, solo: false, asRefer: true, color: "",
        parts: [{
          kind: "midi", name: "P", pos: 0.0, startOffset: 0.0, endOffset: 0.0, gain: 0.0,
          soundSource: { kind: "voice", type: "", id: "" },
          effects: [], notes, automations: {}, piecewiseAutomations: {},
          pitch: { segments: [] }, vibratos: [], properties: {},
        }],
      }],
    };
  },

  exportProject(path, project) {
    const lines = [];
    for (const track of project.tracks || [])
      for (const part of track.parts || [])
        for (const note of part.notes || [])
          lines.push(`${note.pitch} ${note.pos} ${note.dur} ${note.lyric || "la"}`);
    fs.writeFileSync(path, lines.length ? lines.join("\n") + "\n" : "");
  },
});

serve();
