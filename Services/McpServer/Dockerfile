# syntax=docker/dockerfile:1
# AZURE MIGRATION: new file — the MCP server had no Dockerfile before (it
# only ran locally as a stdio subprocess). Same pattern as the other four
# services' Dockerfiles: build context is the repo root, not this folder,
# because McpServer.csproj references Contracts/ (transitively) and needs
# it as a build-time sibling.
#
# Build with: docker build -f Services/McpServer/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# BUILD SPEED FIX: copy only the .csproj files first and restore against
# just those, before copying any actual source code, so a code-only change
# (no new package references) skips `dotnet restore` entirely instead of
# re-running it on every build. Combined with the cache mount below (which
# persists downloaded packages across builds even when this layer DOES
# need to re-run), a NuGet hiccup like the `api.nuget.org` connectivity
# failure this was added after now costs at most once, not every rebuild.
COPY Contracts/LoanPlatform.Contracts.csproj Contracts/
COPY Common/LoanPlatform.Common.csproj Common/
COPY Services/McpServer/McpServer.csproj Services/McpServer/

WORKDIR /src/Services/McpServer
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet restore

WORKDIR /src
COPY Contracts/ Contracts/
COPY Common/ Common/
COPY Services/McpServer/ Services/McpServer/

WORKDIR /src/Services/McpServer
# --no-restore here is intentional and safe: the .dockerignore at the repo
# root excludes bin/ and obj/ from the build context, so this second COPY
# (source only, no local build artifacts) can't clobber the obj/
# project.assets.json the restore step above already wrote inside the
# image. Keeping --no-restore means publish never touches the network on
# its own, which is the actual point of this whole change.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .

# Same convention as the other services — Container Apps sets this via the
# container's ingress target port (see infra/main.bicep); Kestrel just
# needs to be listening on it.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "McpServer.dll"]
