# Backlog

## Technical Debt

- Replace hardcoded absolute base URL generation in Device contracts logic, currently in `DeviceActionnable.ToUrl`, once MaNoir.Core exposes a shared way to resolve the main/public base URL. Keep the current hardcoded behavior until that Core capability exists to avoid inventing a local workaround.

## Architecture Recovery

These items restore the original MaNoir home-automation model before adding another protocol integration.

### Capability scope decision

- [x] Keep the legacy home-automation capabilities in scope: switch, intensity, color, sensors, power metering, occupancy and action buttons.
- [x] Keep `IStorageDevice` in the MaNoir capability model, but migrate it after the core home-automation capabilities.
- [x] Keep `DisplayDeviceBase` in the MaNoir device model, but migrate display capabilities after the core home-automation capabilities.
- [ ] Do not let storage or display migration block the first switch/dimmer/color vertical.

### 1. Re-establish capability contracts

- [ ] Inventory the legacy device capabilities in `home-automation/Common/Home.Common/HomeAutomation`.
- [ ] Define the modern capability contracts for toggle switches, intensity/dimming, color-bound devices, sensors, power metering, occupancy, actions and hubs.
- [ ] Decide which legacy method shapes remain valid and which need asynchronous transport-aware equivalents.
- [ ] Keep capability contracts independent from Hue, Shelly, Zigbee2Mqtt, WLED and other protocols.
- [ ] Add focused contract tests for capability composition and command/state semantics.

### 2. Restore concrete device objects

- [ ] Introduce concrete modern device objects that compose capability interfaces, as the legacy `HueLightSwitch` and `HueColorLight` did.
- [ ] Keep persisted `Device` data as identity, configuration and normalized state; do not use `DeviceRoles` as a replacement for behavior.
- [ ] Define how a concrete device object uses persisted configuration while keeping its protocol transport private.
- [ ] Define the device registry/manager responsible for locating concrete devices by stable identifier.
- [ ] Keep device creation and capability composition explicit and inspectable.

### 3. Remove protocol knowledge from scene execution

- [x] Refactor `SceneExecutionService` so it does not reference Hue, Shelly, Zigbee2Mqtt, WLED or protocol command services.
- [ ] Route scene operations through device capabilities (`IToggleSwitchDevice`, `IIntensityGradientDevice`, `IColorBoundDevice`, etc.).
- [ ] Preserve `TargetDataName`, normalized state updates and scene error reporting during the refactor.
- [ ] Add scene tests proving that the same scene operation works through different concrete device implementations.
- [ ] Do not introduce a generic platform command dispatcher as a substitute for the capability model.

### 4. Leave .NET DI out of business composition

- [ ] Keep Sarah host startup explicit and concrete where hosting does not require framework registration.
- [ ] Remove business object, device object, protocol adapter and command registration graphs from `Program.cs`.
- [ ] Do not add `IServiceProvider` lookups, service locators, reflection scanning or automatic registration.
- [ ] Retain only framework-required hosting integration, with a concrete reason documented at the boundary.
- [ ] Add a repository validation check that rejects accidental use of `builder.Services` or `Microsoft.Extensions.DependencyInjection` in forbidden business/runtime areas.

## Existing Integration Migration

Migrate existing modern integrations onto the restored model instead of adding new platform-specific command paths.

### Hue

- [ ] Extract the existing Hue HTTP, JSON, timeout, retry and color conversion code into concrete Hue device implementations.
- [ ] Implement Hue switch, dimmer and color capabilities on the appropriate concrete device types.
- [ ] Preserve XY, HS, RGB and color-temperature conversions and gamut clipping.
- [ ] Keep Hue bridge discovery and polling as transport/runtime concerns behind the device implementations.
- [x] Replace the legacy Hue polling loop with the runtime registry fed by the existing Hue polling cycle, without running two `/lights` polling loops.
- [ ] Port or replace the incomplete legacy `HueColorLight` behavior with tested implementations.
- [x] Route scene device steps exclusively through runtime capabilities.
- [ ] Keep Hue onboarding separate from the capability migration; onboarding still requires a user-facing real-time flow.

### Shelly and Zigbee2Mqtt

- [ ] Inventory the capabilities currently encoded in `ShellyGen1CommandService`, `ShellyGen2CommandService` and `Zigbee2MqttCommandService`.
- [ ] Move switch, dimmer, color, cover, sensor, energy and action behavior into concrete capability-bearing device objects.
- [ ] Keep MQTT/RPC/HTTP protocol handling private to the relevant device implementation.
- [ ] Preserve existing discovery, availability and normalized state publication behavior.
- [ ] Migrate one representative device from each integration before broad cleanup.

## WLED Integration

WLED is intentionally blocked until the capability model and explicit composition are restored.

- [ ] Confirm the WLED legacy device shape and supported capabilities from the old implementation.
- [ ] Define the WLED concrete device type and compose its switch, dimmer and color capabilities.
- [ ] Keep `/json/info`, `/json/state` and HTTP command payloads private to the WLED implementation.
- [ ] Add mock HTTP tests for discovery, state refresh, on/off, brightness and RGB commands.
- [ ] Add WLED discovery/configuration only after the concrete device path is working.
- [ ] Document WLED configuration after the implementation is capability-based.

## Composition and Runtime Validation

- [ ] Document the explicit Sarah bootstrap and ownership of runtime loops.
- [ ] Ensure protocol runtimes can discover/update concrete devices without exposing protocol DTOs through the API contracts.
- [ ] Verify that API persistence remains the source of truth for catalog data and Sarah remains the local execution runtime.
- [ ] Add architecture-focused tests preventing scene code from depending on protocol names.
- [ ] Build Sarah and run focused unit/functional tests after each migration slice.