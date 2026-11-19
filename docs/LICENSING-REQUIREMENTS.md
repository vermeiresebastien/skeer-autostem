# Licensing and system-requirement gate

Findings for **Skeer autoSTEM** (internal name StemForge). Written 2026-11-16 by the Research
member, as a read-only investigation. Nothing else in the repo was modified.

**Labels used**

| Label | Meaning |
| --- | --- |
| **verified** | I fetched or read the primary source in this session and quote it below. |
| **inferred** | Consistent with sources I read, but the source does not say it in those words. |
| **blocked** | The source that would settle it could not be reached or does not exist. **Not a licence, not a permission.** |

**Repo facts quoted from the working tree on 2026-11-16.** Another member is editing `README.md` and
`docs/` right now, so every `file:line` below must be re-located by its quoted wording, not by the
number.

---

## 1. Minimum Ableton Live version and edition for stem separation — **VERIFIED: Live 12.3, Suite only**

**Verdict.** The floor the product states is wrong by two minor versions and is missing an edition
condition. Stem separation arrived in **Live 12.3** and is in **Suite**; it is not in Standard, Intro
or Lite. `12.1` has never been substantiated by anything in this repo or on this machine, and no
Ableton page I could read supports it.

**Verified from Ableton's own pages (fetched in this session).**

- Release notes, `https://www.ableton.com/en/release-notes/live-12/` — the sentence
  **"Live Suite now features built-in stem separation that runs locally."** sits under the page's
  heading **"12.3 Release Notes"**. The same page carries headings only from **12.3** through
  **12.4.16**: there is **no 12.0 or 12.1 section on it** and nothing dated 12.1 mentions separation.
  Two facts follow: the feature is attributed to 12.3, and it is attributed to **Suite** in the
  sentence itself.
- Blog, `https://www.ableton.com/en/blog/live-12-3-is-here/` (published **25 November 2025**):
  **"With Stem Separation in Live 12 Suite, you can instantly split vocals…"**
- Reference manual, `https://www.ableton.com/en/live-manual/12/stem-separation/` (fetched): describes
  the command and its use, and states it is invoked by the **"Separate Stems to New Audio Tracks
  command in the context menu or the Create menu"**. This page carries **no edition statement and no
  version-introduced statement**, so it neither supports nor contradicts 12.1 — it is silent.
- Editions comparison, `https://www.ableton.com/en/live/compare-editions/` (fetched): **does not
  mention stem separation at all.** It cannot be cited either way.

**Corroborating text I could not fetch directly — labelled accordingly.** Ableton's Help Center
article *"Stem Separation in Ableton Live FAQ"*
(`https://help.ableton.com/hc/en-us/articles/23730994755996-Stem-Separation-in-Ableton-Live-FAQ`)
returns **403** to this machine, so I did not read the page. Its own text, as indexed:
**"Stem Separation is available in Live 12 Suite, starting with Live 12.3. It is not available in
Live Standard, Intro, or Lite."** and **"Stem Separation, introduced in Live 12.3 Suite, uses machine
learning…"**. The 12.3 + Suite part is independently verified from the two fetched pages above; the
**explicit exclusion of Standard, Intro and Lite rests on the unfetchable FAQ**, so treat that
sentence as *verified-from-Ableton-text,-page-fetch-blocked*. It agrees with the fetched pages and
with the word "Suite" in Ableton's own release note.

**What this machine can and cannot prove.**

