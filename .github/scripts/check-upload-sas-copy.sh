#!/usr/bin/env bash
# Checks that an upload URL, the create-only SAS a module signs for a browser's upload (sp=c, docs/adr/0002), cannot be used
# to copy a blob into the container instead (#159): Azure accepts a create SAS as the destination of Put Blob From URL and
# of Copy Blob, and the storage account's allowedCopyScope 'AAD' is what refuses a source outside its Entra tenant. Run it
# against a deployed environment (README, "What to check after the first deploy").
#
# Each attempt prints the request (the URL without its signature) and Azure's answer: status and error code. An attempt
# Azure accepts is a problem; the script exits 1 if there is one, 0 if both were refused.
#
# Usage: bash .github/scripts/check-upload-sas-copy.sh <source url> <upload url> <second upload url>
#   <source url>          a blob readable from Azure that is NOT in the account's tenant, e.g. a public blob of another
#                         tenant's account, or one in a storage account of another subscription and tenant with a read SAS
#   <upload url>          an upload URL the application signed (Editorial's chapter upload), for Put Blob From URL
#   <second upload url>   a second one, for another blob name, for Copy Blob (a create SAS creates a blob once)
# Needs curl. The URLs are signed: never paste them into an issue; the script prints them without their query.
set -euo pipefail

if [ $# -ne 3 ]; then
  echo "Usage: $0 <source url> <upload url> <second upload url>" >&2
  exit 2
fi

source_url="$1"
version="2025-11-05"
problems=0

# The URL as it may be shown: scheme, host and path, never the signature.
shown() {
  printf '%s' "${1%%\?*}"
}

attempt() {
  local operation="$1" url="$2"
  shift 2
  local headers status code
  headers=$(mktemp)
  status=$(curl -sS -o /dev/null -D "$headers" -w '%{http_code}' -X PUT "$url" \
    -H "x-ms-version: $version" -H "x-ms-copy-source: $source_url" -H "Content-Length: 0" "$@")
  code=$(sed -n 's/^x-ms-error-code: *\([^[:space:]]*\).*/\1/Ip' "$headers" | head -n 1)
  rm -f "$headers"
  echo "$operation: PUT $(shown "$url") (x-ms-copy-source: $(shown "$source_url"))"
  echo "  answered $status${code:+ $code}"
  case "$status" in
    2*)
      echo "  PROBLEM: the upload URL copied a blob from outside the tenant."
      problems=$((problems + 1))
      ;;
    403)
      echo "  refused, as it should be."
      ;;
    *)
      echo "  PROBLEM: neither accepted nor refused for authorization; check the URLs and run again."
      problems=$((problems + 1))
      ;;
  esac
}

# A source nobody can read is refused for that reason alone (CannotVerifyCopySource), which would pass for the block.
source_status=$(curl -sS -o /dev/null -w '%{http_code}' -I "$source_url")
if [ "$source_status" != "200" ]; then
  echo "The source $(shown "$source_url") answered $source_status, not 200: give one Azure can read." >&2
  exit 2
fi

# Put Blob From URL: a block blob whose content Azure reads from the source, synchronously.
attempt "Put Blob From URL" "$2" -H "x-ms-blob-type: BlockBlob"
# Copy Blob: the same request without x-ms-blob-type, which Azure schedules as a copy.
attempt "Copy Blob" "$3"

if [ "$problems" -gt 0 ]; then
  echo "$problems problem(s)."
  exit 1
fi
echo "Both copies were refused."
