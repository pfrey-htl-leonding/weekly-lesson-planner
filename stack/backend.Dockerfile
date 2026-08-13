FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY backend/ backend/

RUN dotnet restore backend/WeeklyLessonPlanner.sln --locked-mode \
    && dotnet publish backend/src/WeeklyLessonPlanner.Api/WeeklyLessonPlanner.Api.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "WeeklyLessonPlanner.Api.dll"]

