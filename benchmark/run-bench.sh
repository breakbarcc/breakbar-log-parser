#!/usr/bin/env bash
# Elite Insights benchmark for Linux (the VPS).
#
# Runs the EI CLI once per log (one process per log, like a worker job) in three batches:
#   A  concurrency 1, SingleThreaded=false  (one log uses all cores)
#   B  concurrency 1, SingleThreaded=true   (baseline per log on one core)
#   C  concurrency 2, SingleThreaded=true   (two logs in parallel)
# Per log: wall time, EI "elapsed", peak RSS (GNU time), output sizes.
# Per batch: total wall time, logs/hour, peak increase of system memory in use.
#
# Usage:
#   ./run-bench.sh /path/to/GuildWars2EliteInsights-CLI.dll [options]
# Options:
#   --logs DIR             log directory (default: ./logs next to this script)
#   --results DIR          result root (default: ./results)
#   --repeat N             repeat all batches N times (default 1)
#   --batches A,B,C        which batches to run (default A,B,C)
#   --no-combat-replay     ParseCombatReplay=false
#   --nice N               run EI with nice level N (default 10, like production)
# Env:
#   DOTNET                 path to the dotnet host (default: dotnet, or ~/.dotnet/dotnet)

set -uo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
EI_PATH="${1:-}"
[ -z "$EI_PATH" ] && { sed -n '2,22p' "$0"; exit 1; }
shift
LOG_DIR="$SCRIPT_DIR/logs"
RESULT_ROOT="$SCRIPT_DIR/results"
REPEAT=1
BATCHES="A,B,C"
CR=true
NICE_LEVEL=10

while [ $# -gt 0 ]; do
  case "$1" in
    --logs) LOG_DIR="$2"; shift 2 ;;
    --results) RESULT_ROOT="$2"; shift 2 ;;
    --repeat) REPEAT="$2"; shift 2 ;;
    --batches) BATCHES="$2"; shift 2 ;;
    --no-combat-replay) CR=false; shift ;;
    --nice) NICE_LEVEL="$2"; shift 2 ;;
    *) echo "Unknown option: $1"; exit 1 ;;
  esac
done

EI_PATH="$(realpath "$EI_PATH")"
if [ -z "${DOTNET:-}" ]; then
  if command -v dotnet >/dev/null 2>&1; then DOTNET=dotnet
  elif [ -x "$HOME/.dotnet/dotnet" ]; then DOTNET="$HOME/.dotnet/dotnet"
  else DOTNET=dotnet; fi
fi
case "$EI_PATH" in
  *.dll) EI_CMD=("$DOTNET" "$EI_PATH") ;;
  *) EI_CMD=("$EI_PATH") ;;
esac

[ -x /usr/bin/time ] || { echo "GNU time missing: sudo apt install time"; exit 1; }
command -v "${EI_CMD[0]}" >/dev/null 2>&1 || [ -x "${EI_CMD[0]}" ] || { echo "Not found: ${EI_CMD[0]}"; exit 1; }

# EI calls Settings.Upgrade() at startup. .NET resolves the user config folder via
# LocalApplicationData (~/.local/share) and returns an empty path if that folder does not
# exist, which crashes EI with ArgumentNullException (exit code 134) on headless servers.
mkdir -p "${XDG_DATA_HOME:-$HOME/.local/share}"

# Sanity check: EI must start at all (e.g. missing libicu makes .NET abort with exit code 134)
if ! VCHECK="$("${EI_CMD[@]}" -v 2>&1)"; then
  echo "Elite Insights does not start. Output:"
  echo "$VCHECK" | head -5
  echo "Hints: missing ICU -> 'sudo apt install libicu76'; 'Parameter path' error -> 'mkdir -p ~/.local/share'."
  exit 1
fi

