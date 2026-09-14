# MaNoir.HomeAutomation.Core - Non-negotiable architecture rules

These rules are repository constraints, not suggestions.

## Composition

- Do not use `Microsoft.Extensions.DependencyInjection` as the default composition model.
- Do not add `builder.Services` registrations for domain objects, device objects, command objects, or protocol adapters.
- Prefer explicit construction in the host bootstrap and direct construction of focused helpers.
- Use framework hosting integration only when the framework requires it; keep business composition explicit outside that boundary.
- Do not introduce a service locator, `IServiceProvider` lookup, reflection-based scanning, or automatic registration to replace explicit composition.

## Home automation model

- A device is defined by composable capabilities, not by its protocol or manufacturer.
- Preserve the legacy capability model: `IToggleSwitchDevice`, `IColorBoundDevice`, `IIntensityGradientDevice`, and equivalent capability interfaces.
- Do not make scene execution depend on `Hue`, `Shelly`, `Zigbee2Mqtt`, `Wled`, or another protocol name.
- Do not add platform-specific command services to the scene coordinator.
- Protocol code must remain behind the concrete device implementation that provides one or more capabilities.
- `DeviceRoles` and persisted state describe a device; they do not replace its capability behavior.

## Migration discipline

- Before adding a new integration, identify the corresponding legacy device type and capability interfaces.
- Port the capability contract and concrete device behavior before adding discovery or protocol-specific runtime code.
- Do not create a new runtime, command service, or DI registration until its ownership in the capability model is explicit.
- Do not add dependencies or broad abstractions without a concrete second use case.
- If a required NuGet package is missing, stop and ask the user what to do before adding a local replacement, copying standard functionality, changing package sources, or introducing another dependency.

## Validation

- When a proposed change conflicts with these rules, stop and explain the conflict before editing.
- Keep `Nullable` and `ImplicitUsings` disabled unless the repository explicitly changes that convention.

## Plugin catalog manifest (manoir.plugin.yaml)

- `manoir.plugin.yaml` at this repo's root is the active source manifest. It is not dead and must never be deleted.
- Transformation flow — do not assume, this is what actually happens end to end:

```
this repo's manoir.plugin.yaml (PluginManifest, authored by hand)
        │  CI job "publish-plugin-catalog" in .github/workflows/build.yml
        │  invokes manoir-app/manoir-plugin-action
        ▼
Manoir.PluginCatalog/plugins/<Root>/<Category>/<PluginId>/plugin.yaml (AvailablePlugin, generated)
   + deploy/docker-compose.yml (copied from deployment.artifacts[].path)
   + readme.md (copied from README.md)
```

- It is transformed by the external GitHub Action `manoir-app/manoir-plugin-action` (cloned locally for reference under `manoir-plugin-action/` at the workspace root, not vendored inside this repo) into a lightweight `plugin.yaml` (`AvailablePlugin` format) plus companion artifacts, pushed to a dedicated branch in `Manoir.PluginCatalog`. This is invoked from the `publish-plugin-catalog` job in `.github/workflows/build.yml`.
- Never change this manifest's shape or the publish pipeline without first checking `manoir-plugin-action`'s behavior; a change here likely requires a coordinated change there.
- The action does not currently copy or generate any catalog images; do not assume images are propagated automatically.
- The `catalog.contributions` block of `manoir.plugin.yaml` only produces a lightweight `announcedContributions` summary (kind + labels) in the catalog. It is not parsed by any .NET code. The real admin navigation source of truth is the hand-written `XxxPluginDescriptorProvider.cs` classes (for example `HomeAutomationPluginDescriptorProvider.cs`); keep both in sync manually.
- If you are not sure whether this manifest is still used, whether it is safe to change, or what a given field does, STOP and ask the user to confirm instead of assuming it is dead code — a past session wrongly assumed it should be deleted.