#!/usr/bin/env bash
set -euo pipefail

docker compose up -d --wait postgres
.tools/dotnet-ef database update --project backend/Oa.Api/Oa.Api.csproj --startup-project backend/Oa.Api/Oa.Api.csproj
