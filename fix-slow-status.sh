#!/bin/bash
set -e

# Define variables
PROJECT_DIR="MyEmailSearch/Data"
FILE_NAME="$PROJECT_DIR/SearchDatabase.cs"

echo "Applying fix to $FILE_NAME..."

# Verify file exists
if [ ! -f "$FILE_NAME" ]; then
    echo "Error: $FILE_NAME not found!"
    exit 1
fi

# Create a backup
cp "$FILE_NAME" "$FILE_NAME.bak"

# Use sed to replace the slow integrity check with a fast connection check.
# 1. Replace the SQL command
sed -i 's/cmd.CommandText = "PRAGMA integrity_check;";/cmd.CommandText = "SELECT 1;"; \/\/ Optimized: fast connectivity check only/' "$FILE_NAME"

# 2. Replace the validation logic (PRAGMA returns "ok", SELECT 1 returns 1)
sed -i 's/return result?.ToString() == "ok";/return Convert.ToInt32(result) == 1;/' "$FILE_NAME"

echo "Patch applied successfully."

# Optional: verify the change
grep -A 5 "IsHealthyAsync" "$FILE_NAME"

echo "--------------------------------------------------------"
echo "Fix Complete. Rebuild with: dotnet build -c Release"
