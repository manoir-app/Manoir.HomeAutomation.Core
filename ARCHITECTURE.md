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

Planned concrete runtime services:

- `SceneExecutionService` or equivalent local scene coordinator;
- `TriggerRuntimeService` for clock and MQTT trigger monitoring;
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

## Non-goals

- no separate generic worker process distinct from Sarah;
- no per-protocol microservice split by default;
- no interface-first abstraction layer before the second real runtime implementation needs it.

## Next implementation steps

1. Wire local scene execution inside Sarah for `homeautomation.scenario.execute` and `homeautomation.scenario.disable`.
2. Add a trigger runtime loop that handles clock triggers first, then MQTT watchers.
3. Add protocol bootstrap for the first real integration to port.
4. Move integration activation/configuration concerns into Sarah once the first protocol is live.