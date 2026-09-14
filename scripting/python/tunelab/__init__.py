"""TuneLab scripted-plugin facade (pure stdlib).

Speaks the sidecar wire protocol (docs/scripted-plugin-sidecar-design.md §5): LSP-style
Content-Length frames of JSON-RPC 2.0 over stdio. M1 covers `format` entries; voice/effect
decorators land with M2/M3 and will keep this public surface additive.

Usage (in the package's entry script, e.g. plugin/main.py):

    import tunelab

    @tunelab.format
    class Plugin:
        def import_project(self, stream_ref): ...   # return a project dict
        def export_project(self, stream_ref, project): ...

    tunelab.serve()
"""

from ._service import format, effect, serve, info, warn, error, capabilities

__all__ = ["format", "effect", "serve", "info", "warn", "error", "capabilities"]
__version__ = "0.1.0"
