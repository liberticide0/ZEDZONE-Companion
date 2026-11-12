"""Prepare the user-local recipe converter without a system Python installation."""

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import runpy
import shutil
import sys
import tarfile
import tempfile
from urllib.parse import urlsplit
import urllib.request
import zipfile

MISSING_PACKAGES = 10
MAX_DOWNLOAD_BYTES = 32 * 1024 * 1024


def checked_file(path, expected):
    if not path.is_file() or path.stat().st_size > MAX_DOWNLOAD_BYTES:
        return False
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest() == expected


def download_package(url, output):
    with urllib.request.urlopen(url, timeout=20) as response, output.open("wb") as destination:
        target = urlsplit(response.url)
        if target.scheme != "https" or target.hostname != "files.pythonhosted.org":
            raise ValueError("Unexpected package download origin")
        data = response.read(MAX_DOWNLOAD_BYTES + 1)
        if len(data) > MAX_DOWNLOAD_BYTES:
            raise ValueError("Recipe converter package exceeds its size limit")
        destination.write(data)


def extract_package(source, destination):
    if source.name.endswith(".whl"):
        with zipfile.ZipFile(source) as wheel:
            for member in wheel.infolist():
                path = PurePosixPath(member.filename)
                if path.is_absolute() or any(part in ("..", ".") for part in path.parts) or "\\" in member.filename:
                    raise ValueError("Invalid path in converter wheel")
            wheel.extractall(destination)
        return

    # tpk_ar is published as pure Python source. Keep the package and its license,
    # and never run the source distribution's build scripts.
    with tarfile.open(source, "r:gz") as archive:
        for member in archive.getmembers():
            parts = PurePosixPath(member.name).parts
            if member.isfile() and parts[0] == "tpk_ar-0.2.4" and (
                (len(parts) == 3 and parts[1] == "tpk_ar" and parts[2].endswith(".py"))
                or parts == ("tpk_ar-0.2.4", "LICENSE")
            ):
                target = destination.joinpath(*parts[1:]) if len(parts) == 3 else destination / "tpk_ar-LICENSE"
                target.parent.mkdir(exist_ok=True)
                with archive.extractfile(member) as stream, target.open("wb") as output:
                    shutil.copyfileobj(stream, output)


def prepare_dependencies(lock_path, user_directory, package_directory, allow_download):
    lock = lock_path.read_bytes()
    packages = json.loads(lock)
    cache_id = hashlib.sha256(lock).hexdigest()[:16]
    root = user_directory / "converter"
    installed = root / cache_id
    ready = installed / ".ready"
    if ready.is_file() and ready.read_text(encoding="ascii") == cache_id:
        return installed

    missing = [package for package in packages
               if not checked_file(package_directory / package["name"], package["sha256"])]
    if missing and not allow_download:
        return None

    root.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="setup-", dir=root) as temporary:
        scratch = Path(temporary)
        dependencies = scratch / "dependencies"
        dependencies.mkdir()
        for package in packages:
            name = package["name"]
            path = package_directory / name
            if not checked_file(path, package["sha256"]):
                path = scratch / name
                download_package(package["url"], path)
            if not checked_file(path, package["sha256"]):
                raise ValueError(f"Converter package checksum mismatch: {name}")
            extract_package(path, dependencies)
        (dependencies / ".ready").write_text(cache_id, encoding="ascii")
        try:
            dependencies.rename(installed)
        except OSError:
            if not ready.is_file() or ready.read_text(encoding="ascii") != cache_id:
                raise
    return installed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("game", type=Path)
    parser.add_argument("--allow-download", action="store_true")
    parser.add_argument("--icons", action="store_true")
    parser.add_argument("--package-dir", type=Path)
    args = parser.parse_args()
    converter = Path(__file__).resolve().parent
    user_directory = Path(os.environ["LOCALAPPDATA"]) / "ZEDZONE.Companion"
    installed = prepare_dependencies(
        converter / "converter-packages.json", user_directory,
        args.package_dir or user_directory / "converter-packages", args.allow_download,
    )
    if installed is None:
        return MISSING_PACKAGES
    sys.path.append(str(installed))
    script = converter.parent / "probe-recipes.py"
    if args.icons:
        probe = runpy.run_path(str(script))
        icons = runpy.run_path(str(converter / "probe-icons.py"))
        icons["write_icons"](args.game, probe)
        return 0
    sys.argv = [str(script), str(args.game), "--snapshot"]
    runpy.run_path(str(script), run_name="__main__")
    return 0


if __name__ == "__main__":
    sys.exit(main())
