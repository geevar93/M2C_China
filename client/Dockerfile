# syntax=docker/dockerfile:1
#
# Multi-stage build for the Angular frontend — mirrors klarahome-admin's Dockerfile
# pattern (build with node, serve the static output with nginx) so the whole stack
# comes up via `docker compose up`/`--profile prod up` instead of a manual
# `npm ci && npm run build` + copy-onto-the-host step.
#
# No runtime-config injection step (unlike KlaraHome's admin/storefront, which bake in
# a cross-origin API_BASE_URL): this app's ApiService calls a relative `/api/v1` path
# (client/src/app/core/services/api.service.ts), proxied by Caddy alongside the static
# files — the API's origin is never baked into the bundle, so one built image works
# unchanged in every environment.

# ---- Build stage --------------------------------------------------------------
FROM node:24-alpine AS build
WORKDIR /src

# Copy package manifests first so `npm ci` is cached across builds that only change source.
COPY package.json package-lock.json ./
RUN npm ci

COPY . .
RUN npm run build

# ---- Runtime stage -------------------------------------------------------------
FROM nginx:1.27-alpine AS runtime

COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/dist/browser /usr/share/nginx/html

EXPOSE 8080
