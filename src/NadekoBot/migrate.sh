#!/bin/bash

# Check if migration name is provided
if [ -z "$1" ]; then
    echo "Error: Migration name must be specified."
    echo "Usage: $0 <MigrationName>"
    exit 1
fi

MIGRATION_NAME=$1

# Step 1: Create initial migration
echo "Creating new migration..."

dotnet build

# Getting previous migration names in order to generate SQL scripts
dotnet ef migrations add "${MIGRATION_NAME}" --context SqliteContext --output-dir "Migrations/Sqlite" --no-build
dotnet ef migrations add "${MIGRATION_NAME}" --context PostgresqlContext --output-dir "Migrations/PostgreSql" --no-build

dotnet build

# Check for migration creation success
if [ $? -ne 0 ]; then
    echo "Error: Failed to create migrations"
    exit 1
fi

# Step 2: Generate SQL scripts
echo "Generating diff SQL scripts..."

NEW_MIGRATION_ID_SQLITE=$(dotnet ef migrations list --context SqliteContext --no-build --no-connect | tail -2 | head -1 | cut -d' ' -f1)
NEW_MIGRATION_ID_POSTGRESQL=$(dotnet ef migrations list --context PostgresqlContext --no-build --no-connect | tail -2 | head -1 | cut -d' ' -f1)

dotnet ef migrations script init $MIGRATION_NAME --context SqliteContext -o "Migrations/Sqlite/${NEW_MIGRATION_ID_SQLITE}.sql" --no-build
dotnet ef migrations script init $MIGRATION_NAME --context PostgresqlContext -o "Migrations/Postgresql/${NEW_MIGRATION_ID_POSTGRESQL}.sql" --no-build


if [ $? -ne 0 ]; then
    echo "Error: Failed to generate SQL script"
    exit 1
fi

echo "Cleaning up all migration files..."

# Step 3: Clean up migration files by removing everything
for file in "Migrations/Sqlite/"*.cs; do
    echo "Deleting: $(basename "$file")"
    rm -- "$file"
done

for file in "Migrations/Postgresql/"*.cs; do
    echo "Deleting: $(basename "$file")"
    rm -- "$file"
done

# Step 4: Adding new initial migration
echo "Creating new initial migration..."

dotnet build
dotnet ef migrations add init --context SqliteContext --output-dir "Migrations/Sqlite" --no-build
dotnet ef migrations add init --context PostgresqlContext --output-dir "Migrations/PostgreSql" --no-build