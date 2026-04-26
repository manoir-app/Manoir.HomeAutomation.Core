---
name: bootstrap-unit-functional-tests
description: 'Prepare MaNoir test projects with the split between unit tests and functional tests. Use when creating the test project structure for a capability, deciding what belongs in UnitTests vs FunctionalTests, and wiring the first focused test files.'
argument-hint: 'Describe the capability under test, its backend projects, whether Mongo or API hosts are involved, and which logic should be covered by unit tests versus functional tests.'
user-invocable: true
---

# Bootstrap Unit Functional Tests

Use this skill when you need to create or extend the MaNoir test project structure for one capability.

## Goal

Create a clear split between:

- unit tests for pure logic and in-memory rules;
- functional tests for persistence, API behavior, startup behavior, and integration with real infrastructure.

## Project Creation Constraints

When this skill suggests creating .NET test projects, explicitly avoid:

- top-level statements;
- nullable reference types enabled by default;
- implicit usings enabled by default.

Prefer explicit test classes and classic test project files with `Nullable` and `ImplicitUsings` disabled unless the user asks otherwise.

## Project Split

Prefer this default split:

- `tests/MaNoir.X.UnitTests`;
- `tests/MaNoir.X.FunctionalTests`.

Do not collapse both styles into one single test project.

## Unit Test Scope

Unit tests should cover:

- normalization helpers;
- pure business rules;
- in-memory state transitions;
- rule guards that do not need Mongo or ASP.NET.

Typical anchors in the current repo:

- one unit test file per pure logic slice;
- one test class focused on normalization and in-memory state changes.

## Functional Test Scope

Functional tests should cover:

- Mongo persistence behavior;
- startup registration behavior;
- API endpoints and auth flows;
- integration with real infrastructure bootstrapped by TestContainers.

## TestContainers

Treat TestContainers as a first-class part of the functional test setup, not as an optional detail.

When functional tests need Mongo, NATS, Mosquitto, or another real service, prefer repo test hosts backed by TestContainers instead of assuming a manually started local dependency.

The current repo already follows that pattern with shared functional test hosts such as:

- `MongoDbFunctionalTestHost`;
- `NatsFunctionalTestHost` when messaging is involved;
- `MosquittoFunctionalTestHost` when MQTT behavior is involved.

When bootstrapping a new functional test project or capability slice, explicitly decide:

- which infrastructure must run in TestContainers;
- which helper hosts must be created;
- which NuGet packages for TestContainers must be referenced.

Do not treat infrastructure boot as ad hoc test code duplicated in every test file.

### TestContainers Host Example

```csharp
using Testcontainers.MongoDb;

internal sealed class MongoDbFunctionalTestHost : IAsyncDisposable
{
	private static readonly MongoDbContainer Container = new MongoDbBuilder("mongo:7.0").Build();

	public string ConnectionString => Container.GetConnectionString();

	public async Task StartAsync()
	{
		await Container.StartAsync();
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

### Assembly Cleanup Example

```csharp
[TestClass]
public sealed class FunctionalTestAssemblyHooks
{
	[AssemblyCleanup]
	public static async Task CleanupAsync()
	{
		await MongoDbFunctionalTestHost.DisposeSharedAsync();
	}
}
```

### Functional Test Example

```csharp
[TestMethod]
public async Task DeviceCrud_ShouldPersistThroughMongo()
{
	await using MongoDbFunctionalTestHost host = new MongoDbFunctionalTestHost();
	await host.StartAsync();

	using ProcessEnvironmentVariableScope scope = new ProcessEnvironmentVariableScope("MONGODB_CONNECTIONSTRING", host.ConnectionString);

	DeviceLogic logic = new DeviceLogic();
	Device created = await logic.UpsertAsync(new Device() { Id = "device-1" });

	Assert.IsNotNull(created);
}
```

## Procedure

1. Identify which logic is pure and which logic touches persistence or hosts.
2. Create the two test projects if they do not exist yet.
3. Put pure-rule tests in `UnitTests`.
4. Put Mongo, API, startup, and end-to-end behavior in `FunctionalTests`.
5. Add the minimum TestContainers-backed infrastructure hosts needed in each functional test project.
6. Add assembly-level cleanup hooks when shared containers must be disposed once per test assembly.
7. Use focused test files grouped by capability slice, not one giant catch-all file.

## Minimum Checklist

Before considering the test structure complete, verify:

- unit tests do not require Mongo or ASP.NET hosts;
- functional tests use the repo test infrastructure when persistence is involved;
- functional tests do not assume manually started local services when TestContainers should own the dependency lifecycle;
- TestContainers package references and helper hosts are identified explicitly;
- each important logic slice has at least one unit or functional anchor;
- the split is understandable from folder and file names alone.

## Output Format

Return:

- the test project names to create;
- what each project references;
- which first test files to add;
- which scenarios belong to unit vs functional;
- which TestContainers-backed infrastructure must be wired first;
- which helper test hosts and cleanup hooks must be created.

## References

Use the excerpts above as explanation anchors. Prefer showing a minimal unit-test example and a minimal TestContainers-backed functional-test example over pointing to repo-local files.
