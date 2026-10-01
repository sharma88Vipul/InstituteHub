# Applies all migrations to the local docker-compose database.
dotnet ef database update `
    --project src/InstituteHub.Infrastructure `
    --startup-project src/InstituteHub.Infrastructure
