# Build context is the repo root (see docker-compose.yml) so this can restore
# the whole OrdersService.Api -> Domain/Infrastructure project-reference graph.

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY Directory.Build.props .
COPY src/OrdersService/OrdersService.Api/OrdersService.Api.csproj src/OrdersService/OrdersService.Api/
COPY src/OrdersService/OrdersService.Domain/OrdersService.Domain.csproj src/OrdersService/OrdersService.Domain/
COPY src/OrdersService/OrdersService.Infrastructure/OrdersService.Infrastructure.csproj src/OrdersService/OrdersService.Infrastructure/
RUN dotnet restore src/OrdersService/OrdersService.Api/OrdersService.Api.csproj

COPY src/OrdersService/ src/OrdersService/
RUN dotnet publish src/OrdersService/OrdersService.Api/OrdersService.Api.csproj \
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

ENTRYPOINT ["dotnet", "OrdersService.Api.dll"]
