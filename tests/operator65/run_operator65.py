import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess


def production_method(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth = 0
    for offset in range(opening, len(source)):
        if source[offset] == "{":
            depth += 1
        elif source[offset] == "}":
            depth -= 1
            if depth == 0:
                return source[start:offset + 1]
    raise ValueError("Production method closing brace missing for " + signature)


def main():
    root = Path(__file__).resolve().parents[2]
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", type=Path, default=root / "src" / "RhodesOperatorStatusUI.cs")
    parser.add_argument("--out-dir", type=Path, default=root / "out" / "tests" / "operator65")
    args = parser.parse_args()
    source = args.source.read_text(encoding="utf-8-sig")
    methods = [
        "public static void Bind(object hangarController)",
        "void ScheduleBuild()",
        "IEnumerator DeferredBuild()",
        "void OnEnable()",
        "void OnDisable()",
        "void Cleanup()",
        "void DestroyWidgets()",
    ]
    args.out_dir.mkdir(parents=True, exist_ok=True)
    generated = args.out_dir / "ProductionOperatorSessionMethods65.cs"
    generated.write_text(
        "using System; using System.Collections;\npublic sealed partial class RhodesOperatorStatusUI {\n"
        + "\n".join(production_method(source, method) for method in methods) + "\n}\n",
        encoding="utf-8",
    )
    fixture = Path(__file__).with_name("OperatorSessionFixture65.cs")
    executable = args.out_dir / "OperatorSessionFixture65.exe"
    windows = Path(os.environ["WINDIR"])
    compiler = windows / "Microsoft.NET" / "Framework64" / "v4.0.30319" / "csc.exe"
    if not compiler.is_file():
        compiler = windows / "Microsoft.NET" / "Framework" / "v4.0.30319" / "csc.exe"
    if not compiler.is_file():
        raise FileNotFoundError("Windows .NET Framework 4.x compiler required")
    subprocess.run([
        str(compiler), "/nologo", "/utf8output", "/warn:4", "/warnaserror+",
        "/out:" + str(executable), str(generated), str(fixture),
    ], check=True)
    completed = subprocess.run([str(executable)], capture_output=True, text=True)
    result = completed.stdout + completed.stderr
    print(result, end="")
    (args.out_dir / "OPERATOR_SESSION65_RESULT.txt").write_text(result, encoding="utf-8")
    hashes = {
        str(path.resolve()): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in (args.source, generated, fixture, Path(__file__), executable)
    }
    (args.out_dir / "VERIFIED_OPERATOR_SESSION65_SHA256.json").write_text(
        json.dumps(hashes, indent=2), encoding="utf-8"
    )
    return completed.returncode


if __name__ == "__main__":
    raise SystemExit(main())
