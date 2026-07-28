# Suite Format

The **format half** of a one-package-many-plugins test suite. It reads and writes `.tlsuite`
files by round-tripping the SDK's `ProjectInfo`.

## Why this file exists

This package declares *two* extensions, and each declares its **own** `introduction`. The detail
window therefore shows one tab per entry — this page belongs to the format entry only, and says
nothing about the voice engine next door.

## Usage

1. **File → Import** and pick any `.tlsuite` file.
2. **File → Export** writes the current project back out.

> The package-level `description` in `manifest.json` describes the *package* ("format + voice
> sharing one common dll"). This page describes *one capability*. Neither substitutes for the other.