TEMPLATE="$SCRIPT_DIR/ei-bench.template.conf"
mapfile -t LOGS < <(find "$LOG_DIR" -maxdepth 1 -type f \( -iname '*.evtc' -o -iname '*.zevtc' -o -iname '*.evtc.zip' \) | sort)
[ "${#LOGS[@]}" -eq 0 ] && { echo "No .evtc/.zevtc/.evtc.zip files in $LOG_DIR"; exit 1; }

RUN_ROOT="$RESULT_ROOT/$(hostname)-$(date +%Y%m%d-%H%M%S)"
mkdir -p "$RUN_ROOT"
PER_LOG_CSV="$RUN_ROOT/per-log.csv"
BATCH_CSV="$RUN_ROOT/batches.csv"

evtc_bytes() {
  case "${1,,}" in
    *.evtc) stat -c %s "$1" ;;
    *) python3 -I -c 'import sys,zipfile; print(sum(i.file_size for i in zipfile.ZipFile(sys.argv[1]).infolist()))' "$1" 2>/dev/null || echo "" ;;
  esac
}

mem_used_mb() { awk '/^MemTotal:/{t=$2} /^MemAvailable:/{a=$2} END{printf "%d", (t-a)/1024}' /proc/meminfo; }

# ---------- system info ----------
{
  echo "host=$(hostname)"
  echo "os=$(. /etc/os-release && echo "$PRETTY_NAME") $(uname -r)"
  echo "cpu=$(awk -F: '/model name/{print $2; exit}' /proc/cpuinfo | sed 's/^ *//')"
  echo "logical_cores=$(nproc)"
  echo "ram_mb=$(awk '/^MemTotal:/{printf "%d", $2/1024}' /proc/meminfo)"
  echo "mem_used_before_mb=$(mem_used_mb)"
  echo "ei_path=$EI_PATH"
  echo "ei_version=$("${EI_CMD[@]}" -v 2>/dev/null | grep -E '^v[0-9]' | tail -1)"
  echo "dotnet=$("$DOTNET" --list-runtimes 2>/dev/null | tr '\n' ';')"
  echo "combat_replay=$CR"
  echo "nice=$NICE_LEVEL"
  echo "repeat=$REPEAT"
  echo "logs=${#LOGS[@]}"
} > "$RUN_ROOT/system.txt"
if command -v docker >/dev/null 2>&1; then
  docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}' > "$RUN_ROOT/docker-stats-before.txt" 2>/dev/null || true
fi

echo "batch,repeat,file,size_bytes,evtc_bytes,single_threaded,concurrency,exit_code,parsed,ei_elapsed_ms,wall_s,peak_rss_mb,html_bytes,json_bytes,out_total_bytes" > "$PER_LOG_CSV"
echo "batch,repeat,concurrency,single_threaded,logs,ok,total_wall_s,logs_per_hour,mem_used_increase_peak_mb" > "$BATCH_CSV"