- `%APPDATA%\Ableton\` holds preference folders for **12.0.10, 12.1, 12.3, 12.3.1, 12.3.2, 12.3.5** —
  those versions were run here at some point. Preference folders say nothing about features.
- Only one Live program tree exists: `C:\ProgramData\Ableton\Live 12 Suite\Program\Ableton Live 12
  Suite.exe`, whose file version I read as **12.3.2**, plus the update-staging copy
  `C:\ProgramData\Ableton\.Live 12 Suite_updated\…\Ableton Live 12 Suite.exe` at **12.3.5**. **No 12.1
  program files remain on disk**, so the absence of separation in 12.1 cannot be demonstrated
  locally, and the app data records this (`settings.json`: `"LiveVersion": "12.3.2"`,
  `"LiveEdition": "Suite"`).
- **Neither exe names its edition in its own version info.** Read 2026-11-16 from file metadata, with
  Live not started: both `Ableton Live 12 Suite.exe` copies report `ProductName = "Live"` (with
  `FileVersion` = `ProductVersion` = 12.3.2 and 12.3.5). The `"Suite"` in `settings.json` therefore
  comes only from the **folder and file names** Ableton's installer chose — which makes the shipped
  edition check name matching, not a licence read, and makes an install relocated outside Ableton's
  naming indistinguishable from an unknown edition. Anything the product claims about what it can
  *detect* has to be worded against that, not against the requirement.
- The 12.3.2 install does carry the separation assets — `Program/onnxruntime_abl.dll`,
  `Program/Push/assets/Images/stem_separation_{vocals,drums,bass,others}.png`, and the What's New
  lesson image `…/1-What's New Lessons/StemSeparation.png`. Presence in 12.3.2 proves only
  presence in 12.3.2.

**Consequence inside this repo.** The gate is version-only and the version is wrong:

