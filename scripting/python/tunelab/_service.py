"""The serve loop: initialize handshake + format/settings dispatch + heartbeat reply.

Wire spec: scripting/wire/v1.json (design doc §5). Each request runs on its own
worker thread so long user code (imports, synthesis later) never blocks `ping` —
the host kills us after 4 heartbeat misses otherwise. Replies serialize through the
stdout lock in _send; ordering across requests doesn't matter (id-routed).
"""
import os
import platform
import sys
import threading

from . import _framing
from ._props import walk_encode, walk_decode, sanitize_numbers, decode_map

WIRE_PROTOCOL = 1

_entries = {}          # entry id -> {"import","export","get_settings","apply_settings"}
_settings_by_entry = {}
_lock = threading.Lock()
_stdout_stream = None


def format(cls=None, *, import_fn=None, export_fn=None, entry=None,
           get_settings_fn=None, apply_settings_fn=None):
    """Register the format implementation for one scripted entry.

    Decorator on a class exposing any of `import_project(stream_ref)`,
    `export_project(stream_ref, project)`, `get_settings_config()` /
    `apply_settings(values)`; or pass plain functions directly. `entry` must
    match the manifest "entry" value when a package declares several entries.
    Settings callbacks speak the WIRE config form (plain dicts; see scripting/wire)
    and native property values for apply_settings.
    """
    def register(rec, key):
        _entries[key] = rec

    def make_rec(imp, exp, gets, applies):
        return {"import": imp, "export": exp, "get_settings": gets, "apply_settings": applies}

    if cls is not None:
        inst = cls()
        rec = make_rec(getattr(inst, "import_project", None), getattr(inst, "export_project", None),
                       getattr(inst, "get_settings_config", None), getattr(inst, "apply_settings", None))
        if rec["import"] is None and rec["export"] is None and rec["get_settings"] is None:
            raise ValueError("tunelab.format: nothing to register")
        register(rec, entry or _default_entry_id())
        return cls

    if import_fn is None and export_fn is None and get_settings_fn is None:
        raise ValueError("tunelab.format: nothing to register")
    register(make_rec(import_fn, export_fn, get_settings_fn, apply_settings_fn), entry or _default_entry_id())
    return import_fn if export_fn is None else export_fn


def _default_entry_id():
    # Manifest entries arrive keyed by the package-relative path; the host also passed us that
    # very script as argv[1] (cwd = package dir), so derive the same key.
    if len(sys.argv) > 1:
        try:
            return os.path.relpath(os.path.abspath(sys.argv[1]), os.getcwd()).replace(os.sep, "/")
        except ValueError:
            pass
    return ""


def _send(obj):
    with _lock:
        _framing.write_message(_stdout_stream, obj)


def log(level, message):
    try:
        _send({"jsonrpc": "2.0", "method": "log.write", "params": {"level": level, "message": str(message)}})
    except Exception:
        pass


def info(message):
    log("info", message)


def warn(message):
    log("warning", message)


def error(message):
    log("error", message)


def capabilities():
    """What this facade declares at handshake (inline planes only until shm lands in M3)."""
    return {"audio_plane": "inline", "curve_plane": "inline"}


def _reply(req_id, result=None, err=None):
    msg = {"jsonrpc": "2.0", "id": req_id}
    if err is not None:
        msg["error"] = err
    else:
        msg["result"] = result
    _send(msg)


def _handle_initialize(params):
    provides = []
    for e in params.get("entries") or []:
        entry_id = e.get("entry")
        handler = _entries.get(entry_id)
        if handler is None and len(_entries) == 1:
            # single-handler package: bind it to whatever the manifest declared (1 条目 = 1 实现)
            handler = next(iter(_entries.values()))
            _entries[entry_id] = handler
        if handler is None:
            continue
        provides.append({
            "type": e.get("type", "format"),
            "entry": entry_id,
            "can_import": handler.get("import") is not None,
            "can_export": handler.get("export") is not None,
            "settings": handler.get("get_settings") is not None,
        })
        settings = e.get("settings")
        if settings is not None and handler.get("apply_settings") is not None:
            try:
                handler["apply_settings"](decode_map(settings))   # 整个设置包就是一张 property map
            except Exception as ex:
                error("apply_settings(handshake) failed: %s" % ex)
    return {
        "provides": provides,
        "capabilities": capabilities(),
        "runtime": {
            "exe": sys.executable,
            "version": platform.python_version(),
            "arch": platform.machine(),
            "flavor": "python",
        },
        "facade_version": __import__("tunelab").__version__,
    }


