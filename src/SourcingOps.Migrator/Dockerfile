# syntax=docker/dockerfile:1
#
# Multi-stage build for SourcingOps.Migrator — a small standalone image that applies
# EF Core migrations as an explicit deploy step (never auto-applied by the api
# container in production), mirroring KlaraHome.Migrator's separate-image pattern.
#
# Build context is the repo root (same reasoning as src/SourcingOps.Api/Dockerfile):
# a multi-project solution needs the sibling Domain/Application/Infrastructure project
# folders inside the build context.

# ---- Build stage --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src

COPY SourcingOps.sln .
COPY src/SourcingOps.Domain/SourcingOps.Domain.csproj src/SourcingOps.Domain/
COPY src/SourcingOps.Application/SourcingOps.Application.csproj src/SourcingOps.Application/
COPY src/SourcingOps.Infrastructure/SourcingOps.Infrastructure.csproj src/SourcingOps.Infrastructure/
COPY src/SourcingOps.Migrator/SourcingOps.Migrator.csproj src/SourcingOps.Migrator/
RUN dotnet restore src/SourcingOps.Migrator/SourcingOps.Migrator.csproj

COPY src/ src/
RUN dotnet publish src/SourcingOps.Migrator/SourcingOps.Migrator.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---- Runtime stage -------------------------------------------------------------
# `runtime` (not `aspnet`) base — this is a console job with no HTTP surface, so it
# doesn't need the ASP.NET layer either. No SDK/compiler in the final image.
FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS runtime
WORKDIR /app

USER app

COPY --from=build --chown=app:app /app/publish .

ENV DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "SourcingOps.Migrator.dll"]
