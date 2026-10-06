---
title: Install
---

#  Install Centurion

Centurion ships **native, modern installers** for the mainstream three
platforms — the same style you expect from Python or Docker Desktop — plus
plain archives and an official Docker image. Everything is published as a
pre-release tagged `build-N`; every asset has a `<asset>.sha256` checksum.

##  Release Assets

| Platform | Asset | Install method |
|---|---|---|
| Windows x64 | `centurion-setup-win-x64.exe` | Wizard installer (recommended) |
| Windows x64 | `Centurion-win-x64.zip` | Portable / manual |
| Linux x64 | `centurion-linux-amd64.deb` | `dpkg -i` |
| Linux x64 | `centurion-linux-x86_64.rpm` | `rpm -Uvh` |
| Linux x64 | `Centurion-linux-x64.zip` / `Centurion-linux-x64.tar.gz` | Manual |
| macOS arm64 (Apple Silicon) | `centurion-osx-arm64.pkg` | Installer package (recommended) |
| macOS arm64 | `Centurion-osx-arm64.zip` | Manual |
| macOS x64 (Intel) | `Centurion-osx-x64.zip` | Manual (cross-built, best-effort) |
| Any | `ghcr.io/<owner>/centurion:latest` | Docker |

The installed command is **`centurion`** everywhere.

##  Windows

Download `centurion-setup-win-x64.exe` from the latest release and run it.

- Modern wizard, **per-user install by default**; choose "install for all
  users" when elevated
- Optional **Add Centurion to PATH** (checked by default) — new shells pick it
  up immediately
- Start Menu shortcut, optional desktop icon, proper uninstaller

Uninstall: Settings → Apps → Centurion, or run the uninstaller from Start Menu.

Portable users: unzip `Centurion-win-x64.zip`, run `Centurion.exe`, or add the
folder to PATH yourself.

##  macOS

**Apple Silicon (arm64):** download `centurion-osx-arm64.pkg` and open it.
Installer puts the app in `/usr/local/centurion` and symlinks
`/usr/local/bin/centurion` so the command is immediately on your PATH.

**Intel (x64):** use `Centurion-osx-x64.zip` (cross-built on Linux; not
notarized). Unzip, then either:

```bash
mkdir -p /usr/local/centurion && cp -R Centurion-osx-x64/* /usr/local/centurion/
ln -sf /usr/local/centurion/Centurion /usr/local/bin/centurion
```

Note: macOS packages are unsigned/not notarized for now — right-click →
Open the first time, or run `xattr -dr com.apple.quarantine /usr/local/centurion`.

##  Linux

**Debian / Ubuntu:**

```bash
sudo dpkg -i centurion-linux-amd64.deb     # installs to /opt/centurion, symlink /usr/bin/centurion
sudo apt-get install -f                     # pull missing deps if any
```

**RHEL / Fedora / openSUSE:**

```bash
sudo rpm -Uvh centurion-linux-x86_64.rpm
```

**Manual / other distros:**

```bash
tar -xzf Centurion-linux-x64.tar.gz -C /opt/centurion --strip-components=1
ln -sf /opt/centurion/Centurion /usr/local/bin/centurion
```

Uninstall (deb): `sudo dpkg -r centurion` — (rpm): `sudo rpm -e centurion`.

##  Docker

Official multi-arch image (`linux/amd64` + `linux/arm64`) published on GHCR.
The image bundles `ffmpeg`, `mkvtoolnix` and `aria2`; models and other tools
download on first use into `/var/cache/centurion` (mount a volume to keep them).

```bash
# Transcribe a local file (media mounted at /data)
docker run --rm -v "$PWD:/data" \
  ghcr.io/<owner>/centurion:latest asr demo.mp4 --language en

# Interactive shell with a persistent model cache
docker run --rm -it -v "$PWD:/data" -v centurion-cache:/var/cache/centurion \
  ghcr.io/<owner>/centurion:latest sh

# Configuration via env vars instead of a file
docker run --rm -v "$PWD:/data" -e CENTURION_PROFILE=quality \
  ghcr.io/<owner>/centurion:latest build demo.centurion.json
```

Notes:

- Container runs as non-root user `centurion`; `/data` is the working
  directory and mount point for media, config (`centurion.config.json`) and
  output
- `serve` mode works out of the box — publish port 8024 (or whatever
  `Centurion serve` binds) with `-p`
- Build locally: `docker build --build-arg CENTURION_BUILD_NUMBER=0 -t centurion .`

##  Verifying Downloads

```bash
sha256sum -c Centurion-win-x64.zip.sha256     # Linux/macOS
Get-FileHash Centurion-win-x64.zip -Algorithm SHA256   # PowerShell
```

##  Updating

Installed releases self-update from GitHub:

```bash
centurion update
```

Docker users just pull the new tag. The `build-N` tag is the build number
(= git commit count), identical to the number shown in the startup banner.
