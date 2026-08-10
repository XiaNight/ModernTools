# Versioning & Changelog Rules

This is the authoritative rule for how ModernToolset versions are formed and how
`changelog.txt` is maintained. `publish.ps1` implements the version half; the
weekday maintenance task (see `docs/maintenance/README.md`) implements the
changelog half.

## Version format

A version is the string:

```
V<Major>_<HotFix>_<Minor>
```

Each field is exactly two digits, zero-padded. Regex: `^V(\d{2})_(\d{2})_(\d{2})$`.
Example: `V02_01_04` → Major `02`, HotFix `01`, Minor `04`.

Note the field order is deliberate and non-obvious: the **middle** field is
HotFix and the **last** field is Minor.

### Significance

From most to least significant: **Major > Minor > HotFix.**

So the last field (Minor) outranks the middle field (HotFix). Two versions are
compared by Major first, then Minor, then HotFix — `V02_00_05` is newer than
`V02_01_04` because Minor `05 > 04` regardless of the HotFix digits.

### Increment & reset rules

- **HotFix++** — the smallest change (a quick hotfix). Bump HotFix; keep Major
  and Minor. Example: `V02_01_04 → V02_02_04`.
- **Minor++** — a normal change. Bump Minor; reset HotFix to `00`. The Minor
  floor is `01`, never `00`. Example: `V02_01_04 → V02_00_05`.
- **Major++** — a large/breaking change. Bump Major; reset Minor to its `01`
  floor and HotFix to `00`. Example: `V02_01_04 → V03_00_01`.

### Main vs. dev track

- **Major ≥ 90** (usually `99`) denotes a **dev build**.
- **Major < 90** denotes a **main build**.

The two tracks are versioned independently. `dist\publish-versions.json` caches
the last build on each track (`lastMainBuild`, `lastDevBuild`). When a dev build
is promoted to main, it leaves the dev track and takes the last main Major `+1`.

### Source of truth & enforcement

The version lives in `<InformationalVersion>` in
`ModernTools\ModernTools.csproj`. On publish, `publish.ps1`:

1. Reads the version from the csproj (or the `-Version` override).
2. Requires it to parse and to be strictly higher than the last recorded build
   **on the same track**; if not, it offers an auto-increment menu
   (HotFix / Minor / Major) and writes the chosen version back to the csproj.
3. After a successful build, records the version in `dist\publish-versions.json`
   under `lastMainBuild` or `lastDevBuild`.

## Changelog format

`changelog.txt` lives at the repo root and is **carried into every published
zip** (copied by `publish.ps1`). It is an internal EC-team document.

Structure: a fixed header banner, then release entries **newest first**. Each
entry is:

```
<YYYYMMDD> Modern Toolset_<Version>
01. <change>
02. <change>
...
END
```

### Voice & audience (write for the user, not the developer)

Entries describe the app from the point of view of the people who **use**
ModernToolset (the peripheral / EC engineers running the tool), not the people
who wrote the code. Each line is a plain-language, benefit-oriented statement of
what the user can now do.

- Say what changed for the user: "Added a Firmware Update page for installing
  firmware packages" — not "handling firmware via FirmwarePackage".
- No internal jargon or implementation detail: avoid class/file names, commit
  prefixes (feat/fix/refactor), commit SHAs, and mechanics like Roslyn,
  `AssemblyLoadContext`, or "JSON Schema" unless the user interacts with it
  directly.
- Lead new features with the name the user sees in the UI (Quick Scan, Protocol
  Test, AI Composer).
- Omit purely internal changes with no user-visible effect (refactors, sample-
  data tweaks, test/CI plumbing, version bumps, merge commits).
- One concise line per item, present tense.

Example — write `AI Composer: generate and save your own custom tool pages.`
rather than `runtime-generated, persisted pages via a Roslyn composer harness.`

### Ordering of changes within an entry

Changes are numbered `01.`, `02.`, … and **sorted by importance, highest
first, with fixes last**:

1. Breaking changes
2. New features
3. Improvements / changes
4. Fixes (always at the bottom)

### Which builds get an entry

- **Main build** (Major < 90): a **full** consolidated entry — every change
  accumulated since the previous main build, ordered as above.
- **Dev build** (Major ≥ 90): **noted only** — a one-line internal note in the
  maintenance log, not a formal `changelog.txt` release entry, to keep the
  shipped changelog clean.

## How it stays current (automated)

The weekday maintenance task accumulates changelog-worthy items into
`docs/maintenance/pending-changes.md` as commits land ("since last build
`V02_01_04` …"). Each run it reads `dist\publish-versions.json`; when it sees
`lastMainBuild` increase — meaning a release was built — it consolidates the
buffer into a new `changelog.txt` entry (sorted by importance, fixes last),
then resets the buffer against the new baseline. See
`docs/maintenance/README.md`.
