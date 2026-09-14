"""tunelab facade unit tests: python -m unittest discover -s scripting/python/tests"""
import io
import json
import math
import os
import sys
import unittest

sys.path.insert(0, os.path.join(os.path.dirname(__file__), ".."))

from tunelab import _framing, _props  # noqa: E402


class FramingTests(unittest.TestCase):
    def test_roundtrip_utf8(self):
        buf = io.BytesIO()
        _framing.write_message(buf, {"m": "你好, NaN", "v": float("nan")})
        buf.seek(0)
        msg = _framing.read_message(buf)
        self.assertEqual(msg["m"], "你好, NaN")
        self.assertTrue(math.isnan(msg["v"]))

    def test_content_length_is_bytes_not_chars(self):
        buf = io.BytesIO()
        _framing.write_message(buf, {"s": "汉字" * 50})
        raw = buf.getvalue()
        header, _, body = raw.partition(b"\r\n\r\n")
        self.assertEqual(int(header.split(b":")[1]), len(body))

    def test_eof_returns_none(self):
        self.assertIsNone(_framing.read_message(io.BytesIO(b"")))


class PropertyCodecTests(unittest.TestCase):
    def test_tagged_roundtrip(self):
        native = {"tension": 0.5, "mode": "normal", "on": True, "off": None,
                  "list": [1, "a"], "obj": {"inner": 2.0}}
        tagged = {k: _props.encode_value(v) for k, v in native.items()}
        self.assertEqual(tagged["tension"], {"n": 0.5})
        self.assertEqual(tagged["mode"], {"s": "normal"})
        self.assertEqual(tagged["on"], {"b": True})
        self.assertIsNone(tagged["off"])
        self.assertEqual(tagged["list"], {"a": [{"n": 1}, {"s": "a"}]})
        self.assertEqual(tagged["obj"], {"o": {"inner": {"n": 2.0}}})
        back = _props.decode_map(tagged)
        self.assertEqual(back["tension"], 0.5)
        self.assertEqual(back["mode"], "normal")
        self.assertIs(back["on"], True)
        self.assertIsNone(back["off"])
        self.assertEqual(back["list"], [1.0, "a"])
        self.assertEqual(back["obj"], {"inner": 2.0})

    def test_nan_literal(self):
        raw = json.dumps(_props.encode_value(float("nan")))
        self.assertIn("NaN", raw)  # bare JSON literal, host AllowNamedFloatingPointLiterals reads it
        self.assertTrue(math.isnan(_props.decode_value(json.loads(raw, parse_constant=lambda c: float(c)))))

    def test_project_walk_encodes_only_properties_maps(self):
        project = {"tracks": [{"parts": [{"notes": [
            {"pos": 960, "dur": 480, "pitch": 60, "properties": {"tension": 0.5}},
        ]}]}]}
        out = _props.walk_encode(project)
        note = out["tracks"][0]["parts"][0]["notes"][0]
        self.assertEqual(note["properties"], {"tension": {"n": 0.5}})
        self.assertEqual(note["pitch"], 60)          # untouched
        self.assertEqual(note["pos"], 960)           # untouched (numbers ride plain in DataInfo)
        again = _props.walk_decode(out)
        n2 = again["tracks"][0]["parts"][0]["notes"][0]
        self.assertEqual(n2["properties"], {"tension": 0.5})
        self.assertEqual(n2["pitch"], 60)
        self.assertEqual(n2["pos"], 960)

    def test_plain_values_tolerated_on_read(self):
        # host always writes tagged; readers MAY accept plain natives (third-party payloads)
        self.assertEqual(_props.decode_value(1.5), 1.5)
        self.assertEqual(_props.decode_value("x"), "x")
        self.assertIs(_props.decode_value(True), True)

    def test_special_floats_never_ride_bare(self):
        # sanitize_numbers applies to whole payloads (config declarations), not just property maps
        node = {"defaultValue": float("nan"), "min": float("-inf"), "max": 1.0,
                "list": [float("inf")], "untouched": "NaN-as-text-stays"}
        out = _props.sanitize_numbers(node)
        self.assertEqual(out["defaultValue"], "NaN")
        self.assertEqual(out["min"], "-Infinity")
        self.assertEqual(out["max"], 1.0)
        self.assertEqual(out["list"], ["Infinity"])
        self.assertEqual(out["untouched"], "NaN-as-text-stays")


