#!/usr/bin/env python
import xml.etree.ElementTree as ET
from datetime import datetime
from pathlib import Path
from hashlib import md5
import json
import re
import subprocess
import shutil

tree = ET.parse("JellyfinJav/JellyfinJav.csproj")
version = tree.find("./PropertyGroup/AssemblyVersion").text
targetAbi = tree.find("./ItemGroup/*[@Include='Jellyfin.Controller']").attrib["Version"]
targetAbi = re.sub(r"-\w+", "", targetAbi)  # Remove trailing release candidate suffix.
timestamp = datetime.now().strftime("%Y-%m-%dT%H:%M:%SZ")

# DLL prefixes that Jellyfin ships itself — don't bundle these.
JELLYFIN_PROVIDED = (
    "Emby.",
    "Jellyfin.",
    "MediaBrowser.",
    "Microsoft.AspNetCore.",
    "Microsoft.Extensions.",
    "Microsoft.Win32.",
)

publish_dir = Path(f"release/{version}")
publish_dir.mkdir(parents=True, exist_ok=True)

print(json.dumps({
    "category": "Metadata",
    "guid": "1d5fffc2-1028-4553-9660-bd4966899e44",
    "name": "JellyfinJav",
    "description": "JAV metadata providers for Jellyfin.",
    "owner": "saiklo (fork of kyuhaku)",
    "overview": "JAV metadata providers for Jellyfin.",
    "targetAbi": f"{targetAbi}.0",
    "timestamp": timestamp,
    "version": version,
}, indent=4), file=open(publish_dir / "meta.json", "w"))

# Publish so all transitive dependency DLLs are resolved.
subprocess.run([
    "dotnet", "publish",
    "JellyfinJav/JellyfinJav.csproj",
    "--configuration", "Release",
    "--output", str(publish_dir),
], check=True)

# Remove DLLs that Jellyfin already provides to keep the zip small.
for dll in list(publish_dir.glob("*.dll")):
    if any(dll.name.startswith(prefix) for prefix in JELLYFIN_PROVIDED):
        dll.unlink()

# Remove other non-DLL publish artefacts Jellyfin doesn't need.
for ext in ("*.pdb", "*.xml", "*.json", "*.deps.json", "*.runtimeconfig.json"):
    for f in publish_dir.glob(ext):
        if f.name != "meta.json":
            f.unlink()

# Re-write meta.json (publish may have overwritten the folder).
print(json.dumps({
    "category": "Metadata",
    "guid": "1d5fffc2-1028-4553-9660-bd4966899e44",
    "name": "JellyfinJav",
    "description": "JAV metadata providers for Jellyfin.",
    "owner": "saiklo (fork of kyuhaku)",
    "overview": "JAV metadata providers for Jellyfin.",
    "targetAbi": f"{targetAbi}.0",
    "timestamp": timestamp,
    "version": version,
}, indent=4), file=open(publish_dir / "meta.json", "w"))

print("Bundled DLLs:")
for dll in sorted(publish_dir.glob("*.dll")):
    print(f"  {dll.name}")

zip_path = f"release/jellyfinjav_{version}"
shutil.make_archive(zip_path, "zip", str(publish_dir))

entry = {
    "checksum": md5(open(f"{zip_path}.zip", "rb").read()).hexdigest(),
    "changelog": "",
    "targetAbi": f"{targetAbi}.0",
    "sourceUrl": f"https://github.com/saiklo/JellyfinJAV/releases/download/{version}/jellyfinjav_{version}.zip",
    "timestamp": timestamp,
    "version": version,
}

manifest = json.loads(open("manifest.json").read())
if manifest[0]["versions"][0]["version"] == version:
    del manifest[0]["versions"][0]
manifest[0]["versions"].insert(0, entry)
print(json.dumps(manifest, indent=4), file=open("manifest.json", "w"))

print(f"\nDone. version={version}  checksum={entry['checksum']}")
