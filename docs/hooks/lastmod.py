"""Sitemap dates for the docs.

MkDocs gives each page the build date as its <lastmod>, so each deploy told search engines that
all pages changed. This hook gives each page the date of the last Git commit that changed its
Markdown file or a snippet that the page includes. A file that Git does not know keeps the
build date.

The hook needs the full Git history. In GitHub Actions, set `fetch-depth: 0` on the checkout.
In a shallow clone, the hook logs a warning, and `mkdocs build --strict` stops.
"""

import logging
import re
import subprocess
from pathlib import Path

log = logging.getLogger("mkdocs.hooks.lastmod")

# A pymdownx.snippets line: --8<-- "md/snippets/file.md:section"
SNIPPET = re.compile(r'^\s*--8<--\s+"([^"]+)"', re.M)

_base = Path(".")


def _git(*args: str) -> str:
    out = subprocess.run(["git", *args], cwd=_base, capture_output=True, text=True)
    return out.stdout.strip() if out.returncode == 0 else ""


def on_config(config):
    global _base
    _base = Path(config.config_file_path).parent
    if _git("rev-parse", "--is-shallow-repository") == "true":
        log.warning("lastmod: this is a shallow clone, so the sitemap dates are wrong. Fetch the full history.")
    return config


def on_page_markdown(markdown, page, config, files):
    paths = [Path(page.file.abs_src_path)]
    paths += [_base / s.split(":")[0] for s in SNIPPET.findall(markdown)]
    day = _git("log", "-1", "--format=%cs", "--", *(str(p) for p in paths if p.is_file()))
    if day:
        page.update_date = day
    return markdown
