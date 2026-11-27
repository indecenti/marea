#!/usr/bin/env bash
# End-to-end test of Marea on Linux: frees real files, refills them through the fanotify agent, and
# checks every safety rule (in-use files kept, wrong bytes refused, notice when the agent is down,
# the systemd service). Works on a scratch folder in the user's home; touches no real data.
# Usage (as root): build/linux-selftest.sh <path to mareactl> <test user>
set -u
BIN="$1"
U="${2:-${SUDO_USER:-}}"
[ -n "$U" ] && [ "$U" != root ] || { echo "usage: $0 <mareactl> <non-root user>"; exit 64; }
H=$(getent passwd "$U" | cut -d: -f6)/marea-selftest
rm -rf "$H"; mkdir -p "$H/bin" "$H/models/sub" "$H/mirror" "$H/data"
cp "$BIN" "$H/bin/mareactl"; chmod 755 "$H/bin/mareactl"
chown -R $U:$U "$H"
AS_USER() { runuser -u $U -- env HOME="$(dirname "$H")" MAREA_DATA="$H/data" MAREA_LANG=en "$@"; }
CTL="$H/bin/mareactl"
pass=0; fail=0
ok() { echo "  PASS: $*"; pass=$((pass+1)); }
ko() { echo "  FAIL: $*"; fail=$((fail+1)); }

echo "== model files (random, 300 MB + 120 MB)"
AS_USER python3 - "$H/models" <<'EOF'
import os, sys
d = sys.argv[1]
for name, mb in (("model-a.gguf", 300), ("sub/model-b.safetensors", 120)):
    with open(os.path.join(d, name), "wb") as f:
        for _ in range(mb):
            f.write(os.urandom(1 << 20))
EOF
SHA_A=$(sha256sum "$H/models/model-a.gguf" | cut -c1-64)
SHA_B=$(sha256sum "$H/models/sub/model-b.safetensors" | cut -c1-64)
touch -d '30 days ago' "$H/models/model-a.gguf" "$H/models/sub/model-b.safetensors"
chmod 444 "$H/models/sub/model-b.safetensors"   # read-only, like huggingface_hub 2.x shared blobs
MT_A=$(stat -c %y "$H/models/model-a.gguf")

echo "== add, settings, scan, replicate to the mirror"
AS_USER "$CTL" add "$H/models"
AS_USER python3 - "$H/data/settings.json" "$H/mirror" <<'EOF'
import json, sys
p = sys.argv[1]; c = json.load(open(p, encoding="utf-8-sig"))
c.update(MinIdleDays=0, DiscoverHubRepos=False, HubNameSearch=False, CheckForUpdates=False, WatchSources=False,
         OllamaSource=False, MirrorRoot=sys.argv[2])
json.dump(c, open(p, "w", encoding="utf-8"), indent=1)
EOF
AS_USER "$CTL" scan > /dev/null 2>&1
AS_USER "$CTL" replicate 2>/dev/null | tail -1
AS_USER "$CTL" scan > /dev/null 2>&1
AS_USER "$CTL" plan 2>/dev/null | tail -4

echo "== without the agent: free must refuse"
AS_USER "$CTL" free --all --yes 2>&1 | tail -3
python3 -c "import os,sys; sys.exit(0 if 'user.marea.id' not in os.listxattr(sys.argv[1]) else 1)" "$H/models/model-a.gguf" \
  && ok "nothing freed without an agent" || ko "freed without an agent"

echo "== start the agent (as the user, with the service's capabilities)"
CAPS=+sys_admin,+dac_override,+fowner,+lease
setpriv --reuid=$U --regid=$U --init-groups --inh-caps=$CAPS --ambient-caps=$CAPS \
  env HOME="$(dirname "$H")" MAREA_DATA="$H/data" MAREA_LANG=en "$CTL" agent > "$H/agent.log" 2>&1 &
AG=$!
sleep 3
cat "$H/agent.log"

