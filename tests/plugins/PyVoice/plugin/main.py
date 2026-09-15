# PySine — scripted voice sample: one 440Hz sine per chunk, per-session peek progression.
# Real-interpreter E2E target (ScriptedPythonE2ETests); PYTHONPATH supplies the facade.
import math

import tunelab


@tunelab.voice("PySine")
class PySine:
    RATE = 44100

    def init(self):
        pass

    def destroy(self):
        pass

    def catalog(self):
        return [{"id": "sine", "name": "Sine Bank", "description": "440Hz test bank"}], []

    def automation_configs(self, parts):
        return []

    def synthesized_parameter_configs(self, parts):
        return []

    def part_property_config(self, parts):
        return {"type": "object", "properties": []}

    def note_property_config(self, part, notes):
        return {"type": "object", "properties": []}

    def phoneme_property_configs(self, part, notes):
        return []

    def create_session(self, ctx):
        return PySineSession(ctx)


class PySineSession:
    default_lyric = "la"

    def __init__(self, ctx):
        self.ctx = ctx
        self.pos = 0.0

    def is_continuation(self, note):
        return False

    def peek(self, start, end):
        if self.pos >= end:
            return None
        return (max(start, self.pos), min(self.pos + 0.5, end))

    def synthesize(self, start, end):
        if start < self.pos:   # 电平语义可被重问：已合成的块直接返回
            return
        notes = [n for n in self.ctx.notes() if n.end > start and n.start < end]
        snap = self.ctx.snapshot(notes)
        rate = PySine.RATE
        count = int((end - start) * rate)
        # control-rate pitch pull proves the remote evaluator path (design §5.6 remote-handle choice)
        ctrl = 64
        times = [start + (end - start) * i / (ctrl - 1) for i in range(ctrl)]
        curve = snap.pitch.evaluate(times)
        assert len(curve) == ctrl and curve[0] == 0.25
        seg = self.ctx.output(int(start * rate), count, rate)
        seg.write(0, [0.2 * math.sin(2 * math.pi * 440.0 * i / rate) for i in range(count)])
        seg.commit()
        self.ctx.status([(start, end, "synthesized", 1.0)])
        self.ctx.pitch([[(start, 60.0), (end, 62.0)]])
        self.ctx.phonemes({n.id: ([], [{"symbol": n.lyric or "a", "duration": n.end - n.start,
                                        "stretch_weight": 1.0}], 0.0) for n in snap.notes})
        self.pos = end


tunelab.serve()
