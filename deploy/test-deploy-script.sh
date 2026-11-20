#!/usr/bin/env bash
# Tests deploy.sh without Docker: a stub "docker" in the PATH answers the few commands the script uses.
#   bash deploy/test-deploy-script.sh        (also run by CI)
# Covers the cases that matter on the VPS: the very first deployment (no older images), cleanup of old images,
# a missing image, a container that never gets healthy, and a failing cleanup (must not fail the deployment).
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
mkdir -p "$WORK/app/deploy" "$WORK/bin"
cp "$HERE/deploy.sh" "$WORK/app/deploy/deploy.sh"

cat > "$WORK/bin/docker" <<'STUB'
#!/usr/bin/env bash
# Behaviour is controlled by environment variables: STUB_TAGS (tags of "docker images"), STUB_MISSING,
# STUB_HEALTH, STUB_RMI_FAIL; removed tags are appended to $STUB_LOG.
case "${1:-}" in
  image)
    case "${2:-}" in
      inspect)
        if [ "${3:-}" = "-f" ]; then
          tag="${5#breakbar-logs:}"; echo "2026-11-${tag: -2}T00:00:00Z"   # t05 is newer than t01
        elif [ -n "${STUB_MISSING:-}" ]; then
          exit 1
        fi
        exit 0 ;;
      prune) exit 0 ;;
    esac ;;
  compose) if [[ " $* " == *" ps "* ]]; then echo stubcontainer; fi; exit 0 ;;
  inspect) echo "${STUB_HEALTH:-healthy}"; exit 0 ;;
  images) for t in $STUB_TAGS; do echo "$t"; done; exit 0 ;;
  rmi) echo "$2" >> "$STUB_LOG"; [ -n "${STUB_RMI_FAIL:-}" ] && exit 1; exit 0 ;;
  logs) exit 0 ;;
esac
exit 0
STUB
chmod +x "$WORK/bin/docker"

failures=0
check() { # description, condition result (0 = ok)
  if [ "$2" -eq 0 ]; then echo "  ok   $1"; else echo "  FAIL $1"; failures=$((failures + 1)); fi
}

# run_deploy <tag> [VAR=value ...]  ->  RC, OUT, REMOVED (space separated tags)
run_deploy() {
  local tag="$1"; shift
  : > "$WORK/removed.log"
  OUT="$(env PATH="$WORK/bin:$PATH" STUB_LOG="$WORK/removed.log" IMAGE_TAG="$tag" HEALTH_TIMEOUT=2 "$@" \
        bash "$WORK/app/deploy/deploy.sh" 2>&1)"
  RC=$?
  REMOVED="$(sed 's/^breakbar-logs://' "$WORK/removed.log" | sort | tr '\n' ' ')"
}

echo "first deployment: only 'latest' and the new tag exist"
run_deploy t01 STUB_TAGS="latest t01"
check "exit code 0" "$RC"
case "$OUT" in *"Done: running breakbar-logs:t01"*) check "reports success" 0 ;; *) check "reports success" 1 ;; esac
check "nothing removed" "$([ -z "$REMOVED" ] && echo 0 || echo 1)"

echo "cleanup: keep the 2 newest older images"
run_deploy t05 STUB_TAGS="latest t05 t04 t03 t02 t01" KEEP_IMAGES=2
check "exit code 0" "$RC"
check "removes only the two oldest (t01 t02)" "$([ "$REMOVED" = "t01 t02 " ] && echo 0 || echo 1)"

echo "rollback to an older tag keeps the newer ones within the limit"
run_deploy t03 STUB_TAGS="latest t05 t04 t03 t02 t01" KEEP_IMAGES=3
check "exit code 0" "$RC"
check "never removes the tag being started" "$(case "$REMOVED" in *t03*) echo 1 ;; *) echo 0 ;; esac)"

echo "image was not transferred"
run_deploy t09 STUB_TAGS="latest" STUB_MISSING=1
check "exit code 1" "$([ "$RC" -eq 1 ] && echo 0 || echo 1)"
case "$OUT" in *"is not loaded on this host"*) check "clear message" 0 ;; *) check "clear message" 1 ;; esac

echo "container never becomes healthy"
run_deploy t06 STUB_TAGS="latest t06" STUB_HEALTH=starting
check "exit code 1" "$([ "$RC" -eq 1 ] && echo 0 || echo 1)"
case "$OUT" in *"deployment failed"*) check "reports the failure" 0 ;; *) check "reports the failure" 1 ;; esac

echo "a failing cleanup must not fail a healthy deployment"
run_deploy t05 STUB_TAGS="latest t05 t04 t03 t02 t01" KEEP_IMAGES=1 STUB_RMI_FAIL=1
check "exit code 0" "$RC"

echo
if [ "$failures" -eq 0 ]; then echo "All deploy.sh checks passed."; else echo "$failures check(s) failed."; exit 1; fi
