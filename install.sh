#!/bin/bash
set -e 

# --- Configuration ---
GITHUB_REPO="kusl/MyImapDownloader"
TOOLS=("MyEmailSearch" "MyImapDownloader")
INSTALL_BASE="/opt"

# --- Helper Functions ---
function check_deps() {
    echo "--> Checking for dependencies..."
    local deps=("curl" "jq" "unzip")
    for dep in "${deps[@]}"; do
        if ! command -v "$dep" &> /dev/null; then
            echo "Error: $dep is required. Install it with your package manager."
            exit 1
        fi
    done
}

function get_installed_version() {
    local TOOL_DIR=$1
    if [ -f "$TOOL_DIR/.version" ]; then
        cat "$TOOL_DIR/.version"
    else
        echo "0.0.0"
    fi
}

function install_tool() {
    local TOOL_NAME=$1
    local RELEASE_JSON=$2
    local LATEST_VERSION=$3
    
    local INSTALL_DIR="$INSTALL_BASE/${TOOL_NAME,,}"
    local SYMLINK_NAME="${TOOL_NAME,,}"
    local CONFIG_FILE="appsettings.json"
    local CURRENT_VERSION=$(get_installed_version "$INSTALL_DIR")

    echo "=========================================="
    echo "Processing: $TOOL_NAME"
    echo "Installed:  $CURRENT_VERSION"
    echo "Latest:     $LATEST_VERSION"
    echo "=========================================="

    if [ "$CURRENT_VERSION" == "$LATEST_VERSION" ]; then
        echo "✅ $TOOL_NAME is already up to date."
        return 0
    fi

    # 1. Identify URL (Fixed logic for versioned filenames)
    local DOWNLOAD_URL=$(echo "$RELEASE_JSON" | jq -r ".assets[] | select(.name | contains(\"$TOOL_NAME\") and contains(\"linux-x64\") and contains(\".zip\")) | .browser_download_url")

    if [ -z "$DOWNLOAD_URL" ] || [ "$DOWNLOAD_URL" == "null" ]; then
        echo "❌ Error: Could not find a linux-x64 asset for $TOOL_NAME in this release."
        return 1
    fi

    echo "--> Found asset: $DOWNLOAD_URL"

    # 2. Setup Temp
    local TEMP_DIR=$(mktemp -d)
    local ZIP_FILE="$TEMP_DIR/download.zip"

    # 3. Download & Extract
    echo "--> Downloading..."
    curl -L -o "$ZIP_FILE" "$DOWNLOAD_URL" --progress-bar
    unzip -q "$ZIP_FILE" -d "$TEMP_DIR/extract"

    # 4. Handle Config Preservation
    if [ ! -d "$INSTALL_DIR" ]; then sudo mkdir -p "$INSTALL_DIR"; fi
    if [ -f "$INSTALL_DIR/$CONFIG_FILE" ]; then
        echo "--> Backing up existing config..."
        cp "$INSTALL_DIR/$CONFIG_FILE" "$TEMP_DIR/appsettings.backup"
    fi

    # 5. Install Binary
    echo "--> Deploying binary..."
    local BINARY_SOURCE=$(find "$TEMP_DIR/extract" -type f -name "$TOOL_NAME" | head -n 1)
    
    if [ -z "$BINARY_SOURCE" ]; then
        echo "❌ Error: Binary $TOOL_NAME not found in zip."
        return 1
    fi

    # Remove old binary specifically to avoid 'text file busy' errors
    if [ -f "$INSTALL_DIR/$TOOL_NAME" ]; then
        sudo rm "$INSTALL_DIR/$TOOL_NAME"
    fi

    sudo cp "$BINARY_SOURCE" "$INSTALL_DIR/$TOOL_NAME"
    sudo chmod +x "$INSTALL_DIR/$TOOL_NAME"

    # Restore config or copy new default
    if [ -f "$TEMP_DIR/appsettings.backup" ]; then
        sudo cp "$TEMP_DIR/appsettings.backup" "$INSTALL_DIR/$CONFIG_FILE"
    else
        local NEW_CONFIG=$(find "$TEMP_DIR/extract" -type f -name "$CONFIG_FILE" | head -n 1)
        [ -f "$NEW_CONFIG" ] && sudo cp "$NEW_CONFIG" "$INSTALL_DIR/$CONFIG_FILE"
    fi
    [ -f "$INSTALL_DIR/$CONFIG_FILE" ] && sudo chmod 644 "$INSTALL_DIR/$CONFIG_FILE"

    # 6. Finalize: Create Wrapper Script (The Fix)
    echo "--> Creating wrapper script in /usr/local/bin/$SYMLINK_NAME"
    
    # We use a temp file to build the wrapper content, then sudo move it
    local WRAPPER_TMP="$TEMP_DIR/wrapper.sh"
    
    cat <<EOF > "$WRAPPER_TMP"
#!/bin/bash
# Wrapper for $TOOL_NAME to ensure native dependencies load correctly
export DOTNET_BUNDLE_EXTRACT_BASE_DIR="\${HOME}/.cache/${TOOL_NAME,,}_bundle"
exec "$INSTALL_DIR/$TOOL_NAME" "\$@"
EOF

    # Install the wrapper
    sudo mv "$WRAPPER_TMP" "/usr/local/bin/$SYMLINK_NAME"
    sudo chmod +x "/usr/local/bin/$SYMLINK_NAME"
    
    # Save version
    echo "$LATEST_VERSION" | sudo tee "$INSTALL_DIR/.version" > /dev/null

    rm -rf "$TEMP_DIR"
    echo "✅ Successfully updated $TOOL_NAME"
}

# --- Main ---
check_deps

echo "--> Fetching release info for $GITHUB_REPO..."
RELEASE_JSON=$(curl -s "https://api.github.com/repos/$GITHUB_REPO/releases/latest")
LATEST_VERSION=$(echo "$RELEASE_JSON" | jq -r .tag_name | sed 's/rolling-build-//')

if [ -z "$LATEST_VERSION" ] || [ "$LATEST_VERSION" == "null" ]; then
    echo "❌ Error: Could not determine latest version."
    exit 1
fi

for tool in "${TOOLS[@]}"; do
    install_tool "$tool" "$RELEASE_JSON" "$LATEST_VERSION"
done

echo "**************************************************"
echo "  Update Process Finished."
echo "**************************************************"