# ---------- one EI run (one log) ----------
run_one() {
  local batch="$1" rep="$2" conc="$3" single="$4" log="$5" run_dir="$6"
  mkdir -p "$run_dir"
  sed -e "s|__OUT__|$run_dir|" -e "s|__SINGLE__|$single|" -e "s|__CR__|$CR|" "$TEMPLATE" > "$run_dir/bench.conf"

  ( cd "$run_dir" && DOTNET_gcServer=0 /usr/bin/time -f '%e %M' -o "$run_dir/time.txt" \
      nice -n "$NICE_LEVEL" "${EI_CMD[@]}" -c "$run_dir/bench.conf" "$log" \
      > "$run_dir/stdout.txt" 2> "$run_dir/stderr.txt" )
  local rc=$?

  local line parsed elapsed wall rss_kb html json total
  line="$(grep -m1 '^Processed - ' "$run_dir/stdout.txt" || true)"
  parsed="$(grep -o '"parsed":[a-z]*' <<<"$line" | cut -d: -f2)"
  elapsed="$(grep -o '"elapsed":[0-9]*' <<<"$line" | cut -d: -f2)"
  read -r wall rss_kb < <(tail -1 "$run_dir/time.txt")
  html="$(find "$run_dir" -type f -name '*.html' -printf '%s\n' | awk '{s+=$1} END{print s+0}')"
  json="$(find "$run_dir" -type f \( -name '*.json' -o -name '*.json.gz' \) -printf '%s\n' | awk '{s+=$1} END{print s+0}')"
  total="$(find "$run_dir" -type f ! -name bench.conf ! -name stdout.txt ! -name stderr.txt ! -name time.txt -printf '%s\n' | awk '{s+=$1} END{print s+0}')"

  echo "$batch,$rep,$(basename "$log"),$(stat -c %s "$log"),$(evtc_bytes "$log"),$single,$conc,$rc,$parsed,$elapsed,$wall,$(( ${rss_kb:-0} / 1024 )),$html,$json,$total" >> "$PER_LOG_CSV"
  printf '  %-45s exit=%s parsed=%-5s wall=%7ss peak=%6s MB\n' "$(basename "$log")" "$rc" "$parsed" "$wall" "$(( ${rss_kb:-0} / 1024 ))"
}

# ---------- one batch ----------
run_batch() {
  local batch="$1" rep="$2" conc="$3" single="$4"
  echo "Batch $batch (repeat $rep): concurrency=$conc SingleThreaded=$single"
  local base peak_file
  base="$(mem_used_mb)"
  peak_file="$RUN_ROOT/.mempeak"
  echo "$base" > "$peak_file"
  ( while :; do m="$(mem_used_mb)"; [ "$m" -gt "$(cat "$peak_file" 2>/dev/null || echo 0)" ] 2>/dev/null && echo "$m" > "$peak_file"; sleep 0.5; done ) &
  local sampler=$!

  local t0 i=0 pids=()
  t0="$(date +%s.%N)"
  for log in "${LOGS[@]}"; do
    i=$((i + 1))
    while [ "$(jobs -rp | grep -vcx "$sampler")" -ge "$conc" ]; do sleep 0.2; done
    run_one "$batch" "$rep" "$conc" "$single" "$log" "$RUN_ROOT/out/$batch-r$rep/$(printf '%03d' "$i")" &
    pids+=($!)
  done
  wait "${pids[@]}"
  local t1 secs ok per_hour peak
  t1="$(date +%s.%N)"
  kill "$sampler" 2>/dev/null; wait "$sampler" 2>/dev/null
  secs="$(awk -v a="$t0" -v b="$t1" 'BEGIN{printf "%.1f", b-a}')"
  ok="$(awk -F, -v b="$batch" -v r="$rep" '$1==b && $2==r && $9=="true"' "$PER_LOG_CSV" | wc -l)"
  per_hour="$(awk -v n="${#LOGS[@]}" -v s="$secs" 'BEGIN{ if (s>0) printf "%.1f", n*3600/s; else print 0 }')"
  peak=$(( $(cat "$peak_file") - base ))
  echo "$batch,$rep,$conc,$single,${#LOGS[@]},$ok,$secs,$per_hour,$peak" >> "$BATCH_CSV"
  echo "  -> total ${secs}s, ${per_hour} logs/h, memory in use rose by up to ${peak} MB"
}

echo "Logs: ${#LOGS[@]} | Results: $RUN_ROOT"
for rep in $(seq 1 "$REPEAT"); do
  [[ ",$BATCHES," == *",A,"* ]] && run_batch A "$rep" 1 false
  [[ ",$BATCHES," == *",B,"* ]] && run_batch B "$rep" 1 true
  [[ ",$BATCHES," == *",C,"* ]] && run_batch C "$rep" 2 true
done
rm -f "$RUN_ROOT/.mempeak"
echo "Done. Send these files: $PER_LOG_CSV $BATCH_CSV $RUN_ROOT/system.txt"
