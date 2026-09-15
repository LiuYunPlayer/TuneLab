"""Voice engine plumbing: declarative surface + scheduling + snapshot pulls (wire §5 voice).

User code registers a class with @tunelab.voice("EngineId"). Required members:

  catalog()        -> (sources, layout)  sources: [{"id","name","description"}]
                                             layout: optional [{"voice":id}|{"name":g,"group":[...]}]
  automation_configs(parts)                -> [{id,displayText?,config}]      (wire automation shape)
  synthesized_parameter_configs(parts)      -> [{id,...}]
  part_property_config(parts)               -> config dict
  note_property_config(part, notes)         -> config dict
  phoneme_property_configs(part, notes)     -> [{slot,config}]  (or {} — empty slots)
  create_session(voice_id)                  -> session object

  session members: default_lyric (attr), is_continuation(note_view)->bool,
  peek(start,end)->(s,e)|None, synthesize(start,end) (level semantics; pull truth via the ctx below),
  optional cancel(), dispose(), on_dirty(kind, payload)
"""
import base64
import struct
import threading

_voice_engines = {}        # engine id -> instance
_sessions = {}             # session key -> VoiceCtx

_call_host = None
_notify_host = None


def set_channels(call_host, notify_host):
    global _call_host, _notify_host
    _call_host, _notify_host = call_host, notify_host


def register_voice(obj, engine_id):
    _voice_engines[engine_id] = obj


def declared_engine_ids():
    return list(_voice_engines)


def settings_engines():
    return {k for k, e in _voice_engines.items() if hasattr(e, "get_settings_config")}


def owns(session_key):
    return session_key in _sessions


def create_session(params):
    engine = _voice_engines.get(params["engine"])
    if engine is None:
        raise RuntimeError("no such voice engine " + str(params.get("engine")))
    ctx = VoiceCtx(params["engine"], params["session"], params.get("voice_id", ""))
    user = engine.create_session(ctx) if hasattr(engine, "create_session") else engine
    ctx.user = user
    _sessions[ctx.key] = ctx
    return ctx


def destroy_session(params):
    ctx = _sessions.pop((params.get("session")), None)
    if ctx:
        ctx.dispose()


def on_dirty(params):
    ctx = _sessions.get(params.get("session"))
    if ctx and ctx.user is not None:
        hook = getattr(ctx.user, "on_dirty", None)
        if hook:
            try:
                hook(params.get("kind"), params)
            except Exception:
                pass


def on_cancel(params):
    ctx = _sessions.get(params.get("session"))
    if ctx:
        ctx.cancelled.set()
        hook = getattr(ctx.user, "cancel", None)
        if hook:
            try:
                hook()
            except Exception:
                pass


def handle(method, params):
    engine_id = params.get("engine")
    if method == "engine.init":
        fn = getattr(_need(engine_id), "init", None)
        if fn:
            fn()
        return {}
    if method == "engine.destroy":
        fn = getattr(_need(engine_id), "destroy", None)
        if fn:
            fn()
        return {}
    if method == "voice.get_catalog":
        sources, layout = _coerce_catalog(_need(engine_id).catalog())
        return {"sources": sources, "layout": layout}
    if method == "voice.get_automation_configs":
        return {"tracks": _need(engine_id).automation_configs(params.get("parts") or [])}
    if method == "voice.get_synthesized_parameter_configs":
        fn = getattr(_need(engine_id), "synthesized_parameter_configs", None)
        return {"tracks": fn(params.get("parts") or []) if fn else []}
    if method == "voice.get_part_property_config":
        return {"config": _need(engine_id).part_property_config(params.get("parts") or [])}
    if method == "voice.get_note_property_config":
        eng = _need(engine_id)
        return {"config": eng.note_property_config(params.get("part") or {}, params.get("notes") or [])}
    if method == "voice.get_phoneme_property_configs":
        eng = _need(engine_id)
        fn = getattr(eng, "phoneme_property_configs", None)
        slots = fn(params.get("part") or {}, params.get("notes") or []) if fn else []
        return {"slots": slots}
    if method == "voice.peek":
        ctx = _sessions.get(params["session"])
        r = ctx.user.peek(params["start"], params["end"])
        return {"range": list(r) if r else None}
    if method == "voice.default_lyric":
        return {"lyric": getattr(_sessions[params["session"]].user, "default_lyric", "la")}
    if method == "voice.is_continuation":
        note = _note_from(params["note"])
        return {"continuation": bool(_sessions[params["session"]].user.is_continuation(note))}
    if method == "voice.synthesize":
        ctx = _sessions[params["session"]]
        ctx.user.synthesize(params["start"], params["end"])
        return {}
    raise VoiceNotHandled(method)


class VoiceNotHandled(Exception):
    pass


def _coerce_catalog(result):
    if isinstance(result, tuple) and len(result) == 2:
        return list(result[0]), list(result[1] or [])
    return list(result), []


