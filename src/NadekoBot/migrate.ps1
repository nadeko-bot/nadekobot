param(
    [Parameter(Mandatory=$true)]
    [string]$MigrationName
)

Write-Output "Creating new migration..."

# Step 1: Create initial migrations
dotnet build

dotnet ef migrations add $MigrationName --context SqliteContext --output-dir "Migrations/Sqlite" --no-build
dotnet ef migrations add $MigrationName --context PostgresqlContext --output-dir "Migrations/PostgreSql" --no-build

dotnet build

if ($LASTEXITCODE -ne 0) {
    Write-Error "Error: Failed to create migrations"
    exit 1
}

# Step 2: Generate SQL scripts
Write-Output "Generating diff SQL scripts..."

$newMigrationIdSqlite = (dotnet ef migrations list --context SqliteContext --no-build --no-connect | Select-Object -Last 2 | Select-Object -First 1) -split ' ' | Select-Object -First 1
$newMigrationIdPostgresql = (dotnet ef migrations list --context PostgresqlContext --no-build --no-connect | Select-Object -Last 2 | Select-Object -First 1) -split ' ' | Select-Object -First 1

dotnet ef migrations script init $MigrationName --context SqliteContext -o "Migrations/Sqlite/$newMigrationIdSqlite.sql" --no-build
dotnet ef migrations script init $MigrationName --context PostgresqlContext -o "Migrations/Postgresql/$newMigrationIdPostgresql.sql" --no-build

if ($LASTEXITCODE -ne 0) {
    Write-Error "Error: Failed to generate SQL script"
    exit 1
}

# Step 3: Cleanup migration files
Write-Output "Cleaning up all migration files..."

Get-ChildItem "Migrations/Sqlite" -File | Where-Object { $_.Name -like '*.cs' } | ForEach-Object {
    Write-Output "Deleting: $($_.Name)"
    Remove-Item $_.FullName -ErrorAction SilentlyContinue
}

Get-ChildItem "Migrations/Postgresql" -File | Where-Object { $_.Name -like '*.cs' } | ForEach-Object {
    Write-Output "Deleting: $($_.Name)"
    Remove-Item $_.FullName -ErrorAction SilentlyContinue
}

dotnet build

# Step 4: Create new initial migrations
Write-Output "Creating new initial migration..."
dotnet ef migrations add init --context SqliteContext --output-dir "Migrations/Sqlite" --no-build
dotnet ef migrations add init --context PostgresqlContext --output-dir "Migrations/PostgreSql" --no-build