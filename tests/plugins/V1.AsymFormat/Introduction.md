# Asymmetric Format

One format, two suffixes to read (`.asym`, `.asymx`), one suffix to write (`.asymx`) — the shape
`.mid` / `.midi` has: read whatever the user happens to have, write the one canonical extension.

## What this package is here to prove

**A direction can be narrowed without splitting the entry.** The manifest has a single `format`
entry with `"suffixes": ["asym", "asymx"]` plus `"export-suffixes": ["asymx"]`. Check the menus:
**Import** offers both `.asym` and `.asymx`, **Export** offers only `.asymx`.

**Narrowing a direction is not the same as having two implementations.** The class here implements
both interfaces, so this stays one entry — one name, one introduction, one settings bucket. Writing
it as `format-import` + `format-export` would split one implementation into two of everything, for
no reason other than the suffix lists differing.

The reverse case (`import-suffixes`) exists too and works the same way; it is just far rarer, since
a format you can write is normally a format you can also read.