def _need(engine_id):
    eng = _voice_engines.get(engine_id)
    if eng is None:
        raise RuntimeError("no such engine: " + str(engine_id))
    return eng


class NoteView:
    """A pulled note (plain values; pinned phonemes included; properties are natives)."""
    def __init__(self, d):
        from ._props import decode_map
        self.id = d.get("id", "")
        self.start = d["start"]
        self.end = d["end"]
        self.pitch = d["pitch"]
        self.lyric = d["lyric"]
        self.body_offset = d.get("body_offset", 0.0)
        self.leading = [PhonemeView(p) for p in d.get("leading", [])]
        self.body = [PhonemeView(p) for p in d.get("body", [])]
        self.properties = decode_map(d.get("properties") or {})


class PhonemeView:
    def __init__(self, d):
        from ._props import decode_map
        self.symbol = d["symbol"]
        self.duration = d.get("duration", 0.0)
        self.stretch_weight = d.get("stretch_weight", 0.0)
        self.properties = decode_map(d.get("properties") or {})


def _note_from(d):
    return NoteView(d)


class Evaluator:
    """Remote automation evaluator: evaluate(positions) pulls the host's interpolators."""
    def __init__(self, key, track):
        self._key = key
        self.track = track

    def evaluate(self, positions):
        pos = base64.b64encode(struct.pack("<%dd" % len(positions), *positions)).decode()
        r = _call_host("host.evaluate", {"engine": self._key[0], "session": self._key[1],
                                          "track": self.track, "positions": pos})
        data = base64.b64decode(r["values"])
        return list(struct.unpack("<%dd" % (len(data) // 8), data))


class Snapshot:
    def __init__(self, engine, session, data):
        from ._props import decode_map
        self.key = (engine, session)
        self.notes = [NoteView(n) for n in data["snapshot"]["notes"]]
        self.part_properties = decode_map(data["snapshot"].get("part_properties") or {})
        self.pitch = Evaluator(self.key, "pitch")
        self.pitch_deviation = Evaluator(self.key, "pitch_deviation")
        self.automations = {t: Evaluator(self.key, t) for t in data["snapshot"].get("automations", [])}


class VoiceCtx:
    """The API handed to user sessions (mirrors IVoiceSynthesisSession semantics)."""

    def __init__(self, engine, key, voice_id):
        self.engine = engine
        self.key = key
        self.voice_id = voice_id
        self.user = None
        self.cancelled = threading.Event()
        self._segments = []

    def _via(self, extra=None):
        d = {"engine": self.engine, "session": self.key}
        if extra:
            d.update(extra)
        return d

    # pulls ------------------------------------------------------------------
    def notes(self):
        return [NoteView(n) for n in _call_host("host.notes", self._via())["notes"]]

    def snapshot(self, note_ids):
        """note_ids: [NoteView | id-string] — the host returns the snapshot in request order."""
        ids = [getattr(n, "id", n) for n in note_ids]
        data = _call_host("host.snapshot", self._via({"note_ids": ids}))
        return Snapshot(self.engine, self.key, data)

    def part_properties(self, keys):
        r = _call_host("host.properties", self._via({"keys": list(keys)}))
        from ._props import decode_map
        return decode_map(r["values"])

    # output -----------------------------------------------------------------
    def output(self, sample_offset, sample_count, sample_rate):
        r = _call_host("host.segment_create", self._via({"sample_offset": sample_offset,
                                                         "sample_count": sample_count, "sample_rate": sample_rate}))
        from ._effect import _Segment
        seg = _Segment(self.engine, self.key, r["segment"], sample_count)
        self._segments.append(seg)
        return seg

    # publishing --------------------------------------------------------------
    def status(self, segments):
        _notify_host("voice.status", self._via({"segments": [_status_dict(s) for s in segments]}))

    def pitch(self, segments):
        """segments: [ [(t,hz), ...], ... ]"""
        _notify_host("voice.pitch", self._via({"segments": [[list(p) for p in seg] for seg in segments]}))

    def parameters(self, by_key):
        _notify_host("voice.parameters", self._via({"parameters": by_key}))

    def phonemes(self, by_note_id):
        """by_note_id: {note_id: (leading, body, body_offset)} with phoneme dicts {symbol,duration,stretch_weight}"""
        out = {}
        for nid, syl in by_note_id.items():
            leading, body = syl[0], syl[1]
            offset = syl[2] if len(syl) > 2 else 0.0
            out[nid] = {"leading": list(leading), "body": list(body), "body_offset": offset}
        _notify_host("voice.phonemes", self._via({"syllables": out}))

    def dispose(self):
        for seg in self._segments:
            try:
                seg.dispose()
            except Exception:
                pass
        self._segments.clear()


def _status_dict(s):
    start, end, state = s[0], s[1], s[2]
    progress = s[3] if len(s) > 3 else 0.0
    message = s[4] if len(s) > 4 else None
    return {"start": start, "end": end, "status": state, "progress": progress, "message": message}


import base64  # noqa: E402  (used by Evaluator)
