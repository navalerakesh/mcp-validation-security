#!/usr/bin/env python3

import argparse
import gzip
import io
import pathlib
import tarfile
import zipfile

FIXED_ZIP_TIME = (1980, 1, 1, 0, 0, 0)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--input", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--windows", action="store_true")
    args = parser.parse_args()

    source = pathlib.Path(args.input)
    destination = pathlib.Path(args.output)
    if not source.is_file() or source.is_symlink():
        raise SystemExit("standalone input must be a regular non-symlink file")

    payload = source.read_bytes()
    destination.parent.mkdir(parents=True, exist_ok=True)
    if args.windows:
        info = zipfile.ZipInfo(source.name, FIXED_ZIP_TIME)
        info.create_system = 0
        info.external_attr = 0o100644 << 16
        info.compress_type = zipfile.ZIP_DEFLATED
        with zipfile.ZipFile(destination, "w") as archive:
            archive.writestr(info, payload)
        return

    info = tarfile.TarInfo(source.name)
    info.size = len(payload)
    info.mode = 0o755
    info.mtime = 0
    info.uid = 0
    info.gid = 0
    info.uname = "root"
    info.gname = "root"
    with destination.open("wb") as raw:
        with gzip.GzipFile(filename="", mode="wb", fileobj=raw, mtime=0) as compressed:
            with tarfile.open(fileobj=compressed, mode="w") as archive:
                archive.addfile(info, io.BytesIO(payload))


if __name__ == "__main__":
    main()
