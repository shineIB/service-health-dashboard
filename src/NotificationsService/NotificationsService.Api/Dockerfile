# Build context is the repo root (see docker-compose.yml) so this can restore
# the whole NotificationsService.Api -> Domain/Infrastructure project-reference graph.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY src/NotificationsService/NotificationsService.Api/NotificationsService.Api.csproj src/NotificationsService/NotificationsService.Api/
COPY src/NotificationsService/NotificationsService.Domain/NotificationsService.Domain.csproj src/NotificationsService/NotificationsService.Domain/
COPY src/NotificationsService/NotificationsService.Infrastructure/NotificationsService.Infrastructure.csproj src/NotificationsService/NotificationsService.Infrastructure/
RUN dotnet restore src/NotificationsService/NotificationsService.Api/NotificationsService.Api.csproj

COPY src/NotificationsService/ src/NotificationsService/
RUN dotnet publish src/NotificationsService/NotificationsService.Api/NotificationsService.Api.csproj \
    -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Passed by docker-compose / CI (`docker build --build-arg GIT_SHA=$(git rev-parse HEAD) ...`).
# Not build-cache keys on purpose: they change every commit, and busting the
# build-layer cache for that would slow down every rebuild for no benefit.
ARG GIT_SHA=unknown
ARG BUILD_TIME=unknown

ENV Build__GitSha=$GIT_SHA \
    Build__BuildTimeUtc=$BUILD_TIME \
    ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app .

USER $APP_UID
EXPOSE 8080

ENTRYPOINT ["dotnet", "NotificationsService.Api.dll"]
