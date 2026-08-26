#!/usr/bin/env python3
import hashlib
import json
import pathlib
import re
import sys
import xml.etree.ElementTree as ET

ROOT = pathlib.Path(__file__).resolve().parents[1]
INVENTORY = ROOT / "src" / "Jundiai.Api" / "supply-chain.inventory.json"
CSPROJ = ROOT / "src" / "Jundiai.Api" / "Jundiai.Api.csproj"
PACKAGE_JSON = ROOT / "package.json"
DOCKERFILE = ROOT / "Dockerfile"
COMPOSE = ROOT / "compose.yaml"
PACKAGE_LOCK = ROOT / "package-lock.json"


def fail(message: str) -> None:
    print(f"dependency-inventory ERROR: {message}", file=sys.stderr)
    raise SystemExit(1)


def canonical_sha(payload) -> str:
    encoded = json.dumps(payload, ensure_ascii=False, sort_keys=True, separators=(",", ":")).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


inventory = json.loads(INVENTORY.read_text(encoding="utf-8"))
if inventory.get("formalSbom") is not False:
    fail("formalSbom must remain false until a formal SBOM process exists")

# .NET declarations
root = ET.fromstring(CSPROJ.read_text(encoding="utf-8"))
target_framework = root.findtext(".//TargetFramework")
actual_dotnet = sorted(
    (node.attrib.get("Include"), node.attrib.get("Version"))
    for node in root.findall(".//PackageReference")
)
expected_dotnet = sorted(
    (item["name"], item["version"])
    for item in inventory["dotnet"]["packages"]
)
if target_framework != inventory["dotnet"]["targetFramework"]:
    fail(f"TargetFramework drift: {target_framework!r}")
if actual_dotnet != expected_dotnet:
    fail(f"NuGet direct dependency drift: actual={actual_dotnet!r} expected={expected_dotnet!r}")

# npm declarations
package = json.loads(PACKAGE_JSON.read_text(encoding="utf-8"))
actual_npm = sorted((name, version) for name, version in package.get("devDependencies", {}).items())
expected_npm = sorted((item["name"], item["version"]) for item in inventory["npm"]["devDependencies"])
if package.get("name") != inventory["npm"]["packageName"]:
    fail("npm packageName drift")
if actual_npm != expected_npm:
    fail(f"npm direct dependency drift: actual={actual_npm!r} expected={expected_npm!r}")
actual_lock_state = "present" if PACKAGE_LOCK.exists() else "absent"
if actual_lock_state != inventory["npm"]["lockfile"]:
    fail(f"npm lockfile state drift: actual={actual_lock_state} expected={inventory['npm']['lockfile']}")

# Container image declarations
from_images = re.findall(r"^FROM\s+([^\s]+)", DOCKERFILE.read_text(encoding="utf-8"), flags=re.MULTILINE | re.IGNORECASE)
compose_images = re.findall(r"^\s*image:\s*([^\s#]+)", COMPOSE.read_text(encoding="utf-8"), flags=re.MULTILINE | re.IGNORECASE)
actual_images = sorted(("Dockerfile", image) for image in from_images) + sorted(("compose.yaml", image) for image in compose_images)
expected_images = sorted((item["source"], item["image"]) for item in inventory["containers"])
if sorted(actual_images) != expected_images:
    fail(f"container image drift: actual={sorted(actual_images)!r} expected={expected_images!r}")

sha = canonical_sha(inventory)
print(f"dependency-inventory OK sha256={sha}")
print(f"dotnet-direct={len(actual_dotnet)} npm-direct={len(actual_npm)} container-images={len(actual_images)} lockfile={actual_lock_state}")
