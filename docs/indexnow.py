#!/usr/bin/env python3
"""Tell the IndexNow search engines which pages of docs.nadeko.bot changed.

One request to api.indexnow.org reaches Bing, Yandex, Seznam, Naver, Yep and the other IndexNow
engines. Google does not use IndexNow. Google reads the sitemap.

The docs workflow runs this script after `mkdocs gh-deploy`:

    python indexnow.py --old OLD_SITEMAP --new site/sitemap.xml [--all]

1. The script compares the new sitemap with the sitemap from before the deploy. It sends a page
   when the <lastmod> of the page changed or when the page is new. With --all, it sends all pages.
2. It waits until docs.nadeko.bot shows the new sitemap and the key file (--wait, 600 seconds).
3. It sends the pages.

Without --old, the script uses the live sitemap as the old one. With --dry-run, it only shows
the pages. The key is the name of the one `md/<32 hex>.txt` file, and the file holds the key.
The key is public: it only shows that we control the host. Standard library only.
Exit 1 if the site does not show the new build in time, or if the engines refuse the request.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
import time
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET
from pathlib import Path

SITE = "https://docs.nadeko.bot"
HOST = "docs.nadeko.bot"
ENDPOINT = "https://api.indexnow.org/indexnow"
UA = "nadekobot-docs-indexnow (+https://github.com/nadeko-bot/nadekobot)"
NS = "{http://www.sitemaps.org/schemas/sitemap/0.9}"
HERE = Path(__file__).resolve().parent


def fetch(url: str) -> str:
    req = urllib.request.Request(f"{url}?v={int(time.time())}", headers={"user-agent": UA})
    with urllib.request.urlopen(req, timeout=30) as res:
        return res.read().decode("utf-8")


def parse(xml: str) -> dict[str, str]:
    """Each <loc> of a sitemap, with its <lastmod> (empty if there is none)."""
    root = ET.fromstring(xml)
    return {u.findtext(f"{NS}loc", "").strip(): u.findtext(f"{NS}lastmod", "").strip() for u in root.iter(f"{NS}url")}


def key() -> str:
    keys = [p.stem for p in (HERE / "md").glob("*.txt") if re.fullmatch(r"[0-9a-f]{32}", p.stem)]
    if len(keys) != 1:
        sys.exit(f"[indexnow] expected one <32 hex>.txt key file in md/, found {len(keys)}")
    if (HERE / "md" / f"{keys[0]}.txt").read_text().strip() != keys[0]:
        sys.exit(f"[indexnow] md/{keys[0]}.txt does not hold its key")
    return keys[0]


def old_sitemap(path: str | None) -> dict[str, str] | None:
    try:
        text = Path(path).read_text() if path else fetch(f"{SITE}/sitemap.xml")
        return parse(text) or None
    except (OSError, ET.ParseError, urllib.error.URLError) as e:
        print(f"[indexnow] no old sitemap ({e}); all pages count as changed")
        return None


def wait_until_live(new: dict[str, str], k: str, seconds: int) -> bool:
    deadline = time.time() + seconds
    while True:
        try:
            if parse(fetch(f"{SITE}/sitemap.xml")) == new and fetch(f"{SITE}/{k}.txt").strip() == k:
                return True
        except (ET.ParseError, urllib.error.URLError):
            pass
        if time.time() > deadline:
            return False
        time.sleep(15)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--old", help="the sitemap from before the deploy (default: the live sitemap)")
    ap.add_argument("--new", default=str(HERE / "site" / "sitemap.xml"), help="the sitemap of this build")
    ap.add_argument("--all", action="store_true", help="send all pages, not only the changed pages")
    ap.add_argument("--dry-run", action="store_true", help="show the pages; do not wait and do not send")
    ap.add_argument("--wait", type=int, default=600, help="seconds to wait for the new build to be live")
    args = ap.parse_args()

    k = key()
    new = parse(Path(args.new).read_text())
    old = None if args.all else old_sitemap(args.old)
    urls = sorted(u for u, day in new.items() if old is None or old.get(u) != day)
    for u in urls:
        if not u.startswith(f"{SITE}/"):
            sys.exit(f"[indexnow] {u} is not on {HOST}")
    print(f"[indexnow] {len(urls)} of {len(new)} pages to send:" + "".join(f"\n  {new[u]}  {u}" for u in urls))
    if not urls or args.dry_run:
        return 0

    if not wait_until_live(new, k, args.wait):
        print(f"[indexnow] {SITE} does not show the new sitemap and the key file after {args.wait} s. Nothing sent.")
        return 1
    body = json.dumps({"host": HOST, "key": k, "keyLocation": f"{SITE}/{k}.txt", "urlList": urls}).encode()
    req = urllib.request.Request(ENDPOINT, data=body, method="POST",
                                 headers={"content-type": "application/json; charset=utf-8", "user-agent": UA})
    try:
        with urllib.request.urlopen(req, timeout=30) as res:
            status = res.status
    except urllib.error.HTTPError as e:
        status = e.code
        print(f"[indexnow] {e.code} {e.read().decode(errors='replace')[:300]}")
    # 200: received. 202: received, and the engines still check the key.
    print(f"[indexnow] answer {status}")
    return 0 if status in (200, 202) else 1


if __name__ == "__main__":
    raise SystemExit(main())
