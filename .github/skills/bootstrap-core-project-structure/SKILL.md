---
name: bootstrap-core-project-structure
description: 'Prepare the starting structure of a MaNoir Core-style project family. Use when creating a new capability with contracts, logic, API, admin UI web, and project dependencies including published MaNoir Core NuGet packages from the public package feed.'
argument-hint: 'Describe the capability name, whether it needs API and web admin UI, which contracts must be public, and which published MaNoir Core packages it should depend on from the public feed.'
user-invocable: true
---

# Bootstrap Core Project Structure

Use this skill when you need to prepare the starting project structure for one MaNoir capability or module family.

## Goal

Start with the smallest coherent structure that still supports:

- public contracts when needed;
- backend logic;
- API surface when needed;
- admin web UI when needed;
- test projects split between unit and functional.

## Project Creation Constraints

When the skill proposes creating .NET projects, explicitly avoid these default modern template features:

- do not use top-level statements;
- do not use minimal APIs;
- do not enable nullable reference types by default;
- do not enable implicit usings by default.

Also, except for a clear technical reason, do not rely on dependency injection as the default composition model.

Prefer explicit composition, direct instantiation of focused logic helpers, and simple startup wiring over broad service registration graphs.

Use dependency injection only when there is a concrete reason such as framework integration, hosting constraints, or a real shared lifetime requirement.

Prefer explicit `Program` classes, controller-based APIs when an API host exists, explicit `using` directives, and project files with `Nullable` and `ImplicitUsings` disabled unless the user asks otherwise.

## Default Layout

Use the repository root vocabulary already enforced in MaNoir:

- `apps/` for executable hosts and APIs;
- `packages/` for reusable backend packages and public contracts;
- `ui/` for web admin projects;
- `tests/` for test projects.

## When API And Web Are Needed

If the capability has both API and admin web:

1. create the API project as a reusable library under `packages/MaNoir.X.Api` when the API surface is meant to be hosted by another web project;
2. create the admin web host project under `apps/MaNoir.X.AdminUi`;
3. create the frontend admin web project under `ui/MaNoir.X.AdminUi.<Feature>` when a separate SPA module is needed.

Respect the separation between the two concerns:

- `MaNoir.X.Api` owns API controllers, API contracts mapping, and HTTP-facing platform capability exposure;
- `MaNoir.X.AdminUi` owns the web host and admin-facing composition;
- `MaNoir.X.AdminUi` may reference `MaNoir.X.Api` when the admin host needs to expose that API surface;
- `MaNoir.X.Api` must not become the admin web project;
- `MaNoir.X.AdminUi` must not absorb backend logic that belongs in the backend package or API library.

Do not collapse Admin UI and API into one project. Keep the web host and the API library distinct even when they are deployed together.

Do not put SPA frontend code under `apps/`. Keep the frontend project in `ui/`.

## Minimum Backend Structure

The backend side should normally start with:

- `packages/MaNoir.X.Contracts` for public DTOs, identifiers, commands, and events;
- `packages/MaNoir.X` for the backend logic and persistence helpers.

Inside the backend logic project, prefer a structure aligned with the current Core patterns:

- one `XLogic.cs` root partial class for shared fields and constructor;
- one or more focused partial files such as `XLogic.Persistence.cs`, `XLogic.Crud.cs`, `XLogic.Authentication.cs`, `XBusinessRules.cs`, or equivalent slices by concern;
- one `XMongoOperations.cs` class for raw MongoDB operations;
- optional projection or repository helpers only when justified.

## Dependency Rules

Think about dependencies deliberately:

1. the API library depends on the logic project and the contracts project;
2. the admin web host may depend on the API library when it is the host of that API surface;
3. the admin web frontend depends on public API contracts or clients, never on internal backend logic;
4. the logic project depends on contracts only when the contract model is intentionally public;
5. contracts must not depend on logic or persistence.

## Public NuGet Dependencies

When the new projects depend on published Core packages from this repository, call it out explicitly in the structure proposal.

Expect dependencies such as:

- `MaNoir.Core.Contracts` when public Core contracts are needed;
- `MaNoir.Core` when the capability intentionally builds on reusable backend Core logic;
- `MaNoir.Core.AdminUi.Hosting` when a shared .NET admin host foundation exists and is justified.

Assume these packages are consumed from the public package feed by default.

Reference the public NuGet source first:

- `https://api.nuget.org/v3/index.json`

