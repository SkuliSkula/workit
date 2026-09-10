#!/usr/bin/env bash
#
# Forced command for the GitHub Actions deploy key. The key in authorized_keys
# is pinned to this script, so a leaked CI secret cannot open a shell here.
#
# Contract:
#   SSH_ORIGINAL_COMMAND : the 40-char commit SHA to deploy
#   stdin                : a GHCR token on the first line, used to pull
#
# The token arrives on stdin rather than the command line so it never appears
# in `ps`, and is discarded with `docker logout` on exit.
set -euo pipefail

DEPLOY_DIR=/opt/workit/deploy
REGISTRY=ghcr.io
OWNER=skuliskula
SERVICES=(workit-api workit-owner)
API_CONTAINER=deploy-workit-api-1

log()  { printf '[deploy] %s\n' "$*"; }
fail() { printf '[deploy] ERROR: %s\n' "$*" >&2; exit 1; }

TAG="${SSH_ORIGINAL_COMMAND:-}"
# Anything but a bare commit SHA is rejected — this string is interpolated into
# image references, so it must not carry shell or tag metacharacters.
[[ "$TAG" =~ ^[0-9a-f]{40}$ ]] || fail "expected a 40-character commit SHA, got: '${TAG:0:60}'"

read -r GHCR_TOKEN || fail "no registry token on stdin"
[[ -n "$GHCR_TOKEN" ]] || fail "empty registry token"

cd "$DEPLOY_DIR" || fail "$DEPLOY_DIR not found"

trap 'docker logout "$REGISTRY" >/dev/null 2>&1 || true' EXIT

log "deploying $TAG"
printf '%s' "$GHCR_TOKEN" | docker login "$REGISTRY" -u "$OWNER" --password-stdin >/dev/null \
    || fail "could not authenticate to $REGISTRY"
unset GHCR_TOKEN

# Remember the outgoing tag so a bad rollout can be reversed by hand.
PREVIOUS_TAG=$(grep -E '^IMAGE_TAG=' .env 2>/dev/null | cut -d= -f2- || true)
log "previous tag: ${PREVIOUS_TAG:-<none>}"

# Pin the tag in .env so a later manual `docker compose up -d` reuses this exact
# build rather than drifting to whatever :latest points at.
if grep -qE '^IMAGE_TAG=' .env; then
    sed -i "s/^IMAGE_TAG=.*/IMAGE_TAG=$TAG/" .env
else
    printf 'IMAGE_TAG=%s\n' "$TAG" >> .env
fi

log "pulling images"
# Pull before touching anything: on failure the running containers are untouched.
docker compose pull "${SERVICES[@]}" || fail "pull failed — running containers left as they were"

log "restarting services"
# Only the two app services. The database, its volume, and the external
# DataProtection key volume are never touched.
docker compose up -d "${SERVICES[@]}" || fail "compose up failed"

# The API is not published on a host port — it is reachable only on the internal
# compose network — so health is judged by the container staying up rather than
# by an HTTP probe. A crash loop (bad migration, bad config) shows up here as a
# container that is restarting or has exited.
log "watching the api container for 20s"
for _ in $(seq 1 10); do
    sleep 2
    state=$(docker inspect -f '{{.State.Status}}' "$API_CONTAINER" 2>/dev/null || echo missing)
    restarts=$(docker inspect -f '{{.RestartCount}}' "$API_CONTAINER" 2>/dev/null || echo 0)
    [[ "$state" == "running" ]] || fail "api container is '$state' (restarts: $restarts). Previous tag: ${PREVIOUS_TAG:-<none>}"
done

if docker compose logs workit-api --since 2m 2>&1 | grep -qiE '\[FTL\]|Hosting failed to start|Unhandled exception'; then
    docker compose logs workit-api --since 2m 2>&1 | tail -20
    fail "api logged a fatal error after starting. Previous tag: ${PREVIOUS_TAG:-<none>}"
fi

log "running containers:"
docker compose ps --format 'table {{.Name}}\t{{.State}}'
log "deployed $TAG"
