# TuneLab — notes for AI coding assistants

## Build & test

- Build: `dotnet build TuneLab.sln -c Debug`
- Tests: `dotnet test tests/TuneLab.Tests/TuneLab.Tests.csproj` and `dotnet test legacy/compat/TuneLab.Hosting.Compat.Legacy.Tests/TuneLab.Hosting.Compat.Legacy.Tests.csproj`
- Sample plugins under `tests/plugins/*/` are NOT in the solution — build them individually after changing any SDK surface.

## ⚠️ Frozen public ABI: TuneLab.SDK & TuneLab.Foundation

These two assemblies are the plugin contract. Their public API is frozen and guarded by
PublicApiAnalyzers: every public signature is declared in each project's
`PublicAPI.Shipped.txt`, and **RS0016 / RS0017 build errors fire on any change**.

When you hit RS0016/RS0017, it is an alarm, not an obstacle. Do **not** mechanically edit
the txt files just to make the build green:

1. Accidental change (refactor spillover) → revert the code, leave the txt files alone.
2. Intentional **additive** API (new member/type) → plugin-implemented interfaces need a
   DIM default body; declare the signature in `PublicAPI.Unshipped.txt`
   (`dotnet format analyzers <csproj> --diagnostics RS0016`), and call out the new API
   explicitly when presenting your change.
3. Intentional **breaking** change (delete/alter a shipped signature) → **stop and ask the
   maintainer first.** Never edit `PublicAPI.Shipped.txt` on your own initiative.

Full evolution rules (interface classification, DTO shape policy, enum tolerance, release
workflow): `docs/sdk-api-evolution.md`.

Related invariants: `AssemblyVersion` of both assemblies is pinned to 2.0.0.0 forever (see
csproj comments — do not "align" it with the release version); the manifest `sdk-version`
gate (`ExtensionManager.SdkVersion`) is a separate version axis and must be bumped when
shipping new API.

## Conventions

- Code comments may be written in Chinese; log messages, assertions and exception messages
  are English.
- Do not extract csproj settings into `Directory.Build.props` — the SDK-layer csproj files
  are part of the public contract and stay self-contained.
- `agent-model` is a host-internal module (not a plugin type); new LLM adapters go in via
  PR — see `docs/agent-model-adapters.md`.
