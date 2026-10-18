"""Cross-compile the offline txAdmin administrative tool for Windows x64."""

import argparse
import hashlib
import os
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--go", default="go", help="Path to the Go compiler (Go 1.23+)")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    output = root / "dist"
    output.mkdir(exist_ok=True)
    executable = output / "txAdmin-Unban.exe"
    environment = os.environ.copy()
    environment.update(GOOS="windows", GOARCH="amd64", CGO_ENABLED="0", GOTOOLCHAIN="local")
    subprocess.run(
        [args.go, "build", "-trimpath", "-buildvcs=false", "-ldflags=-H=windowsgui -s -w", "-o", str(executable), "."],
        cwd=root / "admin",
        env=environment,
        check=True,
    )
    digest = hashlib.sha256(executable.read_bytes()).hexdigest()
    (output / "txAdmin-Unban.sha256").write_text(f"{digest}  {executable.name}\n", encoding="ascii")
    print(f"Built: {executable}")
    print(f"SHA256: {digest}")


if __name__ == "__main__":
    main()
