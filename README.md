# Manoir.HomeAutomation.Core

Business domain repo for MaNoir local home automation capabilities.

Current bootstrap scope:

- home automation contracts and persistence;
- scenes and triggers APIs;
- device discovery and state publication;
- Sarah agent process bootstrap.

Planned runtime scope inside Sarah:

- scenes execution runtime;
- triggers runtime for clock and MQTT value triggers;
- Shelly;
- Philips Hue discovery, state synchronization and on/off/brightness commands;
- Zigbee2Mqtt;
- WLED.

Repository layout:

- apps/MaNoir.Agents.Sarah
- apps/MaNoir.HomeAutomation.AdminUi
- apps/MaNoir.HomeAutomation.Api
- packages/MaNoir.HomeAutomation
- packages/MaNoir.HomeAutomation.Contracts
- tests/MaNoir.HomeAutomation.UnitTests
- tests/MaNoir.HomeAutomation.FunctionalTests

The AdminUi web host references the Api library, and the Api library publishes the root home-automation plugin descriptor at startup with no initial contributions and no dependency repositories.

The Sarah agent is the operational runtime process for home automation. It is intended to host the runtime loops, protocol integrations, and message routing, following the same broad pattern as Erza in Manoir.Platform.

Sarah is composed at the host boundary in `apps/MaNoir.Agents.Sarah/Program.cs`. The host registers
the lifecycle, NATS message pump, trigger, scene, Hue, Zigbee2MQTT and Shelly background services.
Protocol services publish concrete runtime devices through `RuntimeDeviceRegistry`; scene execution
uses device capabilities rather than protocol-specific command services.

For Shelly onboarding, call `POST /v1.0/devices/shelly/onboard` with an `ipAddress`. Sarah identifies the device from `GET /shelly`; the current runtime supports the Gen1 response shape. Configure `SHELLY_GEN1_PASSWORD` before starting Sarah. If local authentication is disabled, Sarah sets Basic Auth with the optional `SHELLY_GEN1_USERNAME` (default: `sarah`), configures MQTT to `MQTT_SERVICE_HOST:MQTT_SERVICE_PORT`, then reboots the device. A device already protected by these credentials is not reconfigured when its MQTT connection is already correct.

For Philips Hue, configure `HUE_BRIDGE_ADDRESS` and `HUE_API_KEY` before starting Sarah. `HUE_BRIDGE_ADDRESS` accepts an address such as `192.168.1.20` or a full `http://`/`https://` URL. Sarah polls `/api/{key}/lights` every 30 seconds by default; `HUE_POLL_INTERVAL_SECONDS` can override the interval between 5 and 3600 seconds. Hue lights are registered with stable `hue-{lightId}` identifiers and expose switch, dimmer and XY color state when supported by the bridge.

Sarah currently reloads persisted triggers on startup and after `system.triggers.change`. Clock triggers support offsets from midnight, sunrise and sunset when the local mesh location has coordinates. MQTT value triggers support MQTT wildcards, JSON value extraction and numeric change thresholds. Presence and network-device triggers are not wired yet.

Local restore uses nuget.org as the package source.