echo "== a program has model-a mapped: free must keep it"
AS_USER python3 -c "
import mmap, sys, time
f = open(sys.argv[1], 'rb'); m = mmap.mmap(f.fileno(), 0, access=mmap.ACCESS_READ); f.close()
open(sys.argv[2], 'w').close(); time.sleep(25)" "$H/models/model-a.gguf" "$H/mapped" &
MP=$!
while [ ! -e "$H/mapped" ]; do sleep 0.2; done
AS_USER "$CTL" free --all --yes 2>&1 | tail -3
python3 -c "import os,sys; sys.exit(0 if 'user.marea.id' not in os.listxattr(sys.argv[1]) else 1)" "$H/models/model-a.gguf" \
  && ok "mapped file kept" || ko "mapped file was freed"
python3 -c "import os,sys; sys.exit(0 if 'user.marea.id' in os.listxattr(sys.argv[1]) else 1)" "$H/models/sub/model-b.safetensors" \
  && ok "unused file freed" || ko "unused file not freed"
wait $MP 2>/dev/null

echo "== free model-a now"
AS_USER "$CTL" free --all --yes 2>&1 | tail -2
for f in model-a.gguf sub/model-b.safetensors; do
  stat -c "  %n: size %s, on disk %b blocks, mtime %y" "$H/models/$f"
done
[ "$(stat -c %b "$H/models/model-a.gguf")" -le 16 ] && ok "blocks released" || ko "blocks still allocated"
[ "$(stat -c %a "$H/models/sub/model-b.safetensors")" = 444 ] && ok "read-only file freed, mode kept" || ko "read-only file: mode $(stat -c %a "$H/models/sub/model-b.safetensors")"
[ "$(stat -c %s "$H/models/model-a.gguf")" = $((300<<20)) ] && ok "size kept" || ko "size changed"
[ "$(stat -c %y "$H/models/model-a.gguf")" = "$MT_A" ] && ok "write time kept" || ko "write time changed"
AS_USER "$CTL" status 2>/dev/null | head -5

echo "== a program reads model-a -> refilled, verified"
t0=$(date +%s.%N)
GOT=$(AS_USER sha256sum "$H/models/model-a.gguf" | cut -c1-64)
t1=$(date +%s.%N)
[ "$GOT" = "$SHA_A" ] && ok "read the right bytes ($(awk "BEGIN{printf \"%.1f\", $t1 - $t0}") s)" || ko "wrong bytes: $GOT"
python3 -c "import os,sys; sys.exit(0 if 'user.marea.id' not in os.listxattr(sys.argv[1]) else 1)" "$H/models/model-a.gguf" \
  && ok "identity removed after refill" || ko "still marked"
[ "$(stat -c %y "$H/models/model-a.gguf")" = "$MT_A" ] && ok "write time kept after refill" || ko "write time changed after refill"
[ "$(stat -c %U "$H/models/model-a.gguf")" = "$U" ] && ok "owner kept" || ko "owner changed"

echo "== two programs open model-b at the same time"
(sha256sum "$H/models/sub/model-b.safetensors" > "$H/r1") & R1=$!
(AS_USER sha256sum "$H/models/sub/model-b.safetensors" > "$H/r2") & R2=$!
wait $R1 $R2
[ "$(cut -c1-64 "$H/r1")" = "$SHA_B" ] && [ "$(cut -c1-64 "$H/r2")" = "$SHA_B" ] && ok "both readers got the right bytes (read-only file)" || ko "concurrent readers: $(cat "$H/r1" "$H/r2")"

echo "== the source is corrupted: the open must fail, the file stay freed"
sleep 3
AS_USER "$CTL" journal 6 2>/dev/null | tail -6
AS_USER "$CTL" free --all --yes 2>&1 | grep -v Bit-by | tail -4
cp -a "$H/mirror" "$H/mirror-backup"
for m in $(find "$H/mirror" -type f -size +100M); do printf 'X' | dd of="$m" bs=1 seek=1000 conv=notrunc 2>/dev/null; done
if AS_USER head -c 16 "$H/models/model-a.gguf" > /dev/null 2>"$H/err"; then ko "open allowed with a bad source"; else ok "open refused: $(cat "$H/err")"; fi
python3 -c "import os,sys; sys.exit(0 if 'user.marea.id' in os.listxattr(sys.argv[1]) else 1)" "$H/models/model-a.gguf"   && ok "still freed after the refused open" || ko "lost the freed mark"
rm -rf "$H/mirror"; mv "$H/mirror-backup" "$H/mirror"

