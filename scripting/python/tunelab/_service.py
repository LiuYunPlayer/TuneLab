"""The serve loop: initialize handshake + format.import/export dispatch (wire §5.2/§5.4)."""
import json
import os
import platform
import sys
import threading

from . import _framing
from ._props import walk_encode, walk_decode

WIRE_PROTOCOL = 1

_entries = {}        # entry id (manifest-relative path) -> {"import": fn|None, "export": fn|None}
_lock = threading.Lock()
_stdout_stream = None


def format(cls=None, *, import_fn=None, export_fn=None, entry=None):
    """Register the format implementation for one scripted entry.

    Decorator on a class exposing `import_project(stream_ref)` and/or
    `export_project(stream_ref, project)`; or pass plain functions directly.
    `entry` must match the manifest "entry" value when a package declares several.
    """
    def register(importer, exporter, key):
        _entries[key] = {"import": importer, "export": exporter}

    if cls is not None:
        inst = cls()
        importer = getattr(inst, "import_project", None)
        exporter = getattr(inst, "export_project", None)
        if importer is None and exporter is None:
            raise ValueError("tunelab.format: neither import_project nor export_project defined")
        register(importer, exporter, entry or _default_entry_id())
        return cls

    if import_fn is None and export_fn is None:
        raise ValueError("tunelab.format: nothing to register")
    register(import_fn, export_fn, entry or _default_entry_id())
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


def _handle_initialize(req):
    params = req.get("params") or {}
    declared = params.get("entries") or []
    provides = []
    for e in declared:
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
        })
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


def _dispatch(req):
    method = req.get("method")
    params = req.get("params") or {}
    req_id = req.get("id")

    if method == "initialize":
        _reply(req_id, _handle_initialize(req))
    elif method == "shutdown":
        _reply(req_id, {}) if req_id is not None else None
        os._exit(0)
    elif method == "format.import":
        entry = _entries.get(params.get("entry")) or _single()
        fn = entry and entry.get("import")
        if fn is None:
            _reply(req_id, err={"code": "bad_request", "message": "no importer for entry " + str(params.get("entry"))})
            return
        try:
            project = fn(params["stream_ref"])
            _reply(req_id, {"project": walk_encode(project)})
        except Exception as ex:
            _reply(req_id, err={"code": "runtime_error", "message": str(ex)})
    elif method == "format.export":
        entry = _entries.get(params.get("entry")) or _single()
        fn = entry and entry.get("export")
        if fn is None:
            _reply(req_id, err={"code": "bad_request", "message": "no exporter for entry " + str(params.get("entry"))})
            return
        try:
            fn(params["stream_ref"], walk_decode(params.get("project") or {}))
            _reply(req_id, {})
        except Exception as ex:
            _reply(req_id, err={"code": "runtime_error", "message": str(ex)})
    elif req_id is not None:
        _reply(req_id, err={"code": "bad_request", "message": "unknown method " + str(method)})
    # notifications without id: ignore (forward-compatible)


def _single():
    return next(iter(_entries.values())) if len(_entries) == 1 else None


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
            try:
                threading.Event().wait(2.0)
            except KeyboardInterrupt:
                return
            if os.getppid() != parent:
                os._exit(0)

    threading.Thread(target=watch, daemon=True).start()


def serve():
    """Run the protocol loop until the host's pipe closes. Never returns before that."""
    global _stdout_stream
    _stdout_stream = _framing.open_stdio()[1]
    stdin, _ = _framing.open_stdio()
    _arm_parent_death_guard()
    while True:
        try:
            msg = _framing.read_message(stdin)
        except Exception:
            os._exit(1)          # broken frame = protocol violation; let the host respawn us
        if msg is None:
            os._exit(0)          # stdin EOF = host died (defense line 1)
        _dispatch(msg)
