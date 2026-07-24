#!/bin/sh
set -eu

download_page=${FIVEM_DOWNLOAD_PAGE:-https://docs.fivem.net/docs/server-download/}
dockerfile=${DOCKERFILE:-Dockerfile}
tmpdir=$(mktemp -d)
trap 'rm -rf "$tmpdir"' EXIT HUP INT TERM

curl -fsSL --retry 3 --retry-delay 2 "$download_page" -o "$tmpdir/page.html"

sed -n 's#.*<script id="__NEXT_DATA__" type="application/json">\(.*\)</script>.*#\1#p' \
  "$tmpdir/page.html" > "$tmpdir/next-data.json"

if [ ! -s "$tmpdir/next-data.json" ]; then
  echo "Could not find the FiveM download metadata (__NEXT_DATA__)." >&2
  exit 1
fi

artifact=$(jq -er '
  [.. | objects
    | select(.displayName? == "cfx-server_linux_x64.tar.xz")
    | select((.subtitle? // "") | test("^build [0-9]+$"))]
  | first
  | [(.subtitle | sub("^build "; "")), .downloadURL]
  | @tsv
' "$tmpdir/next-data.json")

build=${artifact%%	*}
url=${artifact#*	}

case "$url" in
  https://downloads.cfx-services.net/prod/*/cfx-server_linux_x64.tar.xz) ;;
  *) echo "Refusing unexpected artifact URL: $url" >&2; exit 1 ;;
esac

curl -fsSL --retry 3 --retry-delay 2 "$url" -o "$tmpdir/cfx-server.tar.xz"
sha256=$(sha256sum "$tmpdir/cfx-server.tar.xz" | awk '{print $1}')
xz -t "$tmpdir/cfx-server.tar.xz"

old_build=$(sed -n 's/^ARG FIVEM_NUM=//p' "$dockerfile")
changed=false
if [ "$old_build" != "$build" ] \
  || ! grep -Fqx "ARG FIVEM_URL=$url" "$dockerfile" \
  || ! grep -Fqx "ARG FIVEM_SHA256=$sha256" "$dockerfile"; then
  sed -i \
    -e "s|^ARG FIVEM_NUM=.*|ARG FIVEM_NUM=$build|" \
    -e "s|^ARG FIVEM_URL=.*|ARG FIVEM_URL=$url|" \
    -e "s|^ARG FIVEM_SHA256=.*|ARG FIVEM_SHA256=$sha256|" \
    "$dockerfile"
  changed=true
fi

echo "Enhanced Linux build $build ($sha256)"
if [ -n "${GITHUB_OUTPUT:-}" ]; then
  {
    echo "build=$build"
    echo "url=$url"
    echo "sha256=$sha256"
    echo "changed=$changed"
  } >> "$GITHUB_OUTPUT"
fi
