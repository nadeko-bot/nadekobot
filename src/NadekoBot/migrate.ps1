param(
    [Parameter(Mandatory=$true)]
    [string]$MigrationName
)

Write-Output "Creating new migration..."

dotnet build

dotnet ef migrations add $MigrationName --context SqliteContext --output-dir "Migrations" --no-build

dotnet build

if ($LASTEXITCODE -ne 0) {
    Write-Error "Error: Failed to create migrations"
    exit 1
}

Write-Output "Generating diff SQL scripts..."

$newMigrationIdSqlite = (dotnet ef migrations list --context SqliteContext --no-build --no-connect | Select-Object -Last 2 | Select-Object -First 1) -split ' ' | Select-Object -First 1

dotnet ef migrations script init $MigrationName --context SqliteContext -o "Migrations/$newMigrationIdSqlite.sql" --no-build

if ($LASTEXITCODE -ne 0) {
    Write-Error "Error: Failed to generate SQL script"
    exit 1
}

Write-Output "Cleaning up migration files..."

Get-ChildItem "Migrations" -File | Where-Object { $_.Name -like '*.cs' } | ForEach-Object {
    Write-Output "Deleting: $($_.Name)"
    Remove-Item $_.FullName -ErrorAction SilentlyContinue
}

dotnet build

Write-Output "Creating new initial migration..."
dotnet ef migrations add init --context SqliteContext --output-dir "Migrations" --no-build