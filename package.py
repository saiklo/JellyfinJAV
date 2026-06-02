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
targetAbi = re.sub(r"-w+", "", targetAbi) # Remove trailing release candidate version.
timestamp = datetime.now().strftime("%Y-%m-%dT%H:%M:%SZ")

meta = {
    "category": "Metadata",
    "guid": "1d5fffc2-1028-4553-9660-bd4966899e44",
    "name": "JellyfinJav",
    "description": "JAV metadata providers for Jellyfin.",
    "owner": "saiklo (fork of kyuhaku)",
    "overview": "JAV metadata providers for Jellyfin.",
    "targetAbi": f"{targetAbi}.0",
    "timestamp": timestamp,
    "version": version
}

Path(f"release/{version}").mkdir(parents=True, exist_ok=True)
print(json.dumps(meta, indent=4), file=open(f"release/{version}/meta.json", "w"))

subprocess.run([
    "dotnet",
    "build",
    "JellyfinJav/JellyfinJav.csproj",
    "--configuration",
    "Release"
])

shutil.copy("JellyfinJav/bin/Release/net80/JellyfinJav.dll", f"release/{version}/")
shutil.copy(f"{Path.home()}/.nuget/packages/anglesharp/1.1.2/lib/net8.0/AngleSharp.dll", f"release/{version}/")

shutil.make_archive(f"release/jellyfinjav_{version}", "zip", f"release/{version}/")

entry = {
    "checksum": md5(open(f"release/jellyfinjav_{version}.zip", "rb").read()).hexdigest(),
    "changelog": "",
    "targetAbi": f"{targetAbi}.0",
    "sourceUrl": f"https://github.com/saiklo/JellyfinJAV/releases/download/{version}/jellyfinjav_{version}.zip",
    "timestamp": timestamp,
    "version": version
}

manifest = json.loads(open("manifest.json", "r").read())

if manifest[0]["versions"][0]["version"] == version:
    del manifest[0]["versions"][0]

manifest[0]["versions"].insert(0, entry)
print(json.dumps(manifest, indent=4), file=open("manifest.json", "w"))
