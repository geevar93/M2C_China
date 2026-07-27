# syntax=docker/dockerfile:1
#
# Multi-stage build for SourcingOps.Api (ACTION_PLAN E2-01, TECH_SPEC §7).
#
# NOTE on build context: the compose file passes `context: .` (repo root) with
# `dockerfile: src/SourcingOps.Api/Dockerfile`, not the `build: ./src/SourcingOps.Api`
# shorthand TECH_SPEC §7.1 shows. A multi-project solution needs the sibling
# Domain/Application/Infrastructure project folders inside the build context to restore
# and publish SourcingOps.Api — a context scoped to the Api folder alone cannot COPY
# them. Flagged in the build report as a deliberate, necessary deviation from the
# indicative compose snippet.

# ---- Build stage --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

# Copy csproj files first so `dotnet restore` is cached across builds that only change source.
COPY SourcingOps.sln .
COPY src/SourcingOps.Domain/SourcingOps.Domain.csproj src/SourcingOps.Domain/
COPY src/SourcingOps.Application/SourcingOps.Application.csproj src/SourcingOps.Application/
COPY src/SourcingOps.Infrastructure/SourcingOps.Infrastructure.csproj src/SourcingOps.Infrastructure/
COPY src/SourcingOps.Api/SourcingOps.Api.csproj src/SourcingOps.Api/
RUN dotnet restore src/SourcingOps.Api/SourcingOps.Api.csproj

COPY src/ src/
RUN dotnet publish src/SourcingOps.Api/SourcingOps.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---- Runtime stage -------------------------------------------------------------
# aspnet (not sdk) runtime-only base — no SDK/compiler layer in the final image (C1, E2-01).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app

# The aspnet Alpine image ships an unprivileged "app" user (uid/gid 64198) but runs as
# root by default — switch to it explicitly so the container never runs as root (E2-01).
RUN mkdir -p /data/uploads && chown -R app:app /data/uploads
USER app

COPY --from=build --chown=app:app /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__RootPath=/data/uploads
EXPOSE 8080

ENTRYPOINT ["dotnet", "SourcingOps.Api.dll"]
