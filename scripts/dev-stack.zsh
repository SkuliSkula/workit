#!/bin/zsh

set -euo pipefail

ROOT_DIR="${0:A:h:h}"
STATE_DIR="$ROOT_DIR/.workit-dev"

API_NAME="api"
OWNER_NAME="owner"
EMPLOYEE_NAME="employee"

API_URL="http://localhost:5200"
API_BROWSER_URL="http://localhost:5200/swagger"
OWNER_URL="http://localhost:5300"
EMPLOYEE_URL="http://localhost:5100"

mkdir -p "$STATE_DIR"

usage() {
  cat <<'EOF'
Usage:
  ./scripts/dev-stack.zsh up [--open]
  ./scripts/dev-stack.zsh down
  ./scripts/dev-stack.zsh restart [--open]
  ./scripts/dev-stack.zsh status
  ./scripts/dev-stack.zsh logs <api|owner|employee>

Commands:
  up       Starts API, Owner app, and Employee app with dotnet watch
  down     Stops all processes started by this script
  restart  Stops and starts the full stack again
  status   Shows running state, pid, log, and URL for each app
  logs     Tails the log for a single app

Options:
  --open   Opens the three local app/API URLs in browser tabs after startup
EOF
}

pid_file() {
  printf '%s/%s.pid' "$STATE_DIR" "$1"
}

log_file() {
  printf '%s/%s.log' "$STATE_DIR" "$1"
}

is_running() {
  local name="$1"
  local pid_path
  pid_path="$(pid_file "$name")"

  [[ -f "$pid_path" ]] || return 1

  local pid
  pid="$(<"$pid_path")"

  kill -0 "$pid" 2>/dev/null
}

start_app() {
  local name="$1"
  local url="$2"
  local project="$3"
  local artifacts_dir="$STATE_DIR/artifacts/$name"

  if is_running "$name"; then
    echo "$name is already running (pid $(<"$(pid_file "$name")"))."
    return
  fi

  local log_path
  log_path="$(log_file "$name")"

  echo "Starting $name..."
  (
    cd "$ROOT_DIR"
    mkdir -p "$artifacts_dir"
    ASPNETCORE_ENVIRONMENT=Development \
      DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH=1 \
      dotnet watch --project "$project" run --no-launch-profile --urls "$url" --artifacts-path "$artifacts_dir" \
      >>"$log_path" 2>&1
  ) &

  local pid=$!
  echo "$pid" >"$(pid_file "$name")"
  echo "$name started (pid $pid)."
}

stop_app() {
  local name="$1"
  local pid_path
  pid_path="$(pid_file "$name")"

  if ! is_running "$name"; then
    rm -f "$pid_path"
    echo "$name is not running."
    return
  fi

  local pid
  pid="$(<"$pid_path")"
  echo "Stopping $name (pid $pid)..."
  kill "$pid" 2>/dev/null || true

  local waited=0
  while kill -0 "$pid" 2>/dev/null; do
    sleep 0.25
    waited=$(( waited + 1 ))
    if (( waited > 20 )); then
      kill -9 "$pid" 2>/dev/null || true
      break
    fi
  done

  rm -f "$pid_path"
}

open_tabs() {
  open "$API_BROWSER_URL"
  open "$OWNER_URL"
  open "$EMPLOYEE_URL"
}

show_status() {
  local name url
  for name url in \
    "$API_NAME" "$API_URL" \
    "$OWNER_NAME" "$OWNER_URL" \
    "$EMPLOYEE_NAME" "$EMPLOYEE_URL"; do
    if is_running "$name"; then
      echo "$name: running (pid $(<"$(pid_file "$name")"))"
    else
      echo "$name: stopped"
    fi
    echo "  url:  $url"
    echo "  log:  $(log_file "$name")"
  done
}

tail_logs() {
  local name="${1:-}"
  case "$name" in
    "$API_NAME"|"$OWNER_NAME"|"$EMPLOYEE_NAME")
      tail -f "$(log_file "$name")"
      ;;
    *)
      echo "Unknown app '$name'. Use api, owner, or employee."
      exit 1
      ;;
  esac
}

command="${1:-}"
shift || true

case "$command" in
  up)
    start_app "$API_NAME" "$API_URL" "Workit.Api/Workit.Api.csproj"
    start_app "$OWNER_NAME" "$OWNER_URL" "Workit.OwnerApp/Workit.OwnerApp.csproj"
    start_app "$EMPLOYEE_NAME" "$EMPLOYEE_URL" "Workit.EmployeeApp/Workit.EmployeeApp.csproj"
    show_status

    if [[ "${1:-}" == "--open" ]]; then
      open_tabs
    fi
    ;;
  down)
    stop_app "$EMPLOYEE_NAME"
    stop_app "$OWNER_NAME"
    stop_app "$API_NAME"
    ;;
  restart)
    "$0" down
    "$0" up "${1:-}"
    ;;
  status)
    show_status
    ;;
  logs)
    tail_logs "${1:-}"
    ;;
  *)
    usage
    exit 1
    ;;
esac
