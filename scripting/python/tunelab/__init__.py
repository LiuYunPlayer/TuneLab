"""TuneLab scripted-plugin facade (pure stdlib).

Speaks the sidecar wire protocol (docs/scripted-plugin-sidecar-design.md §5, spec in
scripting/wire/v1.json): LSP-style Content-Length frames of JSON-RPC 2.0 over stdio.
Covers `format`, `effect` and `voice` entries (shm audio plane is a later fast lane;
the inline plane is always the fallback).

Usage (in the package's entry script, e.g. plugin/main.py):

    import tunelab

    @tunelab.format
    class Plugin:
        def import_project(self, stream_ref): ...   # return a project dict
        def export_project(self, stream_ref, project): ...

    tunelab.serve()
"""

from ._service import format, effect, voice, serve, info, warn, error, capabilities

__all__ = ["format", "effect", "voice", "serve", "info", "warn", "error", "capabilities"]
__version__ = "0.1.0"
