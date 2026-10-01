# Usage: ./scripts/add-migration.ps1 InitialCreate
param([Parameter(Mandatory = $true)][string]$Name)

dotnet ef migrations add $Name `
    --project src/InstituteHub.Infrastructure `
    --startup-project src/InstituteHub.Infrastructure `
    --output-dir Persistence/Migrations