> Superseded in code by the work that followed this pass, on this branch: the floor is now 12.3 with
> a Suite condition, and `LiveInstall` states both (see §5's note below). Everything under this line
> is what the code said **when this was read**, and the `file:line` entries are that snapshot.

- `src/StemForge.Core/Live/LiveInstall.cs:8-9` — comment *"Stem separation ("Separate Stems") was
  added in Live 12.1."* and `MinimumStemSeparationVersion = new(12, 1)`.
- `LiveInstall.cs:14-15` compares the version and nothing else; `Edition` (`LiveInstall.cs:6`) is
  **never compared anywhere in `src/`, `tools/` or `tests/`** — it is only stored and displayed
  (`MainViewModel.cs:143, 893, 916, 943`). So a **Live 12.3 Standard** install passes the gate, the
  run starts, and the failure appears later as a greyed-out or missing Create-menu entry.
- The false floor is repeated in two user-facing strings: `MainViewModel.cs:699-700`
  (*"Live 12.1 or newer is required."*) and `LiveAutomationRunner.cs:71` (same wording thrown as
  `LiveVersionUnsupported`).
- The README and the setup guide repeat it too — see section 5.

**Blocked sub-question.** Whether Live 12.3+ requires an **additional download or extra content** for
stem separation could not be confirmed. The fetched manual, blog and release-notes pages do not say;
the FAQ that would answer it is unfetchable. Do not claim either way.

**Confidence: high** on "12.3, Suite, not below 12.3, not Intro/Standard/Lite". **Zero** on the
download question.

---

## 2. Self-contained .NET 10 redistribution, and the cost of an unsigned installer

### 2a. Notices duty — **VERIFIED**

What the runtime itself asks for. `https://raw.githubusercontent.com/dotnet/runtime/main/LICENSE.TXT`,
read directly, begins:

> "The MIT License (MIT)"
> "Copyright (c) .NET Foundation and Contributors"
> "Permission is hereby granted, free of charge, to any person obtaining a copy of this software…"

MIT conditions are notice preservation, which is what the two files beside the executables provide.
The shipped app is self-contained on two runtime packs, and each pack carries its own texts:

| Runtime pack (both 10.0.12 on this machine) | Files in the pack |
| --- | --- |
| `microsoft.netcore.app.runtime.win-x64` | `LICENSE.TXT` (1139 B, sha256 `d7a68596…08cf5b`), `THIRD-PARTY-NOTICES.TXT` (78041 B, sha256 `6d15e10a…148a21`) |
| `microsoft.windowsdesktop.app.runtime.win-x64` | `LICENSE` (1137 B, sha256 `a8988666…4d01ab`), **no** third-party notices file |

`build.ps1:87-99` copies the **core** pack's `THIRD-PARTY-NOTICES.TXT` to
`ThirdPartyNotices.dotnet.txt` and the **desktop** pack's `LICENSE` to `LICENSE.dotnet-runtime.txt`.
I hashed the produced files: `launch/LICENSE.dotnet-runtime.txt`,
`launch/ThirdPartyNotices.dotnet.txt` and the two copies under `artifacts/SkeerAutoSTEM/` are
**byte-identical to the pack files above** (same SHA-256). `diff` of the two licence texts shows they
differ only by a missing final newline — the same MIT body, so shipping the desktop pack's copy
satisfies the core runtime's notice too. **The claim in `README.md:139-141` is true today.**

What else ships with the product: **nothing**. No shipped project has a `PackageReference`
(`src/StemForge.App`, `src/StemForge.Core`, `tools/StemForge.Cli` have none; only
`tests/StemForge.Tests/StemForge.Tests.csproj` does — `Microsoft.NET.Test.Sdk 17.11.1` (MIT),
`xunit 2.9.2` and `xunit.runner.visualstudio 2.8.2`; the xunit licence file I read is
**Apache-2.0** with one file under MIT). Test packages are not redistributed, so the .NET runtime is
the only third-party payload in the box. Inno Setup compiles the installer on our machine and its
output does not embed the compiler, so no Inno notice travels — **but Inno Setup's own licence terms
are BLOCKED here**: `https://jrsoftware.org/copying.php` returned **403** and
`https://jrsoftware.org/ishelp/index.php?topic=intro` could not be parsed. That is an unfetched page,
not a permission; if we ever redistribute Inno files rather than only its output, this must be read
first.

**Two real gaps, both verified locally.**

1. **The product has a licence of its own.** A root `LICENSE` (MIT, Copyright (c) 2026 Skeer) is
   tracked, `packaging/SkeerAutoSTEM.iss` sets `LicenseFile=..\LICENSE` so setup displays it, and
   `{app}\LICENSE` travels with both executables. `docs/SEPARATION-ENGINES.md:497-503` flagged the
   old absence; the MIT grant covers our code, not the .NET runtime notices and not any engine
   weights, which do not ship.
2. **Notices are reachable but thin.** The installer puts both texts in `{app}` and adds one Start
   menu shortcut, `{group}\Third-party notices (.NET runtime)`
   (`packaging/SkeerAutoSTEM.iss:55-56, 60`). An "About" surface or an EULA step would be the normal
   carrier; the shortcut is inside Microsoft's notice-preservation allowance, so this is a polish
   item, not a breach.

### 2b. Signing — **VERIFIED UNSIGNED, no tooling for it exists here**

`Get-AuthenticodeSignature` on all three produced binaries returns **`NotSigned`** with an empty
signer certificate: `launch/SkeerAutoSTEM.exe` (76 065 645 B), `launch/autostem.exe` (75 501 684 B),
`launch/Skeer_autoSTEM_Setup_v1.1.0.exe` (138 892 793 B). A repo-wide search for
`signtool|authenticode|codesign|certificate|thumbprint` returns **no match in any source, script or
`.iss`** — there is no signing step to fail silently, which confirms the PM's belief.

A provenance fact worth having: the installer in `launch/` and `artifacts/installers/` is dated
**2026-11-15 23:47**, while both executables inside `artifacts/SkeerAutoSTEM/` are dated
**2026-11-16 03:33**. **The setup file that would be shipped does not contain the binaries that are
currently built**, and nothing in the tree records which commit produced it.

### 2c. What unsigned + per-user costs — two short lists

**For the user.**

- A **SmartScreen warning on first run**, worded as a risk rather than a lie. Microsoft's own text:
  SmartScreen checks "downloaded files against a list of files that are well known and downloaded
  frequently. **If the file isn't on that list, Microsoft Defender SmartScreen shows a warning,
  advising caution**", and "If a URL, a file, an app, or a certificate has an established reputation,
  users don't see any warnings. **If there's no reputation, the item is marked as a higher risk and
  presents a warning to the user**"
  (`https://learn.microsoft.com/en-us/windows/security/operating-system-security/virus-and-threat-protection/microsoft-defender-smartscreen/`,
  fetched). A 139 MB single-file installer is precisely an unreputed file.
- **"More info" → "Run anyway" is the only path**, and it is a step at which a first-time user
  reasonably stops, or a colleague in a managed environment genuinely cannot pass.
- **Per-user install scope by design** (`PrivilegesRequired=lowest`, `packaging/SkeerAutoSTEM.iss:40`,
  registry under `Root: HKCU`, lines 66-80): it installs for the current account only, so a second
  account on the same PC sees neither the app nor the `.wav` association. This is a deliberate
  decision recorded in `AGENTS.md`, **not** something to change.
- SmartScreen does **not** check files from internal shares ("It doesn't protect against malicious
  files on internal locations or network shares", same fetched page), so handing the setup to someone
  over a UNC share hides the warning but also skips the check they would otherwise get.
- A **large self-contained single file is unpacked at run time**, which is the shape heuristics flag
  most often; expect AV false positives and support tickets that are not our bug. **Inferred**:
  Microsoft documents the reputation mechanism, not a self-specific heuristic claim.

**For us.**

- **No reproducible provenance for a shipped binary.** With no signature and no recorded source
  commit, a copy of `Skeer_autoSTEM_Setup_v1.1.0.exe` in the wild cannot be tied to a tree — and the
  stale-installer timestamp above shows how easily that happens.
- **No anti-tamper story.** Anyone who edits the payload is undetectable by the user's own tools.
- **Reputation is a shared, non-transferable asset**: each rebuild changes the file hash, so
  SmartScreen reputation has to be earned again per binary and per URL; we cannot "buy" it once.
- **We are limited to the free remediation**: submitting the file to Microsoft
  (`https://www.microsoft.com/wdsi/filesubmission/`, selected from the same fetched page, choosing
  *Microsoft Defender SmartScreen*), plus a stable download URL and a published hash.
- **We cannot install machine-wide without a redesign** (registry classes move to `Root: HKLM`, the
  payload leaves `%LOCALAPPDATA%`), so a corporate request for "one install for all users" is a
  rework, not a checkbox.
- The `.wav` association is **HKCU `OpenWithProgids` plus a user default** (`SkeerAutoSTEM.iss:66-80`).
  On a clean machine that has never seen our installer, nothing associates; on a machine where the
  user chose another handler, we do not win it and must not claim we do.
- **Pricing/licensing consequence:** shipping **no licence for our own code** while shipping a
  139 MB unsigned binary is the worst combination — nothing tells the user what they received, from
  whom, or on what terms.

**No certificate is a decision, not a defect** — but section 5 lists the marketing sentences it makes
unavailable.

---

## 3. Demucs weights and MUSDB18 across the four shipping options

Three layers, kept separate. All three were read from primary sources in this session.

| Layer | Terms as written | Source |
| --- | --- | --- |
| **Demucs code** (`facebookresearch/demucs`) | MIT. README: **"Demucs is released under the MIT license as found in the LICENSE file."** `LICENSE` is MIT, copyright **Meta Platforms, Inc.**, covering *"this software and associated documentation files"*. | `https://raw.githubusercontent.com/facebookresearch/demucs/main/README.md`, `…/main/LICENSE` (fetched) |
| **The pretrained weights** (`htdemucs`, `htdemucs_ft`, `hdemucs_mmi`, served from `dl.fbaipublicfiles.com`) | **Expressly not MIT.** The model author, `adefossez`, 2022-05-23: **"The model weights are not covered by the MIT license, and are provided only for scientific purposes."** | `https://github.com/facebookresearch/demucs/issues/327#issuecomment-1134828611` (read through the GitHub API, `…/repos/facebookresearch/demucs/issues/327/comments`) |
| **MUSDB18 training data** | Licence field **"Other (Non-Commercial)"**; **"MUSDB18 is provided for educational purposes only."**; commercial use restricted unless permission is granted. Same terms on MUSDB18-HQ (`"MUSDB18-HQ - an uncompressed version of MUSDB18", Version 1.0.0`). | Zenodo `https://zenodo.org/records/1117372`, `https://zenodo.org/records/3338373` (both fetched) |

**This corrects `docs/SEPARATION-ENGINES.md` §9.** That section records the weights as having
**"no licence text at all"** and being **"usable by inference only"**, and proposes to ask Meta.
There *is* an upstream statement, it is public, and it is a **restriction**, not a silence: the
weights are "provided only for scientific purposes". Inference-from-silence is off the table. (The
"ask Meta in writing" step is still sensible, but it is now a licence request rather than a
clarification.)

**The four options.**

| | Who distributes what | Whose terms bite | What a complaint points to | Reversible? |
| --- | --- | --- | --- | --- |
| **(a) Bundle weights in the installer** | **We** ship Meta's tensors inside our product, to every customer, permanently and un-recallably. | Meta's stated purpose limit **and** MUSDB18's non-commercial terms, because our product's whole function is that model doing inference commercially. | Our own released `.exe`/`.iss`, dated, with the weights inside it. The upstream sentence is already in writing. | **No.** A distributed binary cannot be recalled; a download mirror can be deleted, a customer's copy cannot. |
| **(b) Download weights on demand at install or first run** | Meta distributes to the user; **we** write the URL, the SHA-256 and the UI that frames it as a product component. | Same two layers, with our intent documented in our own repo (`docs/SEPARATION-ENGINES.md:182-185` already specifies pinning URL/SHA-256/licence text per engine pack). | Our code and our docs showing we built the commercial feature around a file the author limits to scientific purposes — with a weaker evidentiary trail than (a) because nothing of theirs leaves our machine. | **Yes**, by deleting a URL. This is the only structural advantage over (a). |
| **(c) User supplies their own model file** | The user distributes nothing to anyone; we ship a loader. | Our exposure drops, but a product whose setup screens say "point this at `htdemucs_ft`" is option (b) with extra clicks; the guidance is still ours. | Our instructions. If we name a file, we have effectively chosen it. | Yes. |
| **(d) Ship no local engine; stay Live-only** | We ship our code; the user runs their own licensed Ableton Live, which performs the separation under **Ableton's** EULA. | Ours: the .NET notices (met) and our own missing licence (unmet, §2a). No Meta or MUSDB18 contact at all. | Nothing about separation engines. | N/A — nothing to undo. |

**Recommendation: (d).** Reasons, in order:

1. It is the only option whose terms are **all** ours to satisfy today, and the engine it ships is
   the one that has actually produced stems end to end on this desk (`run-20261115-191232`, job 1).
2. (a) is the only **irreversible** option and it is irreversible in exactly the way that matters:
   weights travel inside a binary that cannot be taken back.
3. (b) is reversible and is therefore the correct **staging** shape for the roadmap — but it does not
   need to be shipped to be prepared: the manifest, pinned URL and SHA-256 fields described in
   `docs/SEPARATION-ENGINES.md:182-185` can be built and gated long before a weight is fetched, and
   the licence field there is what turns this from a guess into a record.
4. (c) transfers no risk we care about and removes the feature the user would notice.
5. The whole reason a local engine is wanted — not owning Live's desktop — is currently served
   honestly by the Live engine plus the run-conditions dialog. (d) costs no capability that exists
   today.

**Two conditions on (d).** First, it must not be written as "the local engine is impossible": it is
**unauthorised**, which is a different sentence, and the owner can change it. Second, a written answer
from Meta about the `.pth` files would settle the weights question but **not** the MUSDB18 question —
the training-data terms are nobody's to grant us, and `docs/SEPARATION-ENGINES.md:506-508` already
says so. Any move to (a) or (b) needs counsel, not a repo decision.

---

## 4. A clearly-licensed alternative model — **NEGATIVE RESULT, with one item blocked**

Rule applied: a candidate needs an explicit permissive licence **that I read**, saying where I read
it, and the licence must cover the **weights**, not the code. Anything whose only claim is a README
sentence is skipped.

Census performed (all fetched in this session):

- Hugging Face API, `search=demucs` → the official htdemucs weights are **re-uploaded by third
  parties** and tagged `license:mit` (`akhaliq/Demucs`, `iBoostAI/Demucs-v4`, `JosefKuchar/Demucs`,
  `Intel/demucs-openvino`). **Rejected**: those uploaders are not Meta, and Meta's author has said the
  opposite in writing (§3). This is the trap to name in the README-audit: **an MIT tag on a mirror is
  not a licence.**
- Hugging Face API, `search=music%20source%20separation` → four hits: three tagged `license:mit`
  (`Eddycrack864/Music-Source-Separation-Training`,
  `Pragmaticl/music-source-separation-training-model`, `Livum/Music-Source-Separation-Training`) and
  one `license:gpl`. I read the first card's raw `README.md`: **it contains no provenance at all** —
  no upstream model, no trainer, no dataset — only `license: mit` and a citation. I then read the
  upstream project's README (`ZFTurbo/Music-Source-Separation-Training`): it contains **no licence
  sentence for code or weights** and names the **"MUSDB18 dataset"** among its training data.
  **Rejected**: an MIT tag on a re-upload of MUSDB18-trained checkpoints conveys no permission we do
  not already lack.
- `sigsep/open-unmix-pytorch` README (fetched): code MIT, and the `umxl` weights are **"only licensed
  for non-commercial use (CC BY-NC-SA 4.0)"**. **Rejected** — and note this is the *exception*: here
  the owner did state the weights' terms, and they are non-commercial.
- `deezer/spleeter`: `LICENSE` is **MIT, "Copyright (c) 2019-present, Deezer SA."** (read verbatim),
  but the README's licence sentence is **"The code of Spleeter is MIT-licensed"** and nothing in the
  README states the terms of the **pretrained models** or where they come from. **Rejected** by the
  rule: silence about tensors is not a tensor licence. (Quality is also a step below htdemucs in the
  community's own comparisons, which is not a claim I verified and must not be published.)
- **Gaborformer** (`sevagh`) — a third-party article
  (`https://artrealmai.com/article/gaborformer-open-music-stem-separation`, indexed 2026-11-08)
  describes it as an open stem splitter with **"Apache-2.0 weights"** trained on 86 songs. I could
  find **no primary source**: no repository or model card surfaced by any query, so I read no licence
  text. **Blocked, therefore not a candidate.** The one-line fix if the team wants it re-opened: get
  the repository or model-card URL from the owner and read the licence file directly.
- `Valkyra-Labs/charon-audio` (found in the same search) is **MIT OR Apache-2.0** but is an ONNX
  *runner* for third-party separation models — a permissive code licence over someone else's weights
  is the same trap as the HF mirror tags. Not a candidate.

**Verdict.** As of these fetches, **no stem-separation model exists whose weights carry an explicit
permissive licence that permits commercial redistribution**, so no candidate survives the bar and the
quality comparison against htdemucs **cannot be stated for any of them**. The honest sentence for the
roadmap is: *the barrier is the weights and their training data, and it is not solved by finding a
friendlier mirror or a permissively-licensed runner.*

---

## 5. Doc lines that must change

**Listed only — none of these files was edited.** Re-locate each by wording; line numbers are from
the 2026-11-16 working tree.

| # | `file:line` | Current wording | Why it must change |
| --- | --- | --- | --- |
| 1 | `README.md:3` | "Batch stem separation for **Ableton Live 12** on Windows 11." | Two claims: the edition/version is now known to be 12.3 Suite (§1), and Windows 11 is not the OS this was built and tested on (§7). |
| 2 | `README.md:186` | "- Windows 11 (tested on Windows 11 / 10 22H2)" | The Windows 11 half has no evidence on this machine, which is Windows 10 Home 22H2. Either cite a real Win11 run or state Win10 22H2 as tested and Win11 as expected. |
| 3 | `README.md:187-189` | "**Ableton Live 12.1 or newer**, installed separately. Developed and verified against **Ableton Live 12.3.5 Suite**, and run end to end against **12.3.2 Suite**" | "12.1 or newer" is contradicted by Ableton (§1). "12.3.5" is the **update-staging copy** the locator now refuses to start (`docs/TEST-REPORT.md:25` says exactly this), so "developed against 12.3.5" needs the staging qualifier to stay honest. Add the Suite condition. |
| 4 | `docs/SETUP.md:60` | "Stem separation needs **Live 12.1 or newer**; the app" | Same wrong floor, no edition condition; a Standard user is told they are covered. |
| 5 | `docs/TEST-REPORT.md:5` | "Machine: **Windows 11-era desktop** (AMD Ryzen 9 3900X, 64 GB RAM, RTX 2080 Ti)" | This is the machine, and its OS is Windows 10 Home 22H2 (build 19045) — verified by registry and CIM in this session. The hardware matches; the OS does not. |
| 6 | `docs/SEPARATION-ENGINES.md:476` | "the pretrained `.pth` weights … **no licence text at all** … usable **by inference only** — ask Meta in writing before Phase 3" | Superseded: the author states the weights are **not MIT and "provided only for scientific purposes"**. Not a silence, a restriction (§3). |
| 7 | `docs/SEPARATION-ENGINES.md:237-241` | "its *weights* have **no stated licence**" … "ship nothing but the Demucs family — not because it is licensed, but because it is the only …" | Same supersession; and the conclusion should now cite the shipping option in §3, not "inference". |
| 8 | `docs/SEPARATION-ENGINES.md:478` | "BS-Roformer, `audio-separator`/UVR models, PyMDX, VIP centres — no licence, or CC BY-NC on the community bundles — **out**" | Correct conclusion; strengthen it with the verified evidence in §4 (the MIT-tagged HF re-uploads and the provenance-free card) so nobody re-opens it from a HF tag. |
| 9 | `docs/SEPARATION-ENGINES.md:477` | "MUSDB18 … Zenodo-hosted, **access by request**, 'since the tracks can only be used for academic purposes'" | The Zenodo licence fields I read are "Other (Non-Commercial)" / "provided for educational purposes only". Quote those; "access by request" was **not verified in this pass** and should be confirmed or dropped. |
| 10 | `docs/SEPARATION-ENGINES.md:497-503` | "**today's installer ships no licence file at all.**" | Half-stale: `ThirdPartyNotices.dotnet.txt` and `LICENSE.dotnet-runtime.txt` now ship (§2a). Still true for **our own** licence, which remains absent — rewrite to say precisely that. |
| 11 | `docs/SEPARATION-ENGINES.md:55` | "code is MIT, the weights' licence is the §9 problem" | The §9 problem is now an answered question, not an open one (§3). |

**The same false floor also lives in code, not just docs** (outside the brief's file list, so listed
rather than edited): `src/StemForge.Core/Live/LiveInstall.cs:8-9` (comment +
`MinimumStemSeparationVersion = new(12, 1)`), `src/StemForge.App/ViewModels/MainViewModel.cs:699-700`
and `src/StemForge.Core/Live/LiveAutomationRunner.cs:71` (both user-facing "Live 12.1 or newer is
required."). **That has since been fixed on this branch**: the floor, the edition condition and every
user-facing sentence about them now come from `LiveInstall` alone, so the four `file:line` entries
above are what this research pass found, not what the tree still says.
`docs/TEST-REPORT.md:219` / `README.md:116` said "263 automated tests" when this was written; the
docs lane has since raised both to the count this tree actually reports. Neither number was ever
this document's claim, and nothing in §1 changes it either way.

### Statements that must not be published anywhere (README, SETUP or installer)

- "Works with Ableton Live 12.1" — or any floor below **12.3**.
- "Works with any edition of Live 12" / "Intro and Standard supported" / silence about **Suite**.
- "Stem separation is included in every Live 12" or anything sourced to the editions-comparison page,
  which does not mention it.
- "Tested on Windows 11" until a Windows 11 run is recorded.
- "Requires an additional download" — or "no additional download required" — for Live's separator
  (§1 blocked sub-question).
- "Licensed under MIT" / "open source" for **our own** product: **cleared** — the root `LICENSE` (MIT)
  now exists (§2a), so the sentence is true for our code. It stays banned for the pretrained weights
  and any engine pack (§3: not MIT, scientific purposes only).
- "Demucs-quality local separation without Ableton" — and any quality comparison to htdemucs for any
  model, since §4 left no licensed candidate to compare.
- "Commercial use permitted" / "free for commercial use" for any weights, engine pack, or model
  directory.
- "Trained on licensed/permissive data" for any candidate model.
- "Signed by Skeer" / "verified publisher" / "no SmartScreen prompt" / "antivirus-safe".
- "Installs for everyone on this PC" or any machine-wide claim while `PrivilegesRequired=lowest`
  stands.
- "Live's own separation engine, on this machine, offline" for a hypothetical local engine — the
  Live engine is Live's; ours would be a model of unstated/academic-restricted lineage.

---

## 6. Corrections to the premises I was given

- The brief and the request both describe the desk as **Windows 10 Pro 22H2**. It is **Windows 10
  Home** — `ProductName=Windows 10 Home`, `EditionID=Core`, `DisplayVersion=22H2`,
  build **19045**. Home has no Group Policy editor and no BitLocker-to-Go story, which is a support
  difference, and "Pro" makes the environment look more like a managed corporate desktop than it is.
- Windows 10 22H2 is also past Microsoft's consumer end-of-support date of **14 October 2025**
  (*"Windows 10 will reach end of support on October 14, 2025. The current version, 22H2, will be the
  final version of Windows 10"*, Microsoft Lifecycle
  `https://learn.microsoft.com/en-us/lifecycle/products/windows-10-home-and-pro`, read via the search
  index rather than a direct page fetch).
- Independently of Windows servicing, Microsoft's .NET 10 support table is narrower than "Windows
  10": **"Windows 10 support is limited to Long-Term Servicing Channel (LTSC) and Enterprise editions
  only"**, with the row reading **"Windows 10 (21H2, 1809, 1607 LTSC/Enterprise)"**
  (`https://learn.microsoft.com/en-us/dotnet/core/install/windows`, fetched). A **self-contained**
  app carries its own runtime, so this does not stop it running on Home — but it means Win10 Home is
  an *unsupported configuration*, which is the precise reason "Windows 11" in the README is
  **incomplete rather than merely wrong**: the tested reality is Win10 Home 22H2, the README's
  headline is Win11, and Microsoft's support statement covers neither cleanly.

---

## 7. Work actually done, uncovered scope, blockers

**Done (all read-only).** Read the brief on disk; read `README.md`, `docs/SETUP.md`,
`docs/TEST-REPORT.md`, `docs/LIVE-AUTOMATION.md`, `docs/SEPARATION-ENGINES.md` for the claims listed;
read `build.ps1` and `packaging/SkeerAutoSTEM.iss`; enumerated every project file and its package
references; hashed and diffed the shipped notice files against the NuGet runtime packs; listed
`launch/` and `artifacts/` with sizes and timestamps; read the Authenticode status of all three
produced binaries; searched the whole tree for any signing step; read the local Ableton preference,
install and staging folders and the file versions of both `Live.exe` copies; enumerated the
separation assets inside the 12.3.2 install; read the app's own `settings.json`; read the local
Windows edition/build and the CPU/GPU/RAM to test the test report's machine claim. Upstream: fetched
Ableton's release notes, 12.3 blog, reference manual and editions page; Ableton's Help Center was
blocked; fetched the Demucs README and LICENSE, the demucs#327 comment thread through the GitHub API,
the MUSDB18 and MUSDB18-HQ Zenodo records, the Open-Unmix README, the Spleeter README and LICENSE,
the ZFTurbo MSST README, one Hugging Face model card, two Hugging Face API queries, the
dotnet/runtime `LICENSE.TXT`, Microsoft's SmartScreen overview, Microsoft's .NET-on-Windows install
page. **Exactly one file created: this one.**

**Not covered.** No build, no test, no app or CLI run, no Ableton launch, no package installation, no
git state change. The **Live 12.1 question could not be tested locally at all** — no 12.1 program
files exist here. Whether Live 12.3+ needs an **additional download** for stem separation: unverified.
Whether a **Live Standard or Intro** install really lacks the Create-menu entry — the docs' claim
comes from Ableton's text, and no non-Suite install exists here to inspect. The exact
**trademark/attribution rules for using Ableton's name and the "Live" word in our product and
installer** were not investigated and are a separate gate: `AGENTS.md` and the README already lean on
Ableton's naming. The **Spleeter-vs-htdemucs quality** comparison was deliberately not asserted. No
attempt was made to reach Meta or Ableton Legal, and nothing here is legal advice — the terms above
are quoted, the risk readings are ours.

**Blockers.**

1. `https://help.ableton.com/...23730994755996...` returns **403** from this machine — the FAQ that
   states the edition exclusion list explicitly cannot be read directly. Its wording is available
   only through the search index. Closing this needs a browser fetch on the owner's side, or a
   different network.
2. `https://jrsoftware.org/copying.php` (Inno Setup licence) is **404/403** and the online help page
   would not parse — Inno's terms are **blocked**. Not urgent while only Inno's *output* ships, but it
   must be read before any Inno file is redistributed.
3. **Gaborformer** has no findable primary source, so the one promising permissive-weights lead in
   §4 stays **blocked** until somebody supplies a repository or model-card URL.
4. **The product has no licence text of its own**, so I cannot report what terms we ship under — there
   is nothing to read. Only the owner can supply the terms; nobody on the team can invent them.
5. **Whether (a) or (b) is acceptable at all is an owner decision informed by counsel**, not a research
   finding: the upstream restriction on the weights is verified, but what to do about it is not a
   question this lane can settle.
