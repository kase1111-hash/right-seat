#!/usr/bin/env python3
"""Assembles a distributable Flight Guardian release.

Produces:
  dist/FlightGuardian-win-x64/          ready-to-run folder
    Guardian.Desktop/                   desktop companion app (self-contained)
    Guardian.App/                       headless console monitor
    Guardian.Replay/                    scenario replay CLI
    config/                             guardian.toml + aircraft profiles
    training/scenarios/                 validation scenarios
    community/flight-guardian-efb/      MSFS community package (EFB tablet app)
    README.md, LICENSE
  dist/FlightGuardian-win-x64.zip

Usage:
  python3 scripts/package_release.py [--version 0.2.0] [--skip-publish]

--skip-publish reuses existing publish output (CI runs dotnet publish itself).
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import zipfile
from datetime import datetime, timezone

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DIST = os.path.join(ROOT, "dist")
STAGE = os.path.join(DIST, "FlightGuardian-win-x64")

PUBLISH_PROJECTS = {
    "Guardian.Desktop": "src/Guardian.Desktop/Guardian.Desktop.csproj",
    "Guardian.App": "src/Guardian.App/Guardian.App.csproj",
    "Guardian.Replay": "src/Guardian.Replay/Guardian.Replay.csproj",
}


def run(cmd, **kwargs):
    print("+", " ".join(cmd))
    subprocess.run(cmd, check=True, cwd=ROOT, **kwargs)


def publish(version):
    for name, proj in PUBLISH_PROJECTS.items():
        run([
            "dotnet", "publish", proj,
            "-c", "Release",
            "-r", "win-x64",
            "--self-contained", "true",
            "-p:PublishSingleFile=false",
            f"-p:Version={version}",
            "-o", os.path.join(STAGE, name),
        ])


def copy_support_files():
    shutil.copytree(os.path.join(ROOT, "config"), os.path.join(STAGE, "config"),
                    dirs_exist_ok=True)
    scenarios_src = os.path.join(ROOT, "training", "scenarios")
    scenarios_dst = os.path.join(STAGE, "training", "scenarios")
    shutil.copytree(scenarios_src, scenarios_dst, dirs_exist_ok=True,
                    ignore=shutil.ignore_patterns("scorecard.json"))
    for f in ("README.md", "LICENSE"):
        src = os.path.join(ROOT, f)
        if os.path.exists(src):
            shutil.copy2(src, STAGE)


def filetime(path):
    """Windows FILETIME (100ns ticks since 1601-01-01) for layout.json."""
    epoch_delta = 11644473600  # seconds between 1601 and 1970
    mtime = os.path.getmtime(path)
    return int((mtime + epoch_delta) * 10_000_000)


def build_efb_package(version):
    """Assembles the MSFS community package containing the EFB tablet app."""
    pkg = os.path.join(STAGE, "community", "flight-guardian-efb")
    app_dst = os.path.join(pkg, "html_ui", "efb_ui", "efb_apps", "guardian")
    shutil.copytree(os.path.join(ROOT, "efb", "GuardianApp"), app_dst,
                    dirs_exist_ok=True)

    manifest = {
        "dependencies": [],
        "content_type": "MISC",
        "title": "Flight Guardian EFB",
        "manufacturer": "",
        "creator": "Flight Guardian Project",
        "package_version": version,
        "minimum_game_version": "1.0.0",
        "release_notes": {
            "neutral": {"LastUpdate": "", "OlderHistory": ""}
        },
    }
    with open(os.path.join(pkg, "manifest.json"), "w") as f:
        json.dump(manifest, f, indent=2)

    content = []
    for dirpath, _, filenames in os.walk(pkg):
        for name in sorted(filenames):
            # Only the package-root manifest/layout are excluded from the
            # layout listing; nested app files with the same names count.
            if dirpath == pkg and name in ("manifest.json", "layout.json"):
                continue
            full = os.path.join(dirpath, name)
            rel = os.path.relpath(full, pkg).replace(os.sep, "/")
            content.append({
                "path": rel,
                "size": os.path.getsize(full),
                "date": filetime(full),
            })
    with open(os.path.join(pkg, "layout.json"), "w") as f:
        json.dump({"content": content}, f, indent=2)

    print(f"EFB community package: {pkg} ({len(content)} files)")


def make_zip():
    zip_path = STAGE + ".zip"
    if os.path.exists(zip_path):
        os.remove(zip_path)
    with zipfile.ZipFile(zip_path, "w", zipfile.ZIP_DEFLATED) as zf:
        for dirpath, _, filenames in os.walk(STAGE):
            for name in filenames:
                full = os.path.join(dirpath, name)
                rel = os.path.relpath(full, DIST)
                zf.write(full, rel)
    size_mb = os.path.getsize(zip_path) / 1024 / 1024
    print(f"release zip: {zip_path} ({size_mb:.1f} MB)")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", default="0.2.0")
    parser.add_argument("--skip-publish", action="store_true")
    args = parser.parse_args()

    if os.path.exists(STAGE):
        shutil.rmtree(STAGE)
    os.makedirs(STAGE, exist_ok=True)

    if not args.skip_publish:
        publish(args.version)
    copy_support_files()
    build_efb_package(args.version)
    make_zip()

    print(f"\nDone. Built {datetime.now(timezone.utc).isoformat()}")
    print("Install: unzip, run Guardian.Desktop\\Guardian.Desktop.exe, and copy")
    print("community\\flight-guardian-efb into your MSFS Community folder.")


if __name__ == "__main__":
    sys.exit(main())
