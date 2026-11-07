#!/usr/bin/env python3
"""Offline smoke checks for fixture structure and embedded JavaScript syntax."""
from html.parser import HTMLParser
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parent
FIXTURES = [ROOT / "fixtures" / f"fixture-{n}.html" for n in (1, 2, 3)]

class FixtureParser(HTMLParser):
    def __init__(self):
        super().__init__()
        self.tags = []
        self.text = []
    def handle_starttag(self, tag, attrs):
        self.tags.append((tag.lower(), dict(attrs)))
    def handle_data(self, data):
        self.text.append(data.strip())


def main():
    node = shutil.which("node")
    for path in FIXTURES:
        source = path.read_text(encoding="utf-8")
        parser = FixtureParser()
        parser.feed(source)
        parser.close()
        tags = parser.tags
        text = " ".join(parser.text + re.findall(r"<script[^>]*>(.*?)</script>", source, re.I | re.S))
        inputs = [(tag, attrs) for tag, attrs in tags if tag == "input"]
        radios = [attrs for tag, attrs in inputs if attrs.get("type") == "radio"]
        assert len(radios) >= 2, f"{path.name}: expected male/female radio controls"
        assert any("Male" in chunk for chunk in parser.text), f"{path.name}: missing Male option text"
        assert any("Country" in chunk for chunk in parser.text), f"{path.name}: missing Country label text"
        assert any("State" in chunk for chunk in parser.text), f"{path.name}: missing State label text"
        assert any("First Name" in chunk for chunk in parser.text), f"{path.name}: missing First Name label/decoy text"
        assert any(attrs.get("type") == "text" for _, attrs in inputs), f"{path.name}: missing text input"
        scripts = re.findall(r"<script[^>]*>(.*?)</script>", source, re.I | re.S)
        if node:
            for script in scripts:
                with tempfile.NamedTemporaryFile("w", suffix=".js", encoding="utf-8") as handle:
                    handle.write(script)
                    handle.flush()
                    result = subprocess.run([node, "--check", handle.name], capture_output=True, text=True)
                    assert result.returncode == 0, f"{path.name}: invalid JavaScript: {result.stderr}"
        print(f"PASS fixture structure: {path.name}")
        if node:
            print(f"PASS fixture JavaScript syntax: {path.name}")
    print(f"PASS all {len(FIXTURES)} local fixtures validated")
    if not node:
        print("NOTE node not installed; embedded JavaScript syntax check skipped", file=sys.stderr)
    return 0

if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except AssertionError as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        raise SystemExit(1)
