# InstituteHub web app image (design doc 10). Build from the repository root:
#   docker build -t institutehub .
# The image also contains the EF Core migration bundle (/app/efbundle) to update the database before a release.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached until a project file or lock file changes).
COPY global.json Directory.Build.props ./
COPY src/InstituteHub.Domain/InstituteHub.Domain.csproj src/InstituteHub.Domain/packages.lock.json src/InstituteHub.Domain/
COPY src/InstituteHub.Application/InstituteHub.Application.csproj src/InstituteHub.Application/packages.lock.json src/InstituteHub.Application/
COPY src/InstituteHub.Infrastructure/InstituteHub.Infrastructure.csproj src/InstituteHub.Infrastructure/packages.lock.json src/InstituteHub.Infrastructure/
COPY src/InstituteHub.Web/InstituteHub.Web.csproj src/InstituteHub.Web/packages.lock.json src/InstituteHub.Web/
RUN dotnet restore src/InstituteHub.Web/InstituteHub.Web.csproj --locked-mode

COPY src/ src/
RUN dotnet publish src/InstituteHub.Web/InstituteHub.Web.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# Migration bundle: a single executable that applies pending EF Core migrations.
RUN dotnet tool install --global dotnet-ef --version 10.0.12
ENV PATH="${PATH}:/root/.dotnet/tools"
RUN dotnet ef migrations bundle \
        --project src/InstituteHub.Infrastructure \
        --startup-project src/InstituteHub.Web \
        --configuration Release \
        --output /app/publish/efbundle

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# libfontconfig1: QuestPDF (receipt PDFs). tzdata: Asia/Kolkata for business dates. curl: container health check.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libfontconfig1 tzdata curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

# Uploaded files and Data Protection keys live on volumes mounted here.
RUN mkdir -p /app/data /app/keys && chown -R app:app /app/data /app/keys
USER app

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DataProtection__KeysPath=/app/keys \
    Storage__LocalRoot=/app/data
EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "InstituteHub.Web.dll"]
