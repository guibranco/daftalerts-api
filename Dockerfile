# syntax=docker/dockerfile:1.7

# ---- build ---------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

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
    /p:UseAppHost=false

# Publish EmailIngest as a self-contained single-file executable for linux-x64
RUN dotnet publish src/DaftAlerts.EmailIngest/DaftAlerts.EmailIngest.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained true \
    /p:PublishSingleFile=true \
    /p:InvariantGlobalization=true \
    -o /app/ingest \
    --no-restore

# ---- runtime -------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

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
