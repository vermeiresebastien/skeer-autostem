# Agent rules for this repo

## What this repo ships

A .NET 10 WPF batch app plus a headless CLI that drive Ableton Live's stem separation.
**There is no plugin here.** Nothing in this repo builds or installs a VST3, so
`grep -riE "vst3|juce"` returns nothing but this file and the two compiled setup
executables under `artifacts/` and `launch/`. If it ever starts matching source, this
section is stale and the product changed.

## Plugin placement: one destination, everywhere

Applies to any Skeer installer, including this one if a bundle is ever added.

- A VST3 goes **only** to `C:\Program Files\Common Files\VST3`. Never
  `%USERPROFILE%\Documents\VST3`, never `%LOCALAPPDATA%\Programs\Common\VST3`. The machine
  folder is the only one every host scans; a second copy is not convenience, it is a
  duplicate browser entry plus two bundles that drift apart after a rebuild.
- Exactly **one** `[Files]` line per bundle, with **no `Check=` and no `Tasks=`** on it. A
  conditional there is precisely how a second copy comes back.
- A `[Tasks]` entry that copies a plugin into a DAW folder is **deleted**, not left
  unchecked. An unchecked task is still offered, still remembered by Inno across upgrades,
  and still one careless `/VERYSILENT` away from firing.
- Retired destinations stay listed in `[UninstallDelete]`. After removing a fallback,
  nothing else owns the copies an older build of this installer wrote.
- Do **not** use `[InstallDelete]` for those paths when a `/PORTABLE=1` verification run
  exists: portable mode still executes InstallDelete, so the "verification" deletes real
  files outside its own temp directory.

## This repo's installer is per-user on purpose

`PrivilegesRequired=lowest` (`packaging/SkeerAutoSTEM.iss:40`) and every file class under
`Root: HKCU` (`packaging/SkeerAutoSTEM.iss:66-80`) are a decision, not an oversight. A
machine-wide `.wav` handler would replace this desk's player for every user, and the HKCU
default plus `OpenWithProgids` split is the only way an installer can be offered on `.mp3`
without stealing it.

- Do not flip it to `PrivilegesRequired=admin` because another repo needs elevation to
  write its plugin folder. This one has no machine-wide file to place.
- If it ever does become machine-wide, the registry classes move to `Root: HKLM` in the
  same change, and any sample/library payload moves from `%LOCALAPPDATA%` to
  `{commonappdata}`. Elevated processes must not write per-user areas.
- Never silence Inno's "PrivilegesRequired is set to admin but per-user areas are used"
  with `UsedUserAreasWarning=no`. Fix the causes.

## Audit smell for any .iss

Grep it for: `{userdocs}`, `Programs\Common\VST3`, `PrivilegesRequired=lowest`,
`PrivilegesRequiredOverridesAllowed`, and any `[Tasks]` `Description` mentioning copying
the plugin into a DAW folder.

Then count what is actually on disk in all three VST3 folders and hash the module inside
each bundle. The script is a claim; the disk is evidence, and the two disagree more often
than either README admits.