class ServiceTests(unittest.TestCase):
    def test_initialize_binds_single_handler_and_resends_settings(self):
        import tunelab._service as svc
        applied = {}
        svc._entries.clear()
        svc._entries[""] = {"import": None, "export": None,
                            "get_settings": lambda: {"type": "object", "properties": []},
                            "apply_settings": lambda v: applied.update(v)}
        reply = svc._handle_initialize({"entries": [{
            "type": "format", "entry": "plugin/main.py",
            "settings": {"model": {"n": 1.0}},
        }]})
        self.assertEqual([p["entry"] for p in reply["provides"]], ["plugin/main.py"])
        self.assertTrue(reply["provides"][0]["settings"])
        self.assertEqual(applied, {"model": 1.0})            # tagged 解回原生后灌给插件

    def test_ping_replies_with_empty_result(self):
        import tunelab._service as svc
        sent = []
        original, svc._send = svc._send, sent.append
        try:
            svc._dispatch({"jsonrpc": "2.0", "id": "p1", "method": "ping", "params": {}})
        finally:
            svc._send = original
        self.assertEqual(sent[0]["id"], "p1")
        self.assertEqual(sent[0]["result"], {})


class EffectChannelTests(unittest.TestCase):
    """Reverse-call formatting: f32/f64 base64-LE payloads & the published shapes."""

    def setUp(self):
        from tunelab import _effect
        self.calls = []
        self.notifs = []
        _effect.set_channels(lambda m, p: self._respond(m, p), lambda m, p: self.notifs.append((m, p)))
        self._effect = _effect

    def tearDown(self):
        from tunelab import _effect
        _effect.set_channels(None, None)

    def _respond(self, method, params):
        import base64, struct
        self.calls.append((method, params))
        if method == "host.input_read":
            n = params["count"]
            return {"data": base64.b64encode(struct.pack("<%df" % n, *[0.5] * n)).decode()}
        if method == "host.evaluate":
            raw = base64.b64decode(params["positions"])
            k = len(raw) // 8
            return {"values": base64.b64encode(struct.pack("<%dd" % k, *([0.25] * k))).decode()}
        if method == "host.properties":
            return {"values": {"amount": {"n": 0.5}}}
        if method == "host.segment_create":
            return {"segment": "seg1"}
        return {}

    def test_roundtrip_through_ctx(self):
        import struct
        ctx = self._effect._SessionCtx("Eng", "s1", None)
        ctx.run_process({"sample_offset": 44100, "sample_count": 4, "sample_rate": 44100})
        self.assertEqual(ctx.input_read(0, 4), [0.5, 0.5, 0.5, 0.5])
        self.assertEqual(ctx.properties(["amount"]), {"amount": 0.5})
        seg = ctx.output(0, 2, 44100)
        seg.write(0, [1.0, -1.0])
        write = [c for c in self.calls if c[0] == "host.segment_write"][0][1]
        self.assertEqual(struct.unpack("<2f", __import__("base64").b64decode(write["data"])), (1.0, -1.0))
        ctx.status([(0.0, 1.0, "synthesized", 1.0)])
        name, payload = self.notifs[0]
        self.assertEqual(name, "effect.status")
        self.assertEqual(payload["segments"][0]["status"], "synthesized")
        self.assertEqual(payload["engine"], "Eng")
        vals = ctx.evaluate("intensity", [0.0, 1.0])
        self.assertEqual(vals, [0.25, 0.25])


if __name__ == "__main__":
    unittest.main()
