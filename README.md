# DragConvert

A Windows tray app that converts files from a drag gesture: start dragging a file, hold a modifier key, drop it on a format. Everything runs locally; the original file is never modified.

Status: early development (milestone M0, repository and CI). See [docs/design.md](docs/design.md) for the design and implementation plan.

## Build

Requires the .NET 10 SDK (pinned in `global.json`).

```
dotnet build DragConvert.slnx
dotnet test DragConvert.slnx
dotnet run --project src/DragConvert.App
```

## License

[MIT](LICENSE). Third-party components are listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
