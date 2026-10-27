#!/bin/zsh
# Compile Assembly-CSharp outside Unity with Unity's bundled Roslyn, in two flavors:
#   editor : exactly what the Unity Editor compiles (UNITY_EDITOR defined, editor DLLs referenced)
#   player : what a macOS Mono player build compiles (no editor defines/DLLs, player engine DLLs)
# Works whether Unity is open or closed. Needs Unity to have compiled the project once
# (reads compiler options from Library/Bee/.../Assembly-CSharp.rsp); the source list is rebuilt
# from the file system so newly added scripts are included.
#
# Usage: MLTraining/tools/compile-check.sh            -> exit 0 if both flavors compile
#        VERBOSE=1 MLTraining/tools/compile-check.sh  -> also print warnings
set -u
ROOT="${0:A:h:h:h}"
U=/Applications/Unity/Hub/Editor/2022.3.62f1/Unity.app/Contents
P="$U/PlaybackEngines/MacStandaloneSupport/Variations/mono/Managed"
OUT="$ROOT/MLTraining/tools/.out/compile-check"
mkdir -p "$OUT"
cd "$ROOT" || exit 2

RSP=$(ls -t Library/Bee/artifacts/*.dag/Assembly-CSharp.rsp 2>/dev/null | head -1)
[[ -z "$RSP" ]] && { echo "No Assembly-CSharp.rsp found: open the project in Unity once."; exit 2; }

python3 - "$RSP" "$OUT" "$U" "$P" <<'EOF'
import os, sys, subprocess
rsp, out, U, P = sys.argv[1:]
opts = [l for l in open(rsp).read().splitlines() if l.startswith(("-", "/"))]
opts = [l for l in opts if not l.startswith(("-out:", "-refout:"))]
# Same rule Unity uses when there are no .asmdef files: every script outside an Editor/ folder
srcs = sorted(
    os.path.join(d, f)
    for d, _, fs in os.walk("Assets")
    for f in fs
    if f.endswith(".cs") and "/Editor/" not in os.path.join(d, f)
)
def write(name, lines):
    with open(f"{out}/{name}.rsp", "w") as fh:
        fh.write("\n".join(lines + [f'-out:"{out}/{name}.dll"'] + [f'"{s}"' for s in srcs]) + "\n")
write("editor", opts)
player = []
for l in opts:
    if l.startswith("-define:") and "EDITOR" in l:
        continue
    if l.startswith("-r:"):
        path = l[3:].strip('"')
        base = os.path.basename(path)
        if "Editor" in base:
            continue
        if f"{U}/Managed/UnityEngine/" in path and os.path.exists(f"{P}/{base}"):
            l = f'-r:"{P}/{base}"'
    player.append(l)
write("player", player)
print(f"{len(srcs)} scripts")
EOF

result=0
for flavor in editor player; do
  log="$OUT/$flavor.log"
  "$U/NetCoreRuntime/dotnet" exec "$U/DotNetSdkRoslyn/csc.dll" -nologo @"$OUT/$flavor.rsp" > "$log" 2>&1
  rc=$?
  errors=$(/usr/bin/grep -c " error " "$log")
  warnings=$(/usr/bin/grep -c " warning " "$log")
  if [[ $rc -eq 0 ]]; then echo "$flavor: OK ($warnings warnings)"; else echo "$flavor: FAILED ($errors errors)"; result=1; fi
  /usr/bin/grep " error " "$log" | sed -E "s#^$ROOT/##" | sort -u | head -20
  [[ "${VERBOSE:-0}" == 1 ]] && /usr/bin/grep " warning " "$log" | sed -E "s#^$ROOT/##" | sort -u
done
exit $result
