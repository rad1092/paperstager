#!/usr/bin/env python3
"""Fail closed on missing NuGet audit data or moderate/higher advisories."""

import argparse
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent


def validate_report(report: dict, inventory: dict) -> dict:
    if report.get("version") != 1 or not report.get("projects") or not report.get("sources"):
        raise ValueError("NuGet audit returned no project or source data, or an unknown format.")
    if inventory.get("version") != 1 or not inventory.get("projects"):
        raise ValueError("NuGet dependency inventory is missing or has an unknown format.")
    if ({project.get("path") for project in report["projects"]}
            != {project.get("path") for project in inventory["projects"]}):
        raise ValueError("NuGet audit and inventory inspected different projects.")
    findings = []
    frameworks = sum(len(project.get("frameworks", [])) for project in inventory["projects"])
    package_count = sum(
        len(framework.get("topLevelPackages", [])) + len(framework.get("transitivePackages", []))
        for project in inventory["projects"] for framework in project.get("frameworks", [])
    )
    if frameworks == 0 or package_count == 0:
        raise ValueError("NuGet inventory did not inspect any frameworks or packages.")
    for project in report["projects"]:
        for log in project.get("logs", []):
            if log.get("level", "").lower() in ("error", "warning"):
                raise ValueError("NuGet audit returned a warning/error; inspect the saved JSON.")
        for framework in project.get("frameworks", []):
            for package in framework.get("topLevelPackages", []) + framework.get("transitivePackages", []):
                for advisory in package.get("vulnerabilities", []):
                    severity = advisory.get("severity", "").lower()
                    if severity not in ("low", "moderate", "high", "critical"):
                        raise ValueError("NuGet audit returned an unrecognized vulnerability severity.")
                    findings.append({"package": package["id"], "version": package.get("resolvedVersion"),
                                     "severity": severity, "advisory": advisory.get("advisoryurl")})
    for project in inventory["projects"]:
        for log in project.get("logs", []):
            if log.get("level", "").lower() in ("error", "warning"):
                raise ValueError("NuGet inventory returned a warning/error; inspect the saved JSON.")
    for log in report.get("logs", []) + inventory.get("logs", []):
        if log.get("level", "").lower() in ("error", "warning"):
            raise ValueError("NuGet audit returned a warning/error; inspect the saved JSON.")
    return {"success": not any(f["severity"] != "low" for f in findings),
            "projectCount": len(report["projects"]), "frameworkCount": frameworks,
            "packageReferenceCount": package_count, "findings": findings}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--solution", default="PaperStager.slnx")
    args = parser.parse_args()
    destination = ROOT / "artifacts" / "security"
    destination.mkdir(parents=True, exist_ok=True)
    try:
        reports = {}
        for label, extra in (("inventory", []), ("audit", ["--vulnerable"])):
            result = subprocess.run([args.dotnet, "package", "list", "--project", args.solution,
                                     "--no-restore", "--include-transitive", *extra,
                                     "--format", "json", "--output-version", "1"],
                                    cwd=ROOT, capture_output=True, text=True, timeout=180)
            (destination / f"nuget-{label}.json").write_text(result.stdout, encoding="utf-8")
            (destination / f"nuget-{label}.stderr.log").write_text(result.stderr, encoding="utf-8")
            if result.returncode:
                raise ValueError(f"NuGet {label} command failed with exit status {result.returncode}.")
            if result.stderr.strip():
                raise ValueError(f"NuGet {label} emitted diagnostics on stderr; inspect the saved log.")
            reports[label] = json.loads(result.stdout)
        summary = validate_report(reports["audit"], reports["inventory"])
        (destination / "nuget-summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
        print(f"NuGet audit: {summary['projectCount']} projects, {len(summary['findings'])} advisories.")
        return 0 if summary["success"] else 1
    except (ValueError, KeyError, TypeError, OSError, subprocess.SubprocessError) as error:
        print(f"NuGet audit failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
