# GitHub Pages landing

This folder contains the static landing page for DNP3 Interoperability Tester.

Recommended publishing mode:

1. Open the repository on GitHub.
2. Go to `Settings` > `Pages`.
3. Set `Source` to `GitHub Actions`.
4. Run the `Deploy GitHub Pages` workflow, or push to `main` / `master`.

The landing page is intentionally static and does not affect the WPF desktop
application build.

Avoid `Deploy from a branch` for this repository. That mode can invoke the
GitHub Pages Jekyll pipeline even though this site is plain HTML/CSS.
