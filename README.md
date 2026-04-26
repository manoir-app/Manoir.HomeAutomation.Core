# Manoir.HomeAutomation.Core

Business domain repo for MaNoir local home automation capabilities.

Current bootstrap scope:

- scenarios;
- scripts;
- Shelly;
- Philips Hue;
- Zigbee2Mqtt;
- WLED.

Repository layout:

- apps/MaNoir.HomeAutomation.AdminUi
- apps/MaNoir.HomeAutomation.Api
- packages/MaNoir.HomeAutomation
- packages/MaNoir.HomeAutomation.Contracts
- tests/MaNoir.HomeAutomation.UnitTests
- tests/MaNoir.HomeAutomation.FunctionalTests

The AdminUi web host references the Api library, and the Api library publishes the root home-automation plugin descriptor at startup with no initial contributions and no dependency repositories.

Local restore uses nuget.org as the package source.
