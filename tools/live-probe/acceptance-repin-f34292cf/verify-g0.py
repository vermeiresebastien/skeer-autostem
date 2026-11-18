"""Verifies the G0 pre-fix snapshot against its own manifest, and prints what the folder holds."""
import hashlib
import os

ROOT = r"D:\Projects\tools\autostem-pre-fix-b995063b\live-probe"
MANIFEST = os.path.join(ROOT, "MANIFEST.sha256")

entries, missing, mismatched, ok = 0, [], [], 0
with open(MANIFEST, encoding="utf-8") as handle:
    for raw in handle:
        line = raw.strip()
        if not line:
            continue
        digest, sep, path = line.partition(" *")
        if not sep:
            digest, _, path = line.partition("  ")
        path = path.strip().lstrip("./").replace("\\", os.sep).replace("/", os.sep)
        entries += 1
        full = os.path.join(ROOT, path)
        if not os.path.exists(full):
            missing.append(path)
            continue
        if hashlib.sha256(open(full, "rb").read()).hexdigest() != digest:
            mismatched.append(path)
        else:
            ok += 1

on_disk = sum(len(files) for _root, _dirs, files in os.walk(ROOT))
print("manifest entries      :", entries)
print("verified ok           :", ok)
print("missing               :", len(missing), missing[:4])
print("mismatched            :", len(mismatched), mismatched[:4])
print("files under live-probe:", on_disk)
print("manifest sha256       :",
      hashlib.sha256(open(MANIFEST, "rb").read()).hexdigest())
print("pre-fix probe sha256  :",
      hashlib.sha256(open(os.path.join(ROOT, "AutoStemProbe", "__init__.py"), "rb").read()).hexdigest())
