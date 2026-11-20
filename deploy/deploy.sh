#!/usr/bin/env bash
# Runs ON the VPS after GitHub Actions has loaded the new image (.github/workflows/deploy.yml).
# Can also be run by hand, e.g. to roll back:  IMAGE_TAG=<older commit sha> bash deploy/deploy.sh
#
#   IMAGE_TAG    required  tag of the image breakbar-logs:<tag> that must already be loaded
#   BREAKBAR_DIR optional  app directory containing deploy/ and data/ (default: the parent of this script's folder,
#                          e.g. /home/deploy/apps/breakbar-logs)
#   KEEP_IMAGES  optional  how many older images to keep for rollbacks, besides the one being started (default 3)
#   HEALTH_TIMEOUT optional seconds to wait for the health check (default 90)
set -euo pipefail

DIR="${BREAKBAR_DIR:-$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)}"
TAG="${IMAGE_TAG:?IMAGE_TAG is required (the commit sha of the image to run)}"
KEEP="${KEEP_IMAGES:-3}"
TIMEOUT="${HEALTH_TIMEOUT:-90}"
IMAGE="breakbar-logs:${TAG}"

cd "$DIR"
compose() { docker compose -f deploy/docker-compose.yml "$@"; }

# Fail early and clearly if the transfer did not deliver the image.
docker image inspect "$IMAGE" > /dev/null 2>&1 || { echo "Image $IMAGE is not loaded on this host." >&2; exit 1; }

echo "Starting $IMAGE ..."
IMAGE_TAG="$TAG" compose up -d

# Wait until the container's health check (GET /api/health) passes.
container="$(IMAGE_TAG="$TAG" compose ps -q app)"
status="starting"
for _ in $(seq 1 $((TIMEOUT / 2))); do
  status="$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' "$container")"
  [ "$status" = "healthy" ] && break
  sleep 2
done
if [ "$status" != "healthy" ]; then
  echo "Container is '$status' after ${TIMEOUT} s, deployment failed. Last log lines:" >&2
  docker logs --tail 40 "$container" >&2 || true
  echo "Roll back with: IMAGE_TAG=<previous sha> bash deploy/deploy.sh   (tags: docker images breakbar-logs)" >&2
  exit 1
fi
echo "Healthy."

# Keep the newest images for rollbacks and remove the rest. Tags are removed one by one; removing a tag of an
# image that is in use (or has other tags) only untags it, so this cannot touch the running container.
# Sorted by the image creation time: the output order of "docker images" is not reliable.
docker images breakbar-logs --format '{{.Tag}}' | grep -vx -e latest -e "$TAG" \
  | while read -r tag; do printf '%s %s\n' "$(docker image inspect -f '{{.Created}}' "breakbar-logs:${tag}")" "$tag"; done \
  | sort -r | tail -n +"$((KEEP + 1))" | cut -d' ' -f2 \
  | while read -r old; do docker rmi "breakbar-logs:${old}" > /dev/null 2>&1 || true; done
docker image prune -f > /dev/null
echo "Done: running $IMAGE"
