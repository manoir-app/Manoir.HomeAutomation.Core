# Manoir.HomeAutomation.Core Architecture

## Goal

This repository owns the local home automation domain for MaNoir.

The target runtime model is:

- one configuration/API process;
- one operational agent process named Sarah;
- shared domain packages used by both.

This keeps the operational model close to the legacy Home.Agents.Sarah process while aligning the new codebase with the runtime pattern already used by Erza in Manoir.Platform.

## Processes

### MaNoir.HomeAutomation.AdminUi

This process hosts the API module and the plugin descriptor publication.

Responsibilities:

- expose administration endpoints;
- expose scenes, triggers, and discovery endpoints;
- persist domain state through the package logic.

It is not the long-running automation runtime.

### MaNoir.Agents.Sarah

This process is the operational runtime for home automation.

Responsibilities:

- register itself in the mesh as agent `sarah`;
- maintain lifecycle and heartbeat;
- listen to interprocess messages on NATS;
- host scene execution;
- host trigger monitoring and firing;
- host protocol runtimes and device discovery;
- publish home automation messages and device state changes.

This process is the replacement for the old all-in-one Sarah executable.

## Shared packages

### packages/MaNoir.HomeAutomation.Contracts

Public models and messages for:

- devices and discovered devices;
- scenes and scene groups;
- triggers and conditions;
- legacy interprocess messages still used by the runtime.

### packages/MaNoir.HomeAutomation

Domain logic and persistence for:

- devices;
- discovered devices;
- scenes;
- triggers;
- plugin descriptor support and projections.

This package should keep the business logic and persistence rules. Runtime loops and long-running subscriptions belong in Sarah unless they are clearly reusable outside the agent process.

## Sarah internal structure

The concrete structure of Sarah should mirror the Erza pattern:

- one runtime object carrying identity, mesh, capabilities, topics, and reporting helpers;
- a lifecycle service for registration and heartbeat;
- a message pump service for NATS subscriptions;
- targeted runtime services for each long-running concern.

Current bootstrap classes:

- `SarahRuntime`
- `LifecycleHeartbeatService`
- `MessagePumpService`
- `SarahMessageRouter`

The executable host composes these services explicitly in `Program` using the .NET hosting
boundary required for long-running background services. Business objects and protocol runtimes
remain concrete types; the host registration is not a domain-level dependency injection model.

The current protocol services are:

- `HueRuntimeService`, which polls a configured Hue bridge and feeds the runtime device registry;
- `Zigbee2MqttRuntimeService`, which subscribes to MQTT discovery, state, action and availability topics;
- `ShellyGen1RuntimeService` and `ShellyGen2RuntimeService`, which own their respective onboarding and runtime loops;
- `TriggerRuntimeService`, which owns trigger loading and autonomous trigger monitoring.

Each protocol service owns its transport loop and creates concrete runtime devices. `RuntimeDeviceRegistry`
is the shared runtime lookup and snapshot boundary; it is not a protocol dispatcher. `SarahMessageRouter`
handles only cross-runtime messages such as scene execution, trigger changes and Shelly onboarding.

Planned concrete runtime services:

- `SceneExecutionService` or equivalent local scene coordinator;
- `TriggerRuntimeService` for clock and MQTT trigger monitoring (midnight, sunrise and sunset offsets, MQTT wildcards, JSON extraction and numeric thresholds are implemented);
- `ProtocolRuntimeService` for starting protocol-specific runtimes;
- optional cleanup/maintenance services when a loop is clearly autonomous.

## Message flow

### Scenes

1. The API persists scene definitions.
2. A client requests a scene execution.
3. The domain logic publishes `homeautomation.scenario.execute`.
4. Sarah consumes that message and performs the local execution runtime.

The API remains the source of truth for catalog data.
Sarah remains the source of runtime execution.

### Triggers

1. The API persists trigger definitions.
2. Sarah loads and watches those definitions.
3. Sarah raises trigger messages and side effects when conditions match.

Manual raise through API remains valid for tests and webhooks.

### Protocols

1. Sarah starts the concrete protocol runtimes.
2. Each protocol runtime discovers devices or receives state changes.
3. Shared domain logic stores devices and publishes normalized events.

Protocol-specific code should stay behind Sarah and write into the normalized domain model rather than leaking protocol structures into the API surface.

## Device composition model

The persisted `Home.Common.Model.Device` contract is not itself the runtime behavior model. It stores identity, configuration and normalized state. Runtime behavior is composed explicitly from device elements and capabilities.

### Devices and elements

A runtime device has one or more named elements. Capabilities belong to elements, not only to the physical device.

```text
Device
	-> Element 0
			 -> IToggleSwitchDevice
			 -> IPowerMeteringDevice
	-> Element 1
			 -> IToggleSwitchDevice
			 -> IPowerMeteringDevice
```

This models devices such as a Shelly with two or three independently controllable outputs. `TargetDataName` identifies the element targeted by a scene step; it must not be treated as a device-wide role.

### Hub devices

A hub is a device that also exposes the `IHubDevice` capability and manages child devices.

```text
Hue Bridge : IHubDevice
	-> Hue Light 1
	-> Hue Light 2
```

This is distinct from a multi-element device: a Shelly with several outputs has multiple elements, while a Hue Bridge or Zigbee coordinator has separate child devices. The persisted relationship should use stable child references; runtime code may resolve those references to concrete device objects.

### Capability rules

- A capability must be addressable in the context of its element.
- A physical device may expose multiple elements with different capability compositions.
- `DeviceRoles` and normalized `DeviceData` are projections of runtime capabilities and state; they are not substitutes for capability behavior.
- Protocol implementations remain private to the concrete device or hub implementation.
- `IHubDevice` is a capability of a device, not a replacement for the device model.
- Chromatic color and white color temperature are separate capabilities. `IChromaticColorDevice` covers RGB, RGBW, HSV and XY, including protocol conversion; `IColorTemperatureDevice` covers Kelvin values and device-specific bounds.
- A device may expose both capabilities on one element, as a Hue light can, but neither capability should pretend to be the other.
- `IDeviceDiscoverySource` performs one discovery operation and returns a runtime snapshot. It does not own a polling loop.
- `RuntimeDiscoveryCoordinator` owns periodic polling in Sarah and applies snapshots to `RuntimeDeviceRegistry`, which reports added, updated and removed devices per source.
- A manually configured Hue bridge is a passive `IHubDevice`; `HueDiscoverySource` interrogates it, while Sarah owns the polling lifetime.
- Scene execution resolves a runtime device and element, then invokes its typed capability. It has no protocol-specific command-service fallback.

## Non-goals

- no separate generic worker process distinct from Sarah;
- no per-protocol microservice split by default;
- no interface-first abstraction layer before the second real runtime implementation needs it.

## Next implementation steps

1. Add presence and network-device trigger sources.
2. Add wake-up offsets when scheduler data is available in the new platform.
3. Add protocol bootstrap for the first real integration to port.
4. Move integration activation/configuration concerns into Sarah once the first protocol is live.