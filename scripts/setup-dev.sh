#!/usr/bin/env bash
# Portable Aphelion development bootstrap for humans and any coding agent
# (Cursor, Claude Code, Codex, Copilot, Gemini CLI, etc.).
#
# Idempotent. Safe to re-run. Does not start long-running servers.
# Usage (from repo root):
#   ./scripts/setup-dev.sh
#   ./scripts/setup-dev.sh --deps-only   # skip toolchain install; only restore packages
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

DOTNET_VERSION="10.0.302"
DOTNET_DIR="${DOTNET_ROOT:-$HOME/.dotnet}"
FLUTTER_DIR="${FLUTTER_ROOT:-$HOME/flutter}"
DEPS_ONLY=0

for arg in "$@"; do
  case "$arg" in
    --deps-only) DEPS_ONLY=1 ;;
    -h|--help)
      cat <<'EOF'
Portable Aphelion development bootstrap for humans and any coding agent.

Usage (from repo root):
  ./scripts/setup-dev.sh            Install toolchains if needed, then restore packages
  ./scripts/setup-dev.sh --deps-only   Only restore NuGet / Flutter packages
EOF
      exit 0
      ;;
    *)
      echo "Unknown argument: $arg" >&2
      exit 1
      ;;
  esac
done

ensure_path() {
  export DOTNET_ROOT="$DOTNET_DIR"
  export PATH="$DOTNET_DIR:$DOTNET_DIR/tools:$FLUTTER_DIR/bin:$PATH"
}

install_dotnet() {
  if [[ -x "$DOTNET_DIR/dotnet" ]]; then
    local current
    current="$("$DOTNET_DIR/dotnet" --version 2>/dev/null || true)"
    if [[ "$current" == "$DOTNET_VERSION" || "$current" == 10.* ]]; then
      echo "dotnet already present: $current"
      return
    fi
  fi

  echo "Installing .NET SDK $DOTNET_VERSION into $DOTNET_DIR ..."
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --version "$DOTNET_VERSION" --install-dir "$DOTNET_DIR"
}

install_flutter() {
  if [[ -x "$FLUTTER_DIR/bin/flutter" ]]; then
    echo "Flutter already present at $FLUTTER_DIR"
    return
  fi

  echo "Cloning Flutter stable into $FLUTTER_DIR ..."
  git clone --depth 1 https://github.com/flutter/flutter.git -b stable "$FLUTTER_DIR"
  git config --global --add safe.directory "$FLUTTER_DIR" 2>/dev/null || true
  # First run downloads the Dart SDK bundled with Flutter.
  "$FLUTTER_DIR/bin/flutter" --version >/dev/null
}

install_linux_webview_deps() {
  [[ "$(uname -s)" == "Linux" ]] || return 0

  if ldconfig -p 2>/dev/null | grep -q 'libwebkit2gtk-4.1\.so'; then
    echo "libwebkit2gtk-4.1 already installed"
  else
    if command -v apt-get >/dev/null 2>&1; then
      echo "Installing libwebkit2gtk-4.1-0 (desktop WebView on Linux) ..."
      if command -v sudo >/dev/null 2>&1; then
        sudo DEBIAN_FRONTEND=noninteractive apt-get update -qq
        # fuse3/xdg-desktop-portal postinst can fail in containers; continue if the .so lands.
        sudo DEBIAN_FRONTEND=noninteractive apt-get install -y -qq libwebkit2gtk-4.1-0 || true
      else
        echo "sudo/apt-get unavailable; install libwebkit2gtk-4.1-0 manually for desktop WebView." >&2
      fi
    else
      echo "Non-apt Linux: install WebKitGTK 4.1 for Avalonia NativeWebView." >&2
    fi
  fi

  # Avalonia loads the unversioned soname "libwebkit2gtk".
  local libdir="/usr/lib/$(uname -m)-linux-gnu"
  [[ -d "$libdir" ]] || libdir="/usr/lib/x86_64-linux-gnu"
  if [[ -e "$libdir/libwebkit2gtk-4.1.so.0" ]]; then
    if command -v sudo >/dev/null 2>&1; then
      sudo ln -sfn "$libdir/libwebkit2gtk-4.1.so.0" "$libdir/libwebkit2gtk.so"
      sudo ln -sfn "$libdir/libwebkit2gtk-4.1.so.0" "$libdir/liblibwebkit2gtk.so"
      sudo ldconfig 2>/dev/null || true
    fi
  fi
}

persist_shell_path_hint() {
  local marker="# Aphelion toolchain PATH"
  local rc="$HOME/.bashrc"
  [[ -f "$rc" ]] || return 0
  if grep -qF "$marker" "$rc" 2>/dev/null; then
    return 0
  fi
  {
    echo ""
    echo "$marker"
    echo "export DOTNET_ROOT=\"\$HOME/.dotnet\""
    echo "export PATH=\"\$HOME/.dotnet:\$HOME/.dotnet/tools:\$HOME/flutter/bin:\$PATH\""
  } >> "$rc"
  echo "Appended PATH exports to $rc (new shells will pick them up)."
}

restore_packages() {
  ensure_path
  command -v dotnet >/dev/null || { echo "dotnet not found on PATH" >&2; exit 1; }
  command -v flutter >/dev/null || { echo "flutter not found on PATH" >&2; exit 1; }

  echo "Restoring backend ..."
  dotnet restore "$ROOT/backend/Aphelion.Backend.slnx"
  echo "Restoring desktop ..."
  dotnet restore "$ROOT/desktop/Aphelion.Desktop.slnx"
  echo "Fetching mobile packages ..."
  (
    cd "$ROOT/mobile"
    flutter pub get
  )
}

if [[ "$DEPS_ONLY" -eq 0 ]]; then
  install_dotnet
  install_flutter
  install_linux_webview_deps
  persist_shell_path_hint
fi

restore_packages

echo ""
echo "Setup complete."
echo "  dotnet:  $(ensure_path; dotnet --version)"
echo "  flutter: $(ensure_path; flutter --version 2>/dev/null | head -1)"
echo "Next: see AGENTS.md for lint / test / run commands."
