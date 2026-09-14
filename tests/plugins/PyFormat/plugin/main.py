# PyFormat sample — a .pyt file is one note per line: "<pitch> <pos> <dur> <lyric>".
#
# Real users vendor the facade (pip install --target=plugin/vendor tunelab); this test package
# gets it from PYTHONPATH (ScriptedPythonE2ETests sets it, README in this folder).
import tunelab


@tunelab.format
class PyTxtFormat:
    def import_project(self, stream_ref):
        notes = []
        with open(stream_ref, encoding="utf-8") as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                bits = line.split()
                notes.append({
                    "pos": float(bits[1]), "dur": float(bits[2]), "pitch": int(bits[0]),
                    "lyric": bits[3] if len(bits) > 3 else "la",
                    "pronunciation": "",
                    "properties": {"source": "pytxt"},
                    "leadingPhonemes": [], "bodyPhonemes": [], "bodyOffset": 0.0,
                })
        return {
            "tempos": [{"pos": 0.0, "bpm": 120.0}],
            "timeSignatures": [{"barIndex": 0, "numerator": 4, "denominator": 4}],
            "tracks": [{
                "name": "Py Track", "gain": 0.0, "pan": 0.0, "mute": False, "solo": False,
                "asRefer": True, "color": "",
                "parts": [{
                    "kind": "midi", "name": "Py Part", "pos": 0.0, "startOffset": 0.0, "endOffset": 0.0,
                    "gain": 0.0, "soundSource": {"kind": "voice", "type": "", "id": ""},
                    "effects": [], "notes": notes, "automations": {}, "piecewiseAutomations": {},
                    "pitch": {"segments": []}, "vibratos": [], "properties": {},
                }],
            }],
        }

    def export_project(self, stream_ref, project):
        lines = []
        for track in project.get("tracks", []):
            for part in track.get("parts", []):
                for note in part.get("notes", []):
                    lyric = note.get("lyric") or "la"
                    lines.append("%d %g %g %s" % (int(note["pitch"]), float(note["pos"]), float(note["dur"]), lyric))
        with open(stream_ref, "w", encoding="utf-8") as f:
            f.write("\n".join(lines) + ("\n" if lines else ""))


tunelab.serve()
