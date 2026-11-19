# syntax=docker/dockerfile:1
# CoHost server image. Build context is the repository root:
#   docker build -f server/Dockerfile -t cohost-server:dev .
#
# Default tags follow security patches. For a reproducible release, pin patch tags with
# --build-arg (digest pinning is handled by the release CI in Phase 4).
ARG DOTNET_SDK_TAG=10.0-noble
ARG DOTNET_RUNTIME_TAG=10.0-noble-chiseled
ARG NODE_TAG=24-alpine

# ---- Stage 1: .NET build (JSON Schema generation + publish) ----
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_TAG} AS dotnet-build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY protocol/ protocol/
COPY server/CoHost.Server/ server/CoHost.Server/
RUN dotnet restore server/CoHost.Server/CoHost.Server.csproj \
 && dotnet run --project protocol/CoHost.Contracts.SchemaGen -c Release -- /out/schema \
 && dotnet publish server/CoHost.Server/CoHost.Server.csproj -c Release --no-restore -o /out/app \
 && mkdir -p /out/data

# ---- Stage 2: admin web build (TS types from the schema, Vite bundle) ----
# This stage has no dotnet, so the JSON Schema must come from the dotnet-build stage.
FROM node:${NODE_TAG} AS web-build
WORKDIR /web
COPY server/web/package.json server/web/package-lock.json server/web/.npmrc ./
RUN npm ci
COPY server/web/ ./
COPY --from=dotnet-build /out/schema/ generated/schema/
RUN npm run build

# ---- Stage 3: runtime (chiseled: no shell, no package manager, non-root) ----
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_RUNTIME_TAG} AS final
LABEL org.opencontainers.image.licenses="Apache-2.0"
WORKDIR /app
COPY --from=dotnet-build /out/app/ ./
COPY --from=web-build /web/dist/ ./wwwroot/
# Keys and certificates are generated at first run into the volume, never baked into the image.
COPY --from=dotnet-build --chown=$APP_UID:$APP_UID /out/data /data
# ASPNETCORE_HTTP_PORTS is emptied: no plaintext listener, and the base image's 8080 default
# would otherwise trigger an override warning.
ENV COHOST_DATA_DIR=/data \
    COHOST_PORT=8443 \
    ASPNETCORE_HTTP_PORTS= \
    DOTNET_EnableDiagnostics=0
EXPOSE 8443
VOLUME /data
USER $APP_UID
# chiseled images have no shell or curl, so the app probes itself.
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=3 \
  CMD ["dotnet","CoHost.Server.dll","--healthcheck"]
ENTRYPOINT ["dotnet","CoHost.Server.dll"]
