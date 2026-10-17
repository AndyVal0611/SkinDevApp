#!/usr/bin/env python
"""md_to_html.py - converts a Markdown file to a clean, printable HTML page (A4, tables, headings) so a browser can print it to PDF.
usage: python md_to_html.py input.md output.html "Title"   (needs the 'mistune' package; no network access is used)"""
import sys, html
from pathlib import Path
import mistune

CSS = """
@page { size: A4; margin: 16mm 15mm 16mm 15mm; }
* { box-sizing: border-box; }
body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 10.5pt; line-height: 1.45; color: #1d2330; margin: 0; }
h1 { font-size: 20pt; margin: 0 0 6pt; color: #12304f; border-bottom: 3px solid #2f6db5; padding-bottom: 5pt; }
h2 { font-size: 14.5pt; margin: 18pt 0 6pt; color: #12304f; border-bottom: 1.5px solid #c9d6e8; padding-bottom: 3pt; page-break-after: avoid; }
h3 { font-size: 12pt; margin: 13pt 0 4pt; color: #1f4f86; page-break-after: avoid; }
p, ul, ol { margin: 4pt 0 6pt; } li { margin: 2pt 0; }
table { border-collapse: collapse; width: 100%; margin: 6pt 0 10pt; font-size: 9.6pt; page-break-inside: auto; }
tr { page-break-inside: avoid; }
th { background: #e8f0fa; color: #12304f; text-align: left; }
th, td { border: 1px solid #c3cfdf; padding: 3.5pt 6pt; vertical-align: top; }
tbody tr:nth-child(even) td { background: #f7f9fc; }
code { font-family: Consolas, 'Courier New', monospace; font-size: 9pt; background: #eef2f7; padding: 0 3pt; border-radius: 2pt; }
pre { background: #f2f5f9; border: 1px solid #d5dde8; padding: 6pt 8pt; font-size: 8.8pt; white-space: pre-wrap; }
hr { border: none; border-top: 1px solid #c9d6e8; margin: 12pt 0; }
strong { color: #0e2742; }
"""

def main():
    src, dst, title = Path(sys.argv[1]), Path(sys.argv[2]), (sys.argv[3] if len(sys.argv) > 3 else Path(sys.argv[1]).stem)
    md = mistune.create_markdown(plugins=["table", "strikethrough"], escape=False)
    body = md(src.read_text(encoding="utf-8"))
    dst.write_text("<!doctype html><html><head><meta charset='utf-8'><title>%s</title><style>%s</style></head><body>%s</body></html>" % (html.escape(title), CSS, body), encoding="utf-8")
    print("wrote", dst)

if __name__ == "__main__":
    main()
