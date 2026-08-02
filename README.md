# Manoir.HomeAutomation.Core

Business domain repo for MaNoir local home automation capabilities.

Current bootstrap scope:

- home automation contracts and persistence;
- scenes and triggers APIs;
- device discovery and state publication;
- Sarah agent process bootstrap.

Planned runtime scope inside Sarah:

- scenes execution runtime;
- triggers runtime;
- Shelly;
- Philips Hue;
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

For Shelly onboarding, call `POST /v1.0/devices/shelly/onboard` with an `ipAddress`. Sarah identifies the device from `GET /shelly`; the current runtime supports the Gen1 response shape. Configure `SHELLY_GEN1_PASSWORD` before starting Sarah. If local authentication is disabled, Sarah sets Basic Auth with the optional `SHELLY_GEN1_USERNAME` (default: `sarah`), configures MQTT to `MQTT_SERVICE_HOST:MQTT_SERVICE_PORT`, then reboots the device. A device already protected by these credentials is not reconfigured when its MQTT connection is already correct.

Local restore uses nuget.org as the package source.
