#!/usr/bin/env bash
set -euo pipefail

dotnet build backend/Oa.Api/Oa.Api.csproj --no-restore
dotnet build tests/Oa.Domain.Tests/Oa.Domain.Tests.csproj --no-restore
dotnet run --project tests/Oa.Domain.Tests/Oa.Domain.Tests.csproj --no-build
npm run build --prefix frontend
docker compose config >/dev/null

echo "Verification passed."
