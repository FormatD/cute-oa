#!/usr/bin/env bash
set -euo pipefail

dotnet restore backend/Oa.Api/Oa.Api.csproj
dotnet restore tools/Oa.HrProfileImport/Oa.HrProfileImport.csproj
dotnet restore tests/Oa.Domain.Tests/Oa.Domain.Tests.csproj
dotnet build backend/Oa.Api/Oa.Api.csproj --no-restore
dotnet build tools/Oa.HrProfileImport/Oa.HrProfileImport.csproj --no-restore
dotnet run --project tools/Oa.HrProfileImport/Oa.HrProfileImport.csproj --no-build -- --help >/dev/null
dotnet build tests/Oa.Domain.Tests/Oa.Domain.Tests.csproj --no-restore
dotnet run --project tests/Oa.Domain.Tests/Oa.Domain.Tests.csproj --no-build
npm run build --prefix frontend
docker compose config >/dev/null
bash scripts/verify-production-compose.sh

echo "Verification passed."
