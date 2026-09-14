# PyGain sample effect — multiplies a whole input segment by a fixed gain.
# Real-interpreter E2E target (ScriptedPythonE2ETests); PYTHONPATH supplies the facade.
import tunelab


@tunelab.effect("PyGain")
class PyGain:
    def init(self):
        self.gain = 0.5

    def destroy(self):
        self.gain = 0.0

    def get_property_config(self, effects):
        return {"type": "object", "properties": [{
            "id": "amount",
            "config": {"type": "slider", "defaultValue": 0.5,
                       "scale": {"kind": "linear", "min": 0.0, "max": 1.0}},
        }]}

    def get_automation_configs(self, effects):
        return []

    def get_synthesized_parameter_configs(self, effects):
        return []

    def create_session(self, ctx):
        return _Session(self)


class _Session:
    def __init__(self, engine):
        self.engine = engine

    def process(self, ctx):
        g = ctx.input
        data = ctx.input_read(0, g.sample_count)
        seg = ctx.output(g.sample_offset, g.sample_count, g.sample_rate)
        seg.write(0, [x * self.engine.gain for x in data])
        seg.commit()
        t0 = g.sample_offset / g.sample_rate
        t1 = (g.sample_offset + g.sample_count) / g.sample_rate
        ctx.status([(t0, t1, "synthesized", 1.0)])
        ctx.parameters({"loudness": {"segments": [[[t0, 0.4], [t1, 0.4]]]}})

    def cancel(self):
        pass

    def dispose(self):
        pass


tunelab.serve()