def _find_entry(entry_id):
    e = _entries.get(entry_id)
    if e is None and len(_entries) == 1:
        e = next(iter(_entries.values()))
    return e


def _dispatch(req):
    method = req.get("method")
    params = req.get("params") or {}
    req_id = req.get("id")

    try:
        if method == "initialize":
            _reply(req_id, _handle_initialize(params))
        elif method == "ping":
            _reply(req_id, {})
        elif method == "shutdown":
            if req_id is not None:
                _reply(req_id, {})
            os._exit(0)
        elif method == "format.import":
            entry = _find_entry(params.get("entry"))
            fn = entry and entry.get("import")
            if fn is None:
                _reply(req_id, err={"code": "bad_request", "message": "no importer for entry " + str(params.get("entry"))})
                return
            _reply(req_id, {"project": walk_encode(fn(params["stream_ref"]))})
        elif method == "format.export":
            entry = _find_entry(params.get("entry"))
            fn = entry and entry.get("export")
            if fn is None:
                _reply(req_id, err={"code": "bad_request", "message": "no exporter for entry " + str(params.get("entry"))})
                return
            fn(params["stream_ref"], walk_decode(params.get("project") or {}))
            _reply(req_id, {})
        elif method == "settings.get_config":
            entry = _find_entry(params.get("entry"))
            fn = entry and entry.get("get_settings")
            if fn is None:
                _reply(req_id, err={"code": "bad_request", "message": "no settings for entry " + str(params.get("entry"))})
                return
            _reply(req_id, {"config": sanitize_numbers(fn())})
        elif method == "settings.apply":
            entry = _find_entry(params.get("entry"))
            fn = entry and entry.get("apply_settings")
            if fn is not None:
                fn(decode_map(params.get("values") or {}))
            if req_id is not None:
                _reply(req_id, {})
        elif req_id is not None:
            _reply(req_id, err={"code": "bad_request", "message": "unknown method " + str(method)})
    except Exception as ex:
        if req_id is not None:
            _reply(req_id, err={"code": "runtime_error", "message": str(ex)})
        else:
            error("while handling notification %s: %s" % (method, ex))
        return


def _arm_parent_death_guard():
    """Defense line 2's plugin-side half: exit when the host goes away, per platform."""
    import signal as _signal
    if sys.platform.startswith("linux"):
        try:
            import ctypes
            ctypes.CDLL("libc.so.6", use_errno=True).prctl(1, int(_signal.SIGTERM), 0, 0, 0)  # PR_SET_PDEATHSIG
            return
        except Exception:
            pass
    # macOS (no PDEATHSIG) & fallbacks: poll our parent pid; reparenting to init/launchd = host gone.
    parent = os.getppid()

    def watch():
        while True:
            threading.Event().wait(2.0)
            if os.getppid() != parent:
                os._exit(0)

    threading.Thread(target=watch, daemon=True).start()


def serve():
    """Run the protocol loop until the host's pipe closes. Never returns before that."""
    global _stdout_stream
    stdin, _stdout_stream = _framing.open_stdio()
    _arm_parent_death_guard()
    while True:
        try:
            msg = _framing.read_message(stdin)
        except Exception:
            os._exit(1)          # broken frame = protocol violation; let the host respawn us
        if msg is None:
            os._exit(0)          # stdin EOF = host died (defense line 1)
        threading.Thread(target=_dispatch, args=(msg,), daemon=True).start()
