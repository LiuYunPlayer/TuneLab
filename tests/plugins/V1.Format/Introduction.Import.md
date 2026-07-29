# V1 Test Format — Import

Reads `.tltest`, a small JSON project format. A file that is empty or unparsable falls back to a
built-in sample project, so a manual test always ends up with visible notes.

## What this entry is here to prove

**Import and export are separate entries.** This package declares `format-import` and
`format-export` as two entries, each with its own implementation class, its own name, its own
introduction — this page — and, importantly, **its own settings**.

Open **Settings → Extensions**: this entry has one field, *Fallback Track Name*, which names the
track of the fallback sample project. The export entry has a completely different field (*Indent
Output*). Changing one never affects the other, because each entry owns its own settings bucket.

Had the two classes been declared as a single `format` entry, there would be only one bucket — and
one of the two schemas would have nowhere to live. That is exactly why `format` requires a single
class implementing both interfaces, and why two implementations must be declared as two entries.
