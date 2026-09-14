# build-runtime-package

Assembles TuneLab "runtime" resource packages (`.tlx`, manifest `type: "runtime"` + `flavor`
+ `runtime-version` + `platforms`) that scripted extensions (Python / Node.js / Lua sidecar —
see `docs/scripted-plugin-sidecar-design.md` §8.4) probe against.

    pwsh tools/build-runtime-package/pack-runtime.ps1 -Flavor python -Version 3.12.7 -Rid win-x64

- Downloads the archive named in `runtime-sources.json`, flattens it under `runtime/`, writes
  the manifest, zips to `dist/runtime-<rid>-<flavor>-<version>.tlx` and emits a `.sha256` sidecar.
- The sha256 must be pinned into the release notes / the host's download map (semi-auto fetch
  verifies it; §8.6). Release checklist: pack per cell of the matrix, upload the `.tlx` + hash
  to the GitHub release, bump the host map.

## Layout contract

`RuntimeLocator.ResolvePackageExe` looks for `runtime/<exe>` (win) or `runtime/bin/<exe>` (unix).
`runtime-sources.json` entries carry `layout` (`root` | `prefix`) to normalize; verify the warning
about a missing exe before publishing.

## Cells we do NOT ship from python.org / nodejs.org

- `python win-arm64`: no official embeddable before 3.14 → use python-build-standalone (uv) builds.
- `python win-x86` / `node win-x86`: no official binaries at all — the cell stays open for
  user-supplied trees (`-FromDir`) and other flavors (Lua is C89-portable and can fill it).
- `lua` / `luajit`: no official zip anywhere; build from source and pack with `-FromDir`.

`-FromDir <tree>` is exactly the "user-supplied compiled runtime" mount (§8.6): same layout
contract, same sha256-on-publish discipline if you distribute it.
