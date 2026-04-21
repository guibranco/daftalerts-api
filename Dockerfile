# syntax=docker/dockerfile:1.7

# ---- build ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Version info supplied by CI (GitVersion). Defaults keep local builds working.
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
ARG FILE_VERSION=0.0.0.0
ARG INFORMATIONAL_VERSION=0.0.0-local

# Copy just the things required for restore first for better layer caching.
COPY Directory.Build.props Directory.Packages.props ./
COPY DaftAlerts.sln ./
COPY src/ ./src/
COPY tests/ ./tests/

RUN dotnet restore DaftAlerts.sln

# Publish the Api
RUN dotnet publish src/DaftAlerts.Api/DaftAlerts.Api.csproj \
    -c Release \
    -o /app/api \
    --no-restore \
    -p:UseAppHost=false \
    -p:Version=${ASSEMBLY_VERSION} \
    -p:AssemblyVersion=${ASSEMBLY_VERSION} \
    -p:FileVersion=${FILE_VERSION} \
    -p:InformationalVersion=${INFORMATIONAL_VERSION}

# Publish EmailIngest as a self-contained single-file executable for linux-x64
RUN dotnet publish src/DaftAlerts.EmailIngest/DaftAlerts.EmailIngest.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:InvariantGlobalization=true \
    -p:Version=${ASSEMBLY_VERSION} \
    -p:AssemblyVersion=${ASSEMBLY_VERSION} \
    -p:FileVersion=${FILE_VERSION} \
    -p:InformationalVersion=${INFORMATIONAL_VERSION} \
    -o /app/ingest \
    --no-restore

# ---- runtime -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# Re-declare ARGs needed for LABEL values (ARGs don't cross stage boundaries).
ARG VERSION=0.0.0
ARG INFORMATIONAL_VERSION=0.0.0-local
ARG GIT_SHA=unknown
ARG BUILD_DATE

# OCI image labels — discoverable via `docker inspect` and GHCR UI.
LABEL org.opencontainers.image.title="DaftAlerts API" \
      org.opencontainers.image.description="Personal Daft.ie property aggregator API" \
      org.opencontainers.image.version="${VERSION}" \
      org.opencontainers.image.revision="${GIT_SHA}" \
      org.opencontainers.image.created="${BUILD_DATE}" \
      org.opencontainers.image.source="https://github.com/guibranco/daftalerts-api" \
      org.opencontainers.image.licenses="MIT"

# Surface version to the running app as an env var too (handy for /health or logs).
ENV DAFTALERTS_VERSION=${INFORMATIONAL_VERSION}

# Create a non-root user and data/log dirs.
RUN groupadd --system --gid 10001 daftalerts \
    && useradd --system --uid 10001 --gid daftalerts --home-dir /home/daftalerts --create-home daftalerts \
    && mkdir -p /var/lib/daftalerts /var/log/daftalerts \
    && chown -R daftalerts:daftalerts /var/lib/daftalerts /var/log/daftalerts

WORKDIR /app
COPY --from=build /app/api ./
# Include the EmailIngest binary in the image too (mounted out to the host for Postfix to invoke).
COPY --from=build /app/ingest /opt/daftalerts/ingest

USER daftalerts

ENV ASPNETCORE_URLS=http://+:5080
EXPOSE 5080

ENTRYPOINT ["dotnet", "DaftAlerts.Api.dll"]
