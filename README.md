# Jellys

Jellyfin plugins repository.

## Layout

- `plugins/<PluginName>/` — one .NET project per plugin
- `manifest.json` — Jellyfin plugin repository manifest (add its raw URL under Dashboard → Plugins → Repositories)

## Build

```
dotnet build plugins/<PluginName> -c Release
```

## Add a plugin

1. Create `plugins/<PluginName>` (class library targeting `net9.0`, reference `Jellyfin.Controller`).
2. Zip the built DLL, compute MD5, add a version entry to `manifest.json`.
