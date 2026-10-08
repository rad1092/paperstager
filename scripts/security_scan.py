#!/usr/bin/env python3
"""Scan Git's nonignored source files for high-confidence credential patterns.

This small offline guard is not a complete secret detector. It does not read
ignored build caches or personal configuration and never prints matched values.
"""

from pathlib import Path
import json
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent
RULES = {
    "private-key": re.compile(rb"-----BEGIN (?:RSA |EC |DSA |OPENSSH |ENCRYPTED )?PRIVATE KEY-----"),
    "github-token": re.compile(rb"\b(?:gh[pousr]_[A-Za-z0-9]{36,}|github_pat_[A-Za-z0-9_]{70,})\b"),
    "slack-token": re.compile(rb"\bxox[baprs]-[A-Za-z0-9-]{20,}\b"),
    "aws-access-key": re.compile(rb"\b(?:AKIA|ASIA)[A-Z0-9]{16}\b"),
    "google-api-key": re.compile(rb"\bAIza[0-9A-Za-z_-]{35}\b"),
    "openai-project-key": re.compile(rb"\bsk-(?:proj|svcacct)-[A-Za-z0-9_-]{40,}\b"),
    "credential-url": re.compile(rb"https?://[^\s/:]{2,}:[^\s/@]{8,}@"),
}


def scan_bytes(data: bytes) -> list[dict]:
    return [{"rule": rule, "line": data.count(b"\n", 0, match.start()) + 1}
            for rule, pattern in RULES.items() for match in pattern.finditer(data)]


def main() -> int:
    listing = subprocess.check_output(
        ["git", "ls-files", "--cached", "--others", "--exclude-standard", "-z"], cwd=ROOT
    )
    findings = []
    count = 0
    for relative in sorted(set(listing.decode("utf-8").strip("\0").split("\0")) - {""}):
        path = ROOT / relative
        if path.is_symlink():
            findings.append({"file": relative, "rule": "source-symlink-needs-review", "line": 0})
            continue
        if not path.is_file():
            continue
        count += 1
        for match in scan_bytes(path.read_bytes()):
            findings.append({"file": relative, **match})
    report = {"success": not findings, "filesScanned": count, "findings": findings,
              "scope": "Nonignored Git source files; high-confidence patterns only; values redacted."}
    destination = ROOT / "artifacts" / "security" / "secret-scan.json"
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Secret pattern scan: {count} files, {len(findings)} findings (values never logged).")
    for finding in findings:
        print(f"{finding['file']}:{finding['line']}: {finding['rule']}")
    return 1 if findings else 0


if __name__ == "__main__":
    raise SystemExit(main())
