#!/bin/bash
set -euo pipefail

# Check if migration name is provided
if [ $# -eq 0 ]; then
    echo "Error: Migration name must be specified."
    echo "Usage: $0 <MigrationName>"
    exit 1
fi

MIGRATION_NAME="$1"

echo "Creating new migration..."

# Step 1: Create initial migrations
dotnet build

dotnet ef migrations add "$MIGRATION_NAME" --context SqliteContext --output-dir "Migrations/Sqlite" --no-build
dotnet ef migrations add "$MIGRATION_NAME" --context PostgresqlContext --output-dir "Migrations/PostgreSql" --no-build

dotnet build

if [ $? -ne 0 ]; then
    echo "Error: Failed to create migrations"
    exit 1
fi

# Step 2: Generate SQL scripts
echo "Generating diff SQL scripts..."

NEW_MIGRATION_ID_SQLITE=$(dotnet ef migrations list --context SqliteContext --no-build --no-connect | tail -n 2 | head -n 1 | awk '{print $1}')
NEW_MIGRATION_ID_POSTGRESQL=$(dotnet ef migrations list --context PostgresqlContext --no-build --no-connect | tail -n 2 | head -n 1 | awk '{print $1}')

dotnet ef migrations script init "$MIGRATION_NAME" --context SqliteContext -o "Migrations/Sqlite/${NEW_MIGRATION_ID_SQLITE}.sql" --no-build
dotnet ef migrations script init "$MIGRATION_NAME" --context PostgresqlContext -o "Migrations/PostgreSql/${NEW_MIGRATION_ID_POSTGRESQL}.sql" --no-build

if [ $? -ne 0 ]; then
    echo "Error: Failed to generate SQL script"
    exit 1
fi

# Step 3: Cleanup migration files
echo "Cleaning up all migration files..."

find "Migrations/Sqlite" -name "*.cs" -type f -print -delete
find "Migrations/PostgreSql" -name "*.cs" -type f -print -delete

dotnet build

# Step 4: Create new initial migrations
echo "Creating new initial migration..."

dotnet ef migrations add init --context SqliteContext --output-dir "Migrations/Sqlite" --no-build
dotnet ef migrations add init --context PostgresqlContext --output-dir "Migrations/PostgreSql" --no-build