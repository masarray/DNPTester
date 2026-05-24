# GitHub Pages documentation site

This folder contains the static landing page and product documentation shell for
DNP3 Interoperability Tester. It is intentionally plain HTML, CSS, and
JavaScript so it can be published without adding a web build dependency to the
desktop application.

Current positioning:

- GPL-3 source tree
- Native C# DNP3 master stack
- Internal PDF evidence report engine
- Windows desktop app for SCADA/FAT troubleshooting

Local preview:

```powershell
Start-Process .\docs\index.html
```

Recommended publishing mode:

1. Open the repository on GitHub.
2. Go to `Settings` > `Pages`.
3. Set `Source` to `GitHub Actions`.
4. Run the `Deploy GitHub Pages` workflow, or push to `main` / `master`.

The documentation site is intentionally static and does not affect the WPF
desktop application build.

Avoid `Deploy from a branch` for this repository. That mode can invoke the
GitHub Pages Jekyll pipeline even though this site is plain HTML/CSS.
