#!/bin/bash
set -euo pipefail

if [ $# -eq 0 ]; then
    echo "Error: Migration name must be specified."
    echo "Usage: $0 <MigrationName>"
    exit 1
fi

MIGRATION_NAME="$1"

echo "Creating new migration..."

dotnet build

dotnet ef migrations add "$MIGRATION_NAME" --output-dir "Migrations" --no-build

dotnet build

if [ $? -ne 0 ]; then
    echo "Error: Failed to create migrations"
    exit 1
fi

echo "Generating diff SQL scripts..."

NEW_MIGRATION_ID=$(dotnet ef migrations list --no-build --no-connect | tail -n 2 | head -n 1 | awk '{print $1}')

dotnet ef migrations script init "$MIGRATION_NAME" -o "Migrations/${NEW_MIGRATION_ID}.sql" --no-build

if [ $? -ne 0 ]; then
    echo "Error: Failed to generate SQL script"
    exit 1
fi

echo "Cleaning up migration files..."

find "Migrations" -name "*.cs" -type f -print -delete

dotnet build

echo "Creating new initial migration..."

dotnet ef migrations add init --output-dir "Migrations" --no-build