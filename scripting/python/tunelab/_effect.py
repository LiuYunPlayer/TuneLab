"""Effect engine plumbing: reverse calls to the host + per-session context (wire §5.5).

The RPC loop lives in _service; this module owns the effect-side method table and the
ProcessContext handed to user code. All byte payloads are base64 little-endian
(f32 PCM, f64 curve values) per scripting/wire/v1.json.
"""
import base64
import struct
import threading

_effect_engines = {}          # engine id -> user object
_sessions = {}                # (engine, session) -> SessionCtx
_call_host = None             # injected by _service (reverse request fn)
_notify_host = None           # injected by _service (notification fn)


def set_channels(call_host, notify_host):
    global _call_host, _notify_host
    _call_host, _notify_host = call_host, notify_host


def eval_results(r):
    """Decode a host.evaluate reply. Small batches come back as inline base64 f64;
    on the shm plane a large batch (>=64KB) comes back as {region:{path,data_offset,count}}
    pointing at a per-session curve ring — the RPC response itself is the barrier, so we
    just read the file bytes and copy them into a fresh list (§5.6/§6)."""
    reg = r.get("region")
    if reg is not None:
        count = int(reg["count"])
        with open(reg["path"], "rb") as f:
            f.seek(int(reg.get("data_offset", 24)))
            data = f.read(count * 8)
        return list(struct.unpack("<%dd" % count, data))
    data = base64.b64decode(r["values"])
    return list(struct.unpack("<%dd" % (len(data) // 8), data))


def register_effect(obj, engine_id):
    _effect_engines[engine_id] = obj


def declared_engine_ids():
    return list(_effect_engines)


def settings_engines():
    """Engine ids that expose a settings panel (get_settings_config present)."""
    return {k for k, e in _effect_engines.items() if hasattr(e, "get_settings_config")}


def handle(method, params):
    """Dispatch an effect-family request; returns the result payload dict."""
    if method == "engine.init":
        eng = _need(params)
        if hasattr(eng, "init"):
            eng.init()
        return {}
    if method == "engine.destroy":
        eng = _effect_engines.get(params.get("engine"))
        if eng is not None and hasattr(eng, "destroy"):
            eng.destroy()
        return {}
    if method == "engine.create_session":
        eng = _need(params)
        ctx = _SessionCtx(params["engine"], params["session"], eng)
        _sessions[(params["engine"], params["session"])] = ctx
        user = eng.create_session(ctx) if hasattr(eng, "create_session") else eng
        ctx.user = user
        return {}
    if method.startswith("effect.get_"):
        eng = _need(params)
        key = {"effect.get_property_config": "get_property_config",
               "effect.get_automation_configs": "get_automation_configs",
               "effect.get_synthesized_parameter_configs": "get_synthesized_parameter_configs"}[method]
        fn = getattr(eng, key, None)
        if fn is None:
            return {"config": {"type": "object", "properties": []}} if key.endswith("property_config") else {"tracks": []}
        out = fn(params.get("effects") or [])
        if key == "get_property_config":
            return {"config": out}
        return {"tracks": out}
    if method == "effect.process":
        ctx = _sessions.get((params["engine"], params["session"]))
        if ctx is None:
            raise RuntimeError("unknown session " + str(params.get("session")))
        ctx.run_process(params.get("input") or {})
        return {}
    raise UnknownMethod(method)


class UnknownMethod(Exception):
    pass


def on_notification(method, params):
    if method == "effect.cancel":
        ctx = _sessions.get((params.get("engine"), params.get("session")))
        if ctx:
            ctx.cancelled.set()
            user_cancel = getattr(ctx.user, "cancel", None)
            if user_cancel:
                try:
                    user_cancel()
                except Exception:
                    pass
    elif method == "session.dispose":
        ctx = _sessions.pop((params.get("engine"), params.get("session")), None)
        if ctx:
            ctx.dispose_segments()
            user_dispose = getattr(ctx.user, "dispose", None)
            if user_dispose:
                try:
                    user_dispose()
                except Exception:
                    pass


class _InputGeom:
    def __init__(self, d):
        self.sample_offset = d.get("sample_offset", 0)
        self.sample_count = d.get("sample_count", 0)
        self.sample_rate = d.get("sample_rate", 44100)


class _Segment:
    """Output audio segment; write takes bytes/bytearray/memoryview/numpy/list[float] (f32 samples).

    When the handshake negotiated the shm plane, segment_create carries a
    region{name,path,data_offset,...} — we map it and writes go straight into
    shared memory (the commit control message is the only barrier; the host
    copies region -> segment buffer there, so zero serialization)."""

    def __init__(self, engine, session, seg_id, count, region=None):
        self._k = (engine, session)
        self.id = seg_id
        self.count = count
        self._closed = False
        self._region = None
        self._file = None
        self._view = None
        self._data_offset = 0
        if region:
            self._map(region)

    def _map(self, region):
        import mmap
        self._region = region
        self._data_offset = int(region.get("data_offset", 24))
        self._file = open(region["path"], "r+b")
        self._view = mmap.mmap(self._file.fileno(), 0)

    def _unmap(self):
        try:
            if self._view is not None:
                self._view.close()
            if self._file is not None:
                self._file.close()
        except Exception:
            pass
        self._view = self._file = self._region = None

    def shm_view(self, sample_start=0, count=None):
        """numpy float32 view over the region body (None on the inline plane)."""
        if self._region is None:
            return None
        import numpy as np
        n = self.count if count is None else count
        mv = memoryview(self._view)[self._data_offset + sample_start * 4:
                                    self._data_offset + (sample_start + n) * 4]
        return np.ndarray(shape=(n,), dtype=np.float32, buffer=mv)

    @staticmethod
    def _to_payload(samples):
        if isinstance(samples, (bytes, bytearray)):
            return bytes(samples)
        if isinstance(samples, memoryview):
            return samples.cast("B").tobytes() if samples.format != "B" else samples.tobytes()
        if hasattr(samples, "tobytes"):          # numpy / array 等
            return samples.tobytes()
        return struct.pack("<%df" % len(samples), *samples)

    def write(self, offset, samples):
        payload = self._to_payload(samples)
        if self._region is not None:
            start = self._data_offset + offset * 4
            if offset < 0 or start + len(payload) > len(self._view):
                raise ValueError("segment_write out of range")
            self._view[start:start + len(payload)] = payload
            return
        _call_host("host.segment_write", {"engine": self._k[0], "session": self._k[1],
                                           "segment": self.id, "offset": offset,
                                           "data": base64.b64encode(payload).decode("ascii")})

    def commit(self):
        _call_host("host.segment_commit", {"engine": self._k[0], "session": self._k[1], "segment": self.id})

    def resize(self, sample_offset, sample_count):
        self.count = sample_count
        r = _call_host("host.segment_resize", {"engine": self._k[0], "session": self._k[1],
                                               "segment": self.id, "sample_offset": sample_offset,
                                               "sample_count": sample_count})
        if self._region is not None and isinstance(r, dict) and r.get("region"):
            self._unmap()          # 旧视图作废，重映射新区域（§7 身份保持、区域重铺）
            self._map(r["region"])

    def dispose(self):
        if self._closed:
            return
        self._closed = True
        self._unmap()
        _call_host("host.segment_dispose", {"engine": self._k[0], "session": self._k[1], "segment": self.id})


class _SessionCtx:
    """The ProcessContext user code receives."""

    def __init__(self, engine, session, user_engine):
        self.engine = engine
        self.session = session
        self.user = None
        self.cancelled = threading.Event()
        self._segments = []

    # -- host pulls ---------------------------------------------------------
    def _via(self, extra):
        d = {"engine": self.engine, "session": self.session}
        d.update(extra)
        return d

    def input_read(self, offset=0, count=None):
        """Read a window of the (indivisible) input segment; returns a list[float]."""
        if count is None:
            count = self.input.sample_count - offset
        r = _call_host("host.input_read", self._via({"offset": offset, "count": count}))
        data = base64.b64decode(r["data"])
        return list(struct.unpack("<%df" % (len(data) // 4), data))

    def properties(self, keys):
        r = _call_host("host.properties", self._via({"keys": list(keys)}))
        from ._props import decode_map
        return decode_map(r["values"])

    def automation_tracks(self):
        return _call_host("host.automation_tracks", self._via({}))["tracks"]

    def evaluate(self, track, positions):
        pos = base64.b64encode(struct.pack("<%dd" % len(positions), *positions)).decode("ascii")
        r = _call_host("host.evaluate", self._via({"track": track, "positions": pos}))
        return eval_results(r)

    def output(self, sample_offset, sample_count, sample_rate):
        r = _call_host("host.segment_create", self._via({"sample_offset": sample_offset,
                                                          "sample_count": sample_count, "sample_rate": sample_rate}))
        seg = _Segment(self.engine, self.session, r["segment"], sample_count, r.get("region"))
        self._segments.append(seg)
        return seg

    # -- publishing ---------------------------------------------------------
    def status(self, segments):
        """segments: list of (start, end, state, progress[, message]) with state in
        pending|synthesizing|synthesized|failed."""
        arr = []
        for s in segments:
            start, end, state = s[0], s[1], s[2]
            progress = s[3] if len(s) > 3 else 0.0
            message = s[4] if len(s) > 4 else None
            arr.append({"start": start, "end": end, "status": state, "progress": progress, "message": message})
        _notify_host("effect.status", self._via({"segments": arr}))

    def parameters(self, by_key):
        """by_key: {id: {"segments": [[[t,v],...], ...]}} — the SynthesizedParameter wire shape."""
        _notify_host("effect.parameters", self._via({"parameters": by_key}))

    # -- internals ----------------------------------------------------------
    def run_process(self, input_geom):
        self.input = _InputGeom(input_geom)
        user_process = getattr(self.user, "process", None)
        if user_process is not None:
            user_process(self)
        if self.cancelled.is_set():
            return

    def dispose_segments(self):
        for seg in self._segments:
            try:
                seg.dispose()
            except Exception:
                pass
        self._segments.clear()


def _need(params):
    eng = _effect_engines.get(params.get("engine"))
    if eng is None:
        raise RuntimeError("no such engine registered: " + str(params.get("engine")))
    return eng
