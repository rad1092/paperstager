#!/usr/bin/env python3
"""Package and launch-test PaperStager using only the Python standard library."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import platform
import plistlib
import re
import shutil
import signal
import stat
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parent.parent
RIDS = ("osx-arm64", "win-x64", "linux-x64")


def sha256(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def write_json(path: Path, data: object) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(data, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def package(args: argparse.Namespace) -> None:
    publish_dir = args.publish_dir.resolve(strict=True)
    version = args.version or ET.parse(ROOT / "Directory.Build.props").findtext(".//Version")
    if not version or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+(?:-[a-zA-Z0-9.-]+)?", version):
        raise ValueError("Version must be a safe semantic version, for example 0.1.0.")
    revision = args.sha or subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=ROOT, text=True
    ).strip()
    if not re.fullmatch(r"[0-9a-f]{40}", revision):
        raise ValueError("Package source revision must be a full Git SHA.")
    executable_name = "PaperStager.exe" if args.rid.startswith("win-") else "PaperStager"
    if not (publish_dir / executable_name).is_file():
        raise ValueError(f"Published executable is missing: {executable_name}")
    if not (publish_dir / "PaperStager.runtimeconfig.json").is_file():
        raise ValueError("Published runtime configuration is missing.")
    runtime = json.loads((publish_dir / "PaperStager.runtimeconfig.json").read_text(encoding="utf-8"))
    if "includedFrameworks" not in runtime.get("runtimeOptions", {}):
        raise ValueError("Release must be self-contained; the .NET runtime must be included.")
    frameworks = runtime["runtimeOptions"]["includedFrameworks"]
    runtime_version = next((item.get("version") for item in frameworks if item.get("name") == "Microsoft.NETCore.App"), None)
    if not runtime_version or not re.fullmatch(r"[0-9]+\.[0-9]+\.[0-9]+", runtime_version):
        raise ValueError("Could not identify the self-contained .NET runtime version.")
    package_id = f"microsoft.netcore.app.runtime.{args.rid}"
    nuget_packages = Path(os.environ.get("NUGET_PACKAGES", Path.home() / ".nuget" / "packages"))
    runtime_package = args.runtime_package_dir or nuget_packages / package_id / runtime_version
    for name in ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"):
        if not (runtime_package / name).is_file():
            raise ValueError(f"Actual .NET runtime license is missing: {runtime_package / name}")
    required_docs = [ROOT / "README.md", ROOT / "LICENSE", ROOT / args.notices]
    for document in required_docs:
        if not document.is_file():
            raise ValueError(f"Required release document is missing: {document.name}")
    if not (ROOT / "docs").is_dir():
        raise ValueError("Release documentation directory is missing.")
    if not (ROOT / "third_party").is_dir():
        raise ValueError("Third-party license texts are missing.")
    args.output.mkdir(parents=True, exist_ok=True)
    name = f"PaperStager-{version}-{args.rid}"
    archive = args.output.resolve() / f"{name}.zip"
    if archive.exists():
        raise FileExistsError(f"Refusing to overwrite an existing release archive: {archive}")
    with tempfile.TemporaryDirectory(prefix="paperstager-package-") as temporary:
        stage = Path(temporary) / name
        stage.mkdir()
        if args.rid.startswith("osx-"):
            contents = stage / "PaperStager.app" / "Contents"
            payload = contents / "MacOS"
            resources = contents / "Resources"
            resources.mkdir(parents=True)
            with (contents / "Info.plist").open("wb") as stream:
                plistlib.dump({
                    "CFBundleName": "PaperStager",
                    "CFBundleDisplayName": "PaperStager",
                    "CFBundleIdentifier": "org.paperstager.desktop",
                    "CFBundleExecutable": "PaperStager",
                    "CFBundlePackageType": "APPL",
                    "CFBundleShortVersionString": version,
                    "CFBundleVersion": version.split("-")[0],
                    "NSHighResolutionCapable": True,
                    "NSRequiresAquaSystemAppearance": False,
                }, stream)
        else:
            payload = stage
            resources = stage
        shutil.copytree(publish_dir, payload, dirs_exist_ok=True)
        for destination in {stage, resources}:
            for document in required_docs:
                shutil.copy2(document, destination / document.name)
            shutil.copytree(ROOT / "docs", destination / "docs")
            shutil.copytree(ROOT / "third_party", destination / "third_party")
            runtime_notices = destination / "third_party" / f"dotnet-runtime-{runtime_version}"
            runtime_notices.mkdir(parents=True, exist_ok=True)
            for name in ("LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"):
                shutil.copy2(runtime_package / name, runtime_notices / name)
            write_json(runtime_notices / f"package-{args.rid}.json", {
                "package": package_id, "version": runtime_version,
                "source": f"https://www.nuget.org/packages/{package_id}/{runtime_version}",
                "licenseSha256": sha256(runtime_package / "LICENSE.TXT"),
                "noticesSha256": sha256(runtime_package / "THIRD-PARTY-NOTICES.TXT"),
            })
        if not args.rid.startswith("win-"):
            (payload / executable_name).chmod(0o755)
        executable = (payload / executable_name).relative_to(stage).as_posix()
        metadata = {
            "name": "PaperStager", "version": version, "runtimeIdentifier": args.rid,
            "sourceCommit": revision, "executable": executable,
            "notices": Path(args.notices).name,
            "selfContained": True, "developerIdSigned": False,
            "runtimeVersion": runtime_version, "authenticodeSigned": False, "notarized": False,
        }
        write_json(stage / "package-info.json", metadata)
        if resources != stage:
            write_json(resources / "package-info.json", metadata)
        files = {}
        for file in sorted(stage.rglob("*")):
            if file.is_symlink():
                raise ValueError(f"Release payload may not contain symbolic links: {file.name}")
            if file.is_file():
                files[file.relative_to(stage).as_posix()] = sha256(file)
        write_json(stage / "file-checksums.json", files)
        # ZIP stores Unix executable permissions; extraction below restores them.
        temporary_archive = archive.with_suffix(".zip.partial")
        if temporary_archive.exists():
            raise FileExistsError(f"Previous incomplete archive needs review: {temporary_archive}")
        try:
            with zipfile.ZipFile(temporary_archive, "w", zipfile.ZIP_DEFLATED, compresslevel=6) as output:
                for file in sorted(stage.rglob("*")):
                    if file.is_file():
                        output.write(file, file.relative_to(stage.parent).as_posix())
            temporary_archive.rename(archive)
        finally:
            temporary_archive.unlink(missing_ok=True)
    digest = sha256(archive)
    archive.with_suffix(".zip.sha256").write_text(f"{digest}  {archive.name}\n", encoding="ascii")
    print(f"Packaged {archive.name}; source {revision}; SHA-256 {digest}")


def extract_checked(archive: Path, destination: Path) -> Path:
    """Only extract ordinary files below one top-level package directory."""
    roots = set()
    names = set()
    with zipfile.ZipFile(archive) as source:
        for entry in source.infolist():
            path = PurePosixPath(entry.filename)
            mode = entry.external_attr >> 16
            if (path.is_absolute() or ".." in path.parts or "\\" in entry.filename
                    or not path.parts or ":" in entry.filename
                    or stat.S_ISLNK(mode) or entry.filename in names):
                raise ValueError("Archive contains an unsafe or duplicate entry.")
            roots.add(path.parts[0])
            names.add(entry.filename)
        if len(roots) != 1:
            raise ValueError("Archive must contain exactly one package root.")
        for entry in source.infolist():
            target = destination.joinpath(*PurePosixPath(entry.filename).parts)
            if entry.is_dir():
                target.mkdir(parents=True, exist_ok=True)
                continue
            target.parent.mkdir(parents=True, exist_ok=True)
            with source.open(entry) as src, target.open("xb") as dst:
                shutil.copyfileobj(src, dst)
            if os.name != "nt":
                mode = (entry.external_attr >> 16) & 0o777
                target.chmod(mode or 0o644)
    return destination / roots.pop()


def validate_marker(marker: dict) -> None:
    if any(marker.get(field) is not True for field in ("success", "sourcePreserved", "nativeWindow")):
        raise ValueError("Native smoke did not confirm a visible window, success, and source preservation.")
    for field, minimum in (("importedPages", 4), ("exportedDocuments", 2), ("thumbnailsRendered", 4)):
        if type(marker.get(field)) is not int or marker[field] < minimum:
            raise ValueError(f"Native smoke marker is missing its required {field} evidence.")


def validate_payload(stage: Path, metadata: dict) -> None:
    manifest = json.loads((stage / "file-checksums.json").read_text(encoding="utf-8"))
    files = {path.relative_to(stage).as_posix() for path in stage.rglob("*") if path.is_file()}
    expected = files - {"file-checksums.json"}
    if not isinstance(manifest, dict) or not expected or set(manifest) != expected:
        raise ValueError("Package manifest must cover every payload file exactly once.")
    required = {"package-info.json", "README.md", "LICENSE", metadata.get("notices"), metadata.get("executable")}
    if not required.issubset(expected):
        raise ValueError("Package manifest is missing an executable, metadata, or required notices.")
    for name, digest in manifest.items():
        candidate = stage / name
        if (not isinstance(digest, str) or not re.fullmatch(r"[0-9a-f]{64}", digest)
                or not candidate.resolve().is_relative_to(stage.resolve()) or sha256(candidate) != digest):
            raise ValueError("Extracted package file hash does not match its manifest.")


def run_app(command: list[str], cwd: Path, log_base: Path, timeout: int) -> int:
    with log_base.with_suffix(".stdout.log").open("wb") as out, log_base.with_suffix(".stderr.log").open("wb") as err:
        process = subprocess.Popen(command, cwd=cwd, stdout=out, stderr=err, start_new_session=os.name != "nt")
        try:
            return process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            if os.name == "nt":
                process.kill()
            else:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            raise TimeoutError(f"Native package smoke exceeded {timeout} seconds.") from None


def smoke(args: argparse.Namespace) -> None:
    archive = args.archive.resolve(strict=True)
    expected_digest = archive.with_suffix(".zip.sha256").read_text(encoding="ascii").split()[0]
    if sha256(archive) != expected_digest:
        raise ValueError("Archive SHA-256 does not match its sidecar.")
    args.output.mkdir(parents=True, exist_ok=True)
    results = []
    with tempfile.TemporaryDirectory(prefix="paperstager-unpacked-") as temporary:
        base = Path(temporary)
        stage = extract_checked(archive, base / "unpacked")
        metadata = json.loads((stage / "package-info.json").read_text(encoding="utf-8"))
        if metadata.get("sourceCommit") != args.sha:
            raise ValueError("Package source SHA differs from the commit being verified.")
        host_arch = platform.machine().lower()
        expected_rid = {("darwin", "arm64"): "osx-arm64", ("win32", "amd64"): "win-x64",
                        ("linux", "x86_64"): "linux-x64"}.get((sys.platform, host_arch))
        if metadata.get("runtimeIdentifier") != expected_rid:
            raise ValueError("Package runtime must match this host's native architecture.")
        if stage.name != f"PaperStager-{metadata.get('version')}-{expected_rid}" or archive.stem != stage.name:
            raise ValueError("Package directory and archive names must match its version and runtime.")
        validate_payload(stage, metadata)
        executable = (stage / metadata["executable"]).resolve(strict=True)
        if not executable.is_relative_to(stage.resolve()):
            raise ValueError("Package executable points outside its package.")
        for launch in (1, 2):
            work = base / f"synthetic-run-{launch}"
            work.mkdir()
            command = [str(executable), "--smoke", str(work)]
            launch_mode = "native-executable"
            if sys.platform == "darwin" and launch == 2:
                # The second launch also checks Finder/LaunchServices and Info.plist.
                command = ["open", "-W", "-n", str(stage / "PaperStager.app"), "--args", "--smoke", str(work)]
                launch_mode = "macos-app-bundle"
            if sys.platform.startswith("linux"):
                if not shutil.which("xvfb-run"):
                    raise ValueError("Linux native smoke requires xvfb-run.")
                command = ["xvfb-run", "--auto-servernum", "--server-args=-screen 0 1280x900x24", *command]
            status = run_app(command, stage, args.output / f"launch-{launch}", args.timeout)
            marker_path = work / "smoke-result.json"
            if not marker_path.is_file():
                raise ValueError(f"Native launch {launch} did not produce smoke-result.json (exit {status}).")
            marker = json.loads(marker_path.read_text(encoding="utf-8"))
            write_json(args.output / f"launch-{launch}.json", marker)
            if status != 0:
                raise ValueError(f"Native launch {launch} failed with exit status {status}.")
            validate_marker(marker)
            results.append({"launch": launch, "mode": launch_mode, "exitCode": status, "marker": marker})
    write_json(args.output / "package-smoke.json", {
        "success": True, "archive": archive.name, "sha256": expected_digest,
        "sourceCommit": args.sha, "launches": results,
    })
    print(f"Native package launch and restart passed: {archive.name}")


def checksums(args: argparse.Namespace) -> None:
    archives = sorted(args.directory.glob("PaperStager-*.zip"))
    if len(archives) != len(RIDS):
        raise ValueError(f"Expected {len(RIDS)} release archives, found {len(archives)}.")
    for rid in RIDS:
        if len([path for path in archives if path.name.endswith(f"-{rid}.zip")]) != 1:
            raise ValueError(f"Expected exactly one package for {rid}.")
    for archive in archives:
        if sha256(archive) != archive.with_suffix(".zip.sha256").read_text(encoding="ascii").split()[0]:
            raise ValueError(f"Archive checksum mismatch: {archive.name}")
        with zipfile.ZipFile(archive) as source:
            infos = [name for name in source.namelist() if name.count("/") == 1 and name.endswith("/package-info.json")]
            if len(infos) != 1:
                raise ValueError(f"Archive metadata is missing: {archive.name}")
            info = json.loads(source.read(infos[0]))
            if info.get("sourceCommit") != args.sha:
                raise ValueError(f"Release archives have inconsistent source SHAs: {archive.name}")
            if args.tag and args.tag != f"v{info.get('version')}":
                raise ValueError("Git tag does not match the packaged application version.")
    destination = args.directory / "SHA256SUMS"
    destination.write_text("".join(f"{sha256(path)}  {path.name}\n" for path in archives), encoding="ascii")
    print(f"Verified {len(archives)} package archives from {args.sha}; wrote {destination.name}")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    pack = commands.add_parser("package")
    pack.add_argument("--publish-dir", required=True, type=Path)
    pack.add_argument("--rid", choices=RIDS, required=True)
    pack.add_argument("--output", type=Path, default=ROOT / "artifacts" / "release")
    pack.add_argument("--version")
    pack.add_argument("--sha")
    pack.add_argument("--notices", default="THIRD-PARTY-NOTICES.md")
    pack.add_argument("--runtime-package-dir", type=Path, help="Override the resolved runtime NuGet package directory.")
    pack.set_defaults(handler=package)
    launch = commands.add_parser("smoke")
    launch.add_argument("--archive", required=True, type=Path)
    launch.add_argument("--sha", required=True)
    launch.add_argument("--output", required=True, type=Path)
    launch.add_argument("--timeout", type=int, default=120)
    launch.set_defaults(handler=smoke)
    sums = commands.add_parser("checksums")
    sums.add_argument("--directory", required=True, type=Path)
    sums.add_argument("--sha", required=True)
    sums.add_argument("--tag")
    sums.set_defaults(handler=checksums)
    args = parser.parse_args()
    try:
        args.handler(args)
        return 0
    except (ValueError, OSError, subprocess.SubprocessError, TimeoutError, zipfile.BadZipFile) as error:
        print(f"Release validation failed: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
