#!/usr/bin/env bash
set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$repository_root"

export DOTNET_CLI_HOME="${DOTNET_CLI_HOME:-${TMPDIR:-/tmp}/kurastorage-dotnet-home}"
export NUGET_PACKAGES="${NUGET_PACKAGES:-${TMPDIR:-/tmp}/kurastorage-nuget-packages}"
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

dotnet restore server/KuraStorage.sln --locked-mode --disable-parallel -m:1
dotnet format server/KuraStorage.sln --verify-no-changes --no-restore
dotnet build server/KuraStorage.sln --configuration Release --no-restore -m:1
dotnet test server/KuraStorage.sln --configuration Release --no-build -m:1

test -f server/src/KuraStorage.Api/bin/Release/net10.0/KuraStorage.Api.dll
test -f server/src/KuraStorage.AdminCli/bin/Release/net10.0/KuraStorage.AdminCli.dll

echo "Server verification passed."
