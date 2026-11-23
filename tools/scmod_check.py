#!/usr/bin/env python3
"""Static quality checks for Survivalcraft 2 .scmod archives and game logs."""
from __future__ import annotations

import argparse
import json
import re
import sys
import zipfile
from pathlib import PurePosixPath
from xml.etree import ElementTree


def validate_archive(path: str, expected_api: str | None = None,
                     expected_game: str | None = None) -> list[str]:
    errors: list[str] = []
    try:
        archive = zipfile.ZipFile(path)
    except (OSError, zipfile.BadZipFile) as exc:
        return [f"Cannot open .scmod as ZIP: {exc}"]

    with archive:
        entries = archive.infolist()
        names = [entry.filename for entry in entries]
        if len(names) != len(set(names)):
            errors.append("Archive contains duplicate file paths.")

        safe_names = []
        for name in names:
            normalized = PurePosixPath(name)
            if normalized.is_absolute() or ".." in normalized.parts or "\\" in name:
                errors.append(f"Unsafe archive path: {name}")
            safe_names.append(name)

        files = {entry.filename for entry in entries if not entry.is_dir()}
        dlls = [name for name in files if name.lower().endswith(".dll")]
        if not dlls:
            errors.append("No .dll assembly found in the mod package.")

        info_names = [name for name in files if PurePosixPath(name).name.lower() == "modinfo.json"]
        if not info_names:
            errors.append("modinfo.json is missing.")
        else:
            if len(info_names) > 1:
                errors.append("More than one modinfo.json found; package layout is ambiguous.")
            try:
                info = json.loads(archive.read(info_names[0]).decode("utf-8-sig"))
                for key in ("Name", "Version", "ApiVersion", "ScVersion", "PackageName"):
                    if not info.get(key):
                        errors.append(f"modinfo.json is missing required field: {key}")
                if expected_api and info.get("ApiVersion") != expected_api:
                    errors.append(f"ApiVersion mismatch: expected {expected_api}, got {info.get('ApiVersion')}")
                if expected_game and info.get("ScVersion") != expected_game:
                    errors.append(f"ScVersion mismatch: expected {expected_game}, got {info.get('ScVersion')}")
                if not isinstance(info.get("Dependencies", {}), dict):
                    errors.append("Dependencies must be a JSON object.")
            except (UnicodeDecodeError, json.JSONDecodeError) as exc:
                errors.append(f"modinfo.json is invalid: {exc}")

        for name in files:
            if name.lower().endswith((".xdb", ".xml")):
                try:
                    ElementTree.fromstring(archive.read(name))
                except (ElementTree.ParseError, UnicodeDecodeError) as exc:
                    errors.append(f"Invalid XML in {name}: {exc}")

        if not files:
            errors.append("Archive contains no files.")

    return errors


def inspect_log(path: str) -> list[str]:
    """Return likely fatal game/mod log lines; this is triage, not proof of a crash."""
    patterns = re.compile(
        r"\b(FATAL|Unhandled Exception|NullReferenceException|TypeLoadException|"
        r"FileNotFoundException|MissingMethodException|Database not loaded|"
        r"Failed to load mod|Mod loading failed)\b",
        re.IGNORECASE,
    )
    findings = []
    try:
        with open(path, encoding="utf-8-sig", errors="replace") as stream:
            for line_number, line in enumerate(stream, 1):
                if patterns.search(line):
                    findings.append(f"{path}:{line_number}: {line.strip()}")
    except OSError as exc:
        return [f"Cannot read log: {exc}"]
    return findings


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("package", nargs="?", help="Path to a .scmod archive")
    parser.add_argument("--api", default="1.9.5.1", help="Expected Survivalcraft API version")
    parser.add_argument("--game", default="2.4.0.0", help="Expected Survivalcraft game version")
    parser.add_argument("--log", help="Optional Survivalcraft log to scan")
    args = parser.parse_args()

    if not args.package and not args.log:
        parser.error("provide a .scmod package and/or --log")

    findings: list[str] = []
    if args.package:
        findings.extend(validate_archive(args.package, args.api, args.game))
    if args.log:
        findings.extend(inspect_log(args.log))

    if findings:
        print("SURVIVALCRAFT MOD CHECK: FAIL")
        for finding in findings:
            print(f"- {finding}")
        return 1

    print("SURVIVALCRAFT MOD CHECK: PASS")
    print("Archive structure and metadata passed static checks." if args.package else "No fatal-pattern log entries found.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
