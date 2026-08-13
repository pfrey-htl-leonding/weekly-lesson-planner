#!/usr/bin/env bash
set -euo pipefail

: "${PLANNER_DB_CONNECTION:?Set PLANNER_DB_CONNECTION to a PostgreSQL connection string}"

script_directory="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
backend_directory="$(cd "${script_directory}/.." && pwd)"

cd "${backend_directory}"
dotnet tool restore
dotnet tool run dotnet-ef database update \
    --project src/WeeklyLessonPlanner.Infrastructure/WeeklyLessonPlanner.Infrastructure.csproj \
    --startup-project src/WeeklyLessonPlanner.Infrastructure/WeeklyLessonPlanner.Infrastructure.csproj

