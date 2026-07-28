# Multi Suffix Format

One format, two file suffixes: `.mtest` and `.mtst` — the same relationship `.mid` and `.midi`
have. Import either one and you get the same two-note sample project.

## What this package is here to prove

**The unit of declaration is the format, not the suffix.** The manifest has **one** `format` entry
with `"suffixes": ["mtest", "mtst"]`. One name, one introduction, one implementation class cover
both aliases — so the detail window shows a single tab, with no need to merge look-alike pages
after the fact (a merge would have no way to choose between two differing names). The suffixes it
accepts are listed above this text, since a format's display name rarely reveals which files it opens.

**Registration and routing stay per-suffix.** Open **Settings → Extension Routing**: `.mtest` and
`.mtst` each get their own Import and Export row, so another package can take over just one of
them. The host picks an implementation by suffix when opening a file, and declaring the aliases
together does not narrow that.
