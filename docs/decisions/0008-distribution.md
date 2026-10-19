# 0008. Distribution: GitHub only

Date: 2026-10-19
Status: accepted (decided by the project owner)

DragConvert ships as downloads from GitHub Releases only; there is no Microsoft Store package.

## Consequences for M6

- Packaging targets a self-contained single-file build (and optionally a zip or installer) published to GitHub
  Releases by CI; no MSIX Store submission or Store certification spike.
- Code signing goes through the SignPath Foundation (free for open-source projects; needs a public repository and an
  OSI license, already MIT). Until then, builds are unsigned pre-releases with a note about the SmartScreen prompt.
- Updates: a winget manifest for the signed builds; an in-app update check is optional and must stay opt-in (the app
  makes no network calls by default).
- Notifications keep working without a package: the app registers its own AppUserModelID (ADR 0004).
- The design doc's Store rows (distribution table, M6 tasks, risks) no longer apply.