If the organization also keeps a GitHub Packages publication in parallel, treat it as an additional source only when there is a concrete need for it.

Optional complementary source:

- `https://github.com/manoir-app/MaNoir.Platform/pkgs/nuget/...`

When bootstrapping the project family, also suggest creating the NuGet source configuration file needed to resolve the public feed cleanly for local restore.

Call out that the project setup should include a `NuGet.config` file, or the repo-standard NuGet source configuration file when another convention already exists, with `nuget.org` configured as the default public source for published MaNoir packages.

Only mention adding the GitHub Packages source if the setup explicitly needs the parallel GitHub publication as well.

When preparing the project structure, explicitly list:

- which packages come from local project references;
- which packages come from published Core NuGet packages;
- whether a `NuGet.config` file must be created or updated for the public feed;
- whether GitHub Packages must also be added as a secondary source;
- why each dependency exists.

## Procedure

1. Name the capability and decide whether its contracts are public.
2. Decide whether it needs API, admin web UI, both, or backend only.
3. Propose the minimal project list.
4. Place each project under `apps/`, `packages/`, `ui/`, or `tests/`.
5. State the project references and external NuGet dependencies.
6. State which published Core NuGets must be referenced.
7. State whether `NuGet.config` or the repo-standard NuGet source file must be created or updated for the public feed, and whether a secondary GitHub Packages source is needed.
8. State what should remain out of scope for day one.

## Output Format

Return:

- project list with exact project names;
- folder placement;
- short responsibility per project;
- project reference graph;
- published Core NuGet dependencies to add;
- NuGet source configuration file to create or update for the public feed;
- whether a secondary GitHub Packages source is needed;
- immediate follow-up steps.

## Targeted Excerpts

Use targeted patterns like these in the answer instead of referencing files from this repository.

### Project Family Example

```text
apps/
	MaNoir.X.AdminUi/
packages/
	MaNoir.X.Contracts/
	MaNoir.X/
	MaNoir.X.Api/
ui/
	MaNoir.X.AdminUi.Feature/
tests/
	MaNoir.X.UnitTests/
	MaNoir.X.FunctionalTests/
```

### Project Reference Example

```text
MaNoir.X.Api
	-> MaNoir.X
	-> MaNoir.X.Contracts

MaNoir.X.AdminUi
	-> MaNoir.X.Api

MaNoir.X.AdminUi.Feature
	-> public API contracts or generated client only

MaNoir.X
	-> MaNoir.X.Contracts (only when the shared model is intentionally public)
```

### .NET Project File Example

```xml
<Project Sdk="Microsoft.NET.Sdk">
	<PropertyGroup>
		<TargetFramework>net10.0</TargetFramework>
		<ImplicitUsings>disable</ImplicitUsings>
		<Nullable>disable</Nullable>
	</PropertyGroup>
</Project>
```

### API Host Example

```csharp
public static class Program
{
		public static void Main(string[] args)
		{
				WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
				builder.Services.AddControllers();

				WebApplication app = builder.Build();
				app.MapControllers();
				app.Run();
		}
}
```

### Explicit Composition Example

```csharp
[ApiController]
[Route("api/devices")]
public sealed class DeviceController : ControllerBase
{
	[HttpGet("{deviceId}")]
	public async Task<ActionResult<DeviceDto>> GetById(string deviceId, CancellationToken cancellationToken)
	{
		DeviceLogic logic = new DeviceLogic();
		Device device = await logic.GetByIdAsync(deviceId, cancellationToken);
		if (device == null)
			return NotFound();

		return Ok(new DeviceDto() { Id = device.Id });
	}
}
```

### NuGet Source Example

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
	<packageSources>
		<clear />
		<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
	</packageSources>
</configuration>
```

### Optional Dual-Source Example

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
	<packageSources>
		<clear />
		<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
		<add key="github-manoir" value="https://nuget.pkg.github.com/manoir-app/index.json" />
	</packageSources>
</configuration>
```

### Dependency Summary Example

```text
Local project references:
- MaNoir.X.Api -> MaNoir.X
- MaNoir.X.Api -> MaNoir.X.Contracts

Published Core NuGets:
- MaNoir.Core.Contracts
- MaNoir.Core
- MaNoir.Core.AdminUi.Hosting

Default public source:
- nuget.org

Optional secondary source:
- GitHub Packages for the manoir-app organization
```
