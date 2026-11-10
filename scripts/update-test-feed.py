"""Loopback-only update fixture for packages built with -FixtureVersion."""

import argparse
import hashlib
import json
import re
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("archive", type=Path)
    parser.add_argument("--mode", choices=("ok", "none", "bad-hash", "unavailable"), default="ok")
    parser.add_argument("--delay", type=float, default=0, help="Optional pause in seconds per MiB")
    options = parser.parse_args()
    match = re.fullmatch(r"ZEDZONE-Companion-(\d+\.\d+\.\d+)-win-x64\.zip", options.archive.name)
    if match is None:
        parser.error("use a fixture ZIP with its generated filename")
    tag = f"v{match[1]}"
    with options.archive.open("rb") as archive:
        digest = hashlib.file_digest(archive, "sha256").hexdigest()
    size = options.archive.stat().st_size
    metadata = json.dumps({
        "draft": False, "prerelease": False, "tag_name": tag,
        "assets": [{
            "name": options.archive.name, "state": "uploaded", "size": size,
            "browser_download_url": (
                f"https://github.com/liberticide0/ZEDZONE-Companion/releases/download/{tag}/{options.archive.name}"
            ),
            "digest": "sha256:" + ("0" * 64 if options.mode == "bad-hash" else digest),
        }],
    }).encode()

    class Handler(BaseHTTPRequestHandler):
        def do_GET(self):
            if self.path == "/latest":
                if options.mode == "none":
                    self.send_error(404)
                elif options.mode == "unavailable":
                    self.send_error(503)
                else:
                    self.send_response(200)
                    self.send_header("Content-Type", "application/json")
                    self.send_header("Content-Length", str(len(metadata)))
                    self.end_headers()
                    self.wfile.write(metadata)
            elif self.path == "/asset" and options.mode in ("ok", "bad-hash"):
                self.send_response(200)
                self.send_header("Content-Type", "application/zip")
                self.send_header("Content-Length", str(size))
                self.end_headers()
                try:
                    with options.archive.open("rb") as archive:
                        while chunk := archive.read(1024 * 1024):
                            self.wfile.write(chunk)
                            if options.delay > 0:
                                time.sleep(options.delay)
                except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
                    self.log_message("fixture download cancelled")
            else:
                self.send_error(404)

    with ThreadingHTTPServer(("127.0.0.1", 48161), Handler) as server:
        print(f"Companion update fixture {tag} on http://127.0.0.1:48161 ({options.mode})", flush=True)
        try:
            server.serve_forever()
        except KeyboardInterrupt:
            pass


if __name__ == "__main__":
    main()
