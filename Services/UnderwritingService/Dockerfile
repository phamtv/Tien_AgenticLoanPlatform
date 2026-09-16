# syntax=docker/dockerfile:1
# Build context is the repo root (not this folder) — this service needs
# Contracts/ and Common/ as sibling projects to resolve its ProjectReferences.
# Build with: docker build -f Services/UnderwritingService/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

# Declares the build arg so docker-compose.yml's "args: USE_SQL_SERVER"
# actually reaches the dotnet commands below — a real bug caught and
# fixed here: this ARG was originally missing entirely, meaning
# docker-compose's build arg went nowhere and every container silently
# built with the default (in-memory) code path regardless of what
# compose specified. Caught by actually running the containers and
# noticing SQL Server tables stayed empty / ConcurrentDictionary behavior
# persisted, not by inspection alone.
ARG USE_SQL_SERVER=false
# Same wiring as USE_SQL_SERVER above, for the TraceLogging MSBuild
# property (see UnderwritingService.csproj / Common/Logging/TraceLogger.cs).
ARG TRACE_LOGGING=true

WORKDIR /src

# BUILD SPEED FIX: copy only the .csproj files first and restore against
# just those, before copying any actual source code. Docker only re-runs a
# layer when the files it depends on change, so a code-only change (no new
# package references) now skips `dotnet restore` entirely instead of
# re-running it on every single build. Combined with the cache mount below
# (which persists downloaded packages across builds even when this layer
# DOES need to re-run, e.g. a real package change), a NuGet hiccup like the
# `api.nuget.org` connectivity failure this was added after now costs at
# most once, not every rebuild.
COPY Contracts/LoanPlatform.Contracts.csproj Contracts/
COPY Common/LoanPlatform.Common.csproj Common/
COPY Services/UnderwritingService/UnderwritingService.csproj Services/UnderwritingService/

WORKDIR /src/Services/UnderwritingService
# id=nuget-underwriting: see OriginationService/Dockerfile for why each
# service needs its own cache-mount id (parallel `docker compose up
# --build` was corrupting a shared, unkeyed NuGet cache across services).
RUN --mount=type=cache,id=nuget-underwriting,target=/root/.nuget/packages \
    dotnet restore -p:UseSqlServer=$USE_SQL_SERVER -p:TraceLogging=$TRACE_LOGGING

WORKDIR /src
COPY Contracts/ Contracts/
COPY Common/ Common/
COPY Services/UnderwritingService/ Services/UnderwritingService/

WORKDIR /src/Services/UnderwritingService
# --no-restore here is intentional and safe: the .dockerignore at the repo
# root excludes bin/ and obj/ from the build context, so this second COPY
# (source only, no local build artifacts) can't clobber the obj/
# project.assets.json the restore step above already wrote inside the
# image. Keeping --no-restore means publish never touches the network on
# its own, which is the actual point of this whole change.
RUN --mount=type=cache,id=nuget-underwriting,target=/root/.nuget/packages \
    dotnet publish -c Release -o /app --no-restore -p:UseSqlServer=$USE_SQL_SERVER -p:TraceLogging=$TRACE_LOGGING

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "UnderwritingService.dll"]
