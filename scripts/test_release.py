"""Focused tests for packaging trust boundaries and fail-closed audit handling."""

import json
from pathlib import Path
import plistlib
import shutil
import subprocess
import sys
import tempfile
import unittest
import zipfile

import audit_dependencies
import release
import security_scan


class ReleaseTests(unittest.TestCase):
    def test_macos_layout_rejects_loose_managed_payload(self):
        with tempfile.TemporaryDirectory() as temporary:
            app = Path(temporary) / "PaperStager.app"
            payload = app / "Contents" / "MacOS"
            payload.mkdir(parents=True)
            (payload / "PaperStager").write_bytes(bytes.fromhex("cffaedfe") + b"synthetic")
            (payload / "Example.dll").write_bytes(b"MZ synthetic managed assembly")
            with self.assertRaisesRegex(ValueError, "Non-Mach-O"):
                release.macos_code_files(app)

    @unittest.skipUnless(sys.platform == "darwin", "Requires Apple's native codesign tool")
    def test_final_bundle_signature_survives_zip_and_rejects_tampering(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            app = root / "Package" / "PaperStager.app"
            payload = app / "Contents" / "MacOS"
            resources = app / "Contents" / "Resources"
            (payload / "_macarm").mkdir(parents=True)
            resources.mkdir()
            # Copies only: never alter OS binaries or their signatures.
            for target in (payload / "PaperStager", payload / "_macarm" / "helper"):
                shutil.copyfile("/usr/bin/true", target)
                target.chmod(0o755)
            (resources / "notice.txt").write_text("original resource", encoding="utf-8")
            with (app / "Contents" / "Info.plist").open("wb") as stream:
                plistlib.dump({"CFBundleIdentifier": "org.paperstager.desktop", "CFBundleExecutable": "PaperStager",
                              "CFBundlePackageType": "APPL"}, stream)
            release.sign_macos_bundle(app)
            with zipfile.ZipFile(root / "package.zip", "w") as archive:
                for path in sorted((root / "Package").rglob("*")):
                    if path.is_file():
                        archive.write(path, path.relative_to(root))
            unpacked = release.extract_checked(root / "package.zip", root / "extracted") / "PaperStager.app"
            self.assertTrue(release.verify_macos_signature(unpacked)["success"])
            notice = unpacked / "Contents" / "Resources" / "notice.txt"
            notice.write_text("tampered resource", encoding="utf-8")
            with self.assertRaises(subprocess.CalledProcessError):
                release.verify_macos_signature(unpacked)
            notice.write_text("original resource", encoding="utf-8")
            release.verify_macos_signature(unpacked)
            helper = unpacked / "Contents" / "MacOS" / "_macarm" / "helper"
            data = bytearray(helper.read_bytes())
            data[len(data) // 2] ^= 1
            helper.write_bytes(data)
            with self.assertRaises(subprocess.CalledProcessError):
                release.verify_macos_signature(unpacked)

    def test_traversal_and_symlink_archives_are_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for name in ("../escape.txt", "/outside.txt", "Root/C:\\escape.txt"):
                with zipfile.ZipFile(root / "bad.zip", "w") as archive:
                    archive.writestr(name, "unsafe")
                with self.assertRaises(ValueError):
                    release.extract_checked(root / "bad.zip", root / "unpacked")
            with zipfile.ZipFile(root / "bad.zip", "w") as archive:
                info = zipfile.ZipInfo("Root/link")
                info.external_attr = 0o120777 << 16
                archive.writestr(info, "../../outside")
            with self.assertRaises(ValueError):
                release.extract_checked(root / "bad.zip", root / "unpacked")

    def test_archive_extract_preserves_unicode_and_executable(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            with zipfile.ZipFile(root / "good.zip", "w") as archive:
                info = zipfile.ZipInfo("PaperStager/문서/PaperStager")
                info.external_attr = 0o100755 << 16
                archive.writestr(info, "synthetic")
            extracted = release.extract_checked(root / "good.zip", root / "out")
            self.assertEqual((extracted / "문서/PaperStager").read_text(), "synthetic")

    def test_partial_smoke_evidence_cannot_pass(self):
        marker = {"success": True, "sourcePreserved": True, "nativeWindow": True, "importedPages": 4,
                  "exportedDocuments": 2, "thumbnailsRendered": 4}
        release.validate_marker(marker)
        for field in marker:
            incomplete = dict(marker)
            incomplete.pop(field)
            with self.assertRaises(ValueError):
                release.validate_marker(incomplete)

    def test_manifest_cannot_omit_payload_or_change_content(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            metadata = {"executable": "PaperStager", "notices": "NOTICES.md"}
            for name in ("PaperStager", "package-info.json", "README.md", "LICENSE", "NOTICES.md"):
                (root / name).write_text("synthetic", encoding="utf-8")
            manifest = {path.name: release.sha256(path) for path in root.iterdir()}
            release.write_json(root / "file-checksums.json", {})
            with self.assertRaises(ValueError):
                release.validate_payload(root, metadata)
            release.write_json(root / "file-checksums.json", manifest)
            release.validate_payload(root, metadata)
            (root / "unlisted.dll").write_text("extra", encoding="utf-8")
            with self.assertRaises(ValueError):
                release.validate_payload(root, metadata)
            (root / "unlisted.dll").unlink()
            (root / "PaperStager").write_text("tampered", encoding="utf-8")
            with self.assertRaises(ValueError):
                release.validate_payload(root, metadata)

    def test_scan_detects_credentials_without_including_values(self):
        synthetic = ("ghp_" + "A" * 36).encode()
        matches = security_scan.scan_bytes(b"first line\n" + synthetic)
        self.assertEqual(matches, [{"rule": "github-token", "line": 2}])
        self.assertNotIn(synthetic.decode(), json.dumps(matches))

    def test_audit_rejects_moderate_and_unknown_or_empty_data(self):
        baseline = {"version": 1, "sources": ["https://api.nuget.org/v3/index.json"],
                    "projects": [{"path": "Example.csproj"}]}
        inventory = {"version": 1, "projects": [{"path": "Example.csproj", "frameworks": [
            {"framework": "net10.0", "topLevelPackages": [{"id": "Synthetic.Package"}]}]}]}
        # NuGet omits frameworks entirely when no vulnerabilities are found.
        self.assertTrue(audit_dependencies.validate_report(baseline, inventory)["success"])
        package = {"id": "Synthetic.Package", "resolvedVersion": "1.0.0",
                   "vulnerabilities": [{"severity": "Moderate", "advisoryurl": "https://example.invalid/advisory"}]}
        baseline["projects"][0]["frameworks"] = [{"transitivePackages": [package]}]
        self.assertFalse(audit_dependencies.validate_report(baseline, inventory)["success"])
        package["vulnerabilities"][0]["severity"] = "unknown"
        with self.assertRaises(ValueError):
            audit_dependencies.validate_report(baseline, inventory)
        with self.assertRaises(ValueError):
            audit_dependencies.validate_report({}, inventory)


if __name__ == "__main__":
    unittest.main()
