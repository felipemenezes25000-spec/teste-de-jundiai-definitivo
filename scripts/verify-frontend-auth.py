#!/usr/bin/env python3
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
WEB = ROOT / "src" / "Jundiai.Api" / "wwwroot"

PUBLIC_PAGES = {"login.html", "citizen.html"}
PROTECTED_PAGES = [
    "index.html", "poc.html", "verification.html", "evidence-pack.html", "dossier.html", "contingency.html",
    "command-center.html", "caretrace.html", "governance.html", "registration.html", "workforce.html",
    "referrals.html", "clinical-ops.html", "agenda.html", "telemedicine.html", "immunization-v2.html",
    "pharmacy-care.html", "diagnostics.html", "dental-v2.html", "billing-v2.html", "operations.html",
    "esus.html", "acs.html",
]

SCRIPT_RE = re.compile(r'<script[^>]+src=["\']([^"\']+)["\']', re.I)
DIRECT_API_LINK_RE = re.compile(r'(?:href|action)=["\']\s*/api/(?!health|citizen|auth)', re.I)


def load_text(path: Path) -> str:
    return path.read_text(encoding="utf-8")


def page_sources(page: str) -> tuple[str, list[str]]:
    path = WEB / page
    if not path.exists():
        raise FileNotFoundError(path)
    html = load_text(path)
    chunks = [html]
    refs: list[str] = []
    for raw in SCRIPT_RE.findall(html):
        if raw.startswith(("http://", "https://", "//")):
            continue
        rel = raw.split("?", 1)[0].lstrip("/")
        script = WEB / rel
        refs.append(rel)
        if script.exists():
            chunks.append(load_text(script))
    return "\n".join(chunks), refs


def main() -> int:
    failures: list[str] = []
    rows: list[str] = []

    for page in PROTECTED_PAGES:
        try:
            combined, refs = page_sources(page)
        except FileNotFoundError:
            failures.append(f"{page}: arquivo ausente")
            continue

        lower = combined.lower()
        uses_guard = "auth-client.js" in refs or "/auth-client.js" in lower
        bearer_aware = "jundiai.session" in lower and "authorization" in lower
        direct_api_link = bool(DIRECT_API_LINK_RE.search(combined))
        legacy_role = "x-demo-role" in lower

        # O guard compartilhado remove headers demonstrativos legados antes do request.
        safe_auth = uses_guard or bearer_aware
        if not safe_auth:
            failures.append(f"{page}: sem auth-client.js e sem Bearer baseado em jundiai.session")
        if direct_api_link:
            failures.append(f"{page}: possui href/action direto para API protegida; navegação não carrega Bearer")
        if legacy_role and not uses_guard:
            failures.append(f"{page}: contém X-Demo-Role sem auth-client.js para neutralizá-lo")

        rows.append(
            f"{page}: auth={'guard' if uses_guard else 'bearer' if bearer_aware else 'MISSING'}; "
            f"legacy-role={'neutralized' if legacy_role and uses_guard else 'present' if legacy_role else 'no'}; "
            f"scripts={len(refs)}"
        )

    for page in sorted(PUBLIC_PAGES):
        if not (WEB / page).exists():
            failures.append(f"{page}: página pública ausente")

    print("Frontend auth coverage")
    for row in rows:
        print(f"  {row}")

    if failures:
        print("\nFAILURES:")
        for failure in failures:
            print(f"  - {failure}")
        return 1

    print(f"OK: {len(PROTECTED_PAGES)} superfícies internas cobertas; {len(PUBLIC_PAGES)} públicas classificadas.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
