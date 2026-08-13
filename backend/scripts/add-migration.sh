#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 1 ]]; then
    echo "Usage: $0 <MigrationName>" >&2
    exit 2
fi

: "${PLANNER_DB_CONNECTION:?Set PLANNER_DB_CONNECTION to a PostgreSQL connection string}"

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
backend_directory="$(cd "${script_directory}/.." && pwd)"

cd "${backend_directory}"
dotnet tool restore
dotnet tool run dotnet-ef migrations add "$1" \
    --project src/WeeklyLessonPlanner.Infrastructure/WeeklyLessonPlanner.Infrastructure.csproj \
    --startup-project src/WeeklyLessonPlanner.Infrastructure/WeeklyLessonPlanner.Infrastructure.csproj \
    --output-dir Persistence/Migrations