echo "== agent stopped: a read shows the notice, not zeros"
kill -TERM $AG; wait $AG 2>/dev/null
HEAD=$(AS_USER head -c 16 "$H/models/model-a.gguf")
[ "$HEAD" = "MAREA-FREED-FILE" ] && ok "notice at the start of a freed file" || ko "head is '$HEAD'"
AS_USER "$CTL" restore "$H/models/model-a.gguf" 2>&1 | tail -1

echo "== agent restarted: freed files are armed again; remove brings everything back"
setpriv --reuid=$U --regid=$U --init-groups --inh-caps=$CAPS --ambient-caps=$CAPS   env HOME="$(dirname "$H")" MAREA_DATA="$H/data" MAREA_LANG=en "$CTL" agent > "$H/agent2.log" 2>&1 &
AG=$!
sleep 3
AS_USER "$CTL" remove "$H/models" 2>&1 | tail -1
[ "$(sha256sum "$H/models/model-a.gguf" | cut -c1-64)" = "$SHA_A" ] && ok "model-a back and verified" || ko "model-a wrong after remove"
[ "$(sha256sum "$H/models/sub/model-b.safetensors" | cut -c1-64)" = "$SHA_B" ] && ok "model-b back and verified" || ko "model-b wrong after remove"
python3 -c "import os,sys; sys.exit(0 if not os.listxattr(sys.argv[1]) else 1)" "$H/models" && ok "folder unmarked" || ko "folder still marked"
kill -TERM $AG; wait $AG 2>/dev/null
echo "--- agent log"; cat "$H/agent.log" "$H/agent2.log"

echo "== the real service: sudo mareactl service install (pointed at the test data with a drop-in)"
if [ -e /etc/systemd/system/marea-agent@.service ] || [ -e /usr/local/bin/mareactl ]; then
  echo "  Marea is already installed as a service here: skipped (this test would replace it)"
elif [ "$(systemctl is-system-running 2>/dev/null)" != "offline" ] && command -v systemctl > /dev/null; then
  AS_USER "$CTL" add "$H/models" > /dev/null; AS_USER "$CTL" scan > /dev/null 2>&1
  mkdir -p /etc/systemd/system/marea-agent@$U.service.d
  printf '[Service]
Environment=MAREA_DATA=%s
Environment=MAREA_LANG=en
' "$H/data" > /etc/systemd/system/marea-agent@$U.service.d/test.conf
  SUDO_USER=$U "$CTL" service install
  sleep 3
  systemctl is-active marea-agent@$U && ok "service active" || ko "service not active"
  stat -c '  %n %U %a' /usr/local/bin/mareactl
  AS_USER "$CTL" free --all --yes 2>&1 | grep -v Bit-by | tail -1
  GOT=$(AS_USER sha256sum "$H/models/model-a.gguf" | cut -c1-64)
  [ "$GOT" = "$SHA_A" ] && ok "service refilled model-a" || ko "service: wrong bytes"
  systemctl restart marea-agent@$U; sleep 3
  GOT=$(sha256sum "$H/models/sub/model-b.safetensors" | cut -c1-64)
  [ "$GOT" = "$SHA_B" ] && ok "after a service restart model-b still comes back" || ko "after restart: $GOT"
  SUDO_USER=$U "$CTL" service uninstall; echo "  exit without --yes: $?"
  AS_USER "$CTL" remove "$H/models" 2>&1 | tail -1
  SUDO_USER=$U "$CTL" service uninstall --yes
  journalctl -u marea-agent@$U --no-pager -n 8 | sed 's/^/  journal: /'
  rm -rf /etc/systemd/system/marea-agent@$U.service.d /etc/systemd/system/marea-agent@.service /usr/local/bin/mareactl
  systemctl daemon-reload
else
  echo "  systemd not running here: skipped"
fi

echo "== totals: $pass passed, $fail failed"
rm -rf "$H"
[ "$fail" -eq 0 ]
