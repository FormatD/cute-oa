#!/usr/bin/env bash
set -euo pipefail

# `up -d` returns before initdb has created POSTGRES_USER on a new volume.
# Waiting for the Compose health check makes a first startup reliable while
# preserving existing development data on normal restarts.
docker compose up -d --wait postgres

# Integration tests always use an isolated database. The demo database `oa`
# is never reset by this script, so normal restarts retain user-visible data.
docker compose exec -T postgres psql -U oa -d postgres -v ON_ERROR_STOP=1 \
  -c 'DROP DATABASE IF EXISTS oa_test WITH (FORCE);' \
  -c 'CREATE DATABASE oa_test OWNER oa;'
dotnet run --project tests/Oa.Postgres.Tests/Oa.Postgres.Tests.csproj
