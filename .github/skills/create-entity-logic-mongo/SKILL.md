---
name: create-entity-logic-mongo
description: 'Create one MaNoir entity slice with entity model, pure business rules, logic partials, Mongo operations, and optional projection into the generic Entity catalog. Use when adding a new aggregate or document family with XLogic, XMongoOperations, a clean split between pure logic and persistence, and possibly a projected IProjectedEntityRepository registered in the EntityProjectionRepositoryRegistry.'
argument-hint: 'Describe the entity name, whether it belongs in Contracts, which operations are needed, what rules must stay pure and persistence-free, and whether the domain entity must be exposed through the generic Entity catalog.'
user-invocable: true
---

# Create Entity Logic Mongo

Use this skill when you need to create one new backend entity slice in the MaNoir Core style.

## Goal

Create a coherent slice with:

- the entity model;
- pure business rules separated from persistence;
- the main logic class split into partial files by concern;
- a Mongo operations helper dedicated to database access.
- an optional projection path from the domain entity into the generic `Entity` model.

## Code Style Constraints

When this skill creates or suggests project code, explicitly avoid:

- top-level statements;
- minimal APIs;
- nullable reference types enabled by default;
- implicit usings enabled by default.

Prefer explicit classes, explicit `using` directives, and classic project configuration.

## Target Pattern

Follow the current Core-style backend split:

1. one root partial logic class such as `XLogic.cs` for constructor and shared dependencies;
2. focused partial files such as:
- `XLogic.Persistence.cs` for read/write orchestration;
- `XLogic.Crud.cs` for basic lifecycle operations;
- `XBusinessRules.cs` or equivalent for pure operations and normalization helpers;
3. one `XMongoOperations.cs` class for direct MongoDB collection access and query primitives.
4. when the domain entity must appear in the generic catalog, one `XEntityConstants.cs` file for owned kinds and categories;
5. when the domain entity must appear in the generic catalog, one `XProjectedEntityRepository.cs` class implementing `IProjectedEntityRepository`.

## Pure Logic Rule

Keep pure logic truly persistence-free.

Pure methods should:

- normalize identifiers;
- validate in-memory state;
- apply business rule transformations;
- decide whether a state change is allowed.

Pure methods should not:

- call MongoDB;
- depend on ASP.NET;
- depend on HTTP concepts;
- hide persistence access behind a pseudo-pure helper.

## Persistence Rule

`XMongoOperations` should stay low-level and explicit.

It should:

- expose collection access and focused Mongo queries;
- validate only raw persistence prerequisites such as missing identifiers;
- avoid embedding orchestration or business policy.

If the shared Core package already publishes the Mongo helper infrastructure through NuGet, use that public helper instead of rebuilding connection plumbing locally.

### Mongo Helper Example

```csharp
using MaNoir.Core.DataAccess;
using MongoDB.Driver;

public sealed class DeviceMongoOperations
{
	private readonly MongoDbHelper _mongo;
	private readonly IMongoCollection<Device> _collection;

	public DeviceMongoOperations()
	{
		_mongo = new MongoDbHelper();
		_collection = _mongo.GetCollection<Device>();
	}

	public Task<Device> GetByIdAsync(string id, CancellationToken cancellationToken = default)
	{
		return _collection.Find(device => device.Id == id).FirstOrDefaultAsync(cancellationToken);
	}
}
```

Assume the helper resolves the MongoDB connection through environment variables provided by the platform bootstrap.

## Contracts Placement

Put the entity in `Contracts` only when it is a real public exchange model.

If the type is only useful inside this repo, keep it out of `Contracts`.

## Generic Entity Projection Rule

Do not make the domain-specific entity disappear behind the generic `Entity` model.

The domain model remains the source of truth.

Use the generic `Entity` model only when the slice must participate in the shared entity catalog consumed by generic APIs, integrations, or UI surfaces.

The goal of a projection is precisely to avoid persisting the same business object twice: once in the domain-owned Mongo model and once again in the generic `Entity` storage.

When a domain entity is projected into the generic catalog:

- keep the domain-owned model and logic as the authoritative source;
- convert the domain model into a read-only generic `Entity` in a dedicated projected repository;
- keep projection code explicit instead of hiding it inside the main domain logic class;
- define domain-owned entity kind and category constants in a dedicated constants class;
- do not mirror-persist the same object into `EntityMongoOperations`.

Use true generic entity persistence only when `Entity` itself is the native source of truth for that data.

That case is different from a projection. It applies only to data that is born generic and does not already have its own domain-owned persisted model.

Typical examples are generic annotations, ad hoc catalog entries, lightweight cross-domain markers, or other data whose canonical storage is the generic entity catalog itself.

Do not use native generic entity persistence for a domain object that already has its own Mongo model and logic. In that case, project it through `IProjectedEntityRepository` and keep a single persisted source of truth.

Projected entities are read-only by design once they enter the generic entity catalog.

## Registry Rule

Register projected repositories explicitly in `EntityProjectionRepositoryRegistry`.

Follow the current Core pattern:

- implement `IProjectedEntityRepository` for each domain-owned projection source;
- expose one stable `Source` string describing the projection origin;
- declare the supported entity kinds explicitly;
- register the repository in the registry creation path or explicit composition root;
- avoid magic scanning, reflection-based auto-registration, or hidden DI registration.

The registry is part of the composition boundary, not part of the domain entity itself.

## Procedure

1. Decide whether the entity model is public or internal.
2. Decide whether the source of truth is a domain-specific model or the generic `Entity` catalog itself.
3. If the source of truth is domain-specific, decide whether it must also be exposed through the generic `Entity` catalog.
4. Create the entity model in the right project.
5. Create the root partial `XLogic.cs` with constructor and dependencies.
6. Create the pure rules file for normalization and state transitions.
7. Create the persistence partial for orchestrated read/write methods.
8. Create `XMongoOperations.cs` for raw Mongo access.
9. If the entity must appear in the generic catalog while staying domain-owned, create `XEntityConstants.cs`.
10. If the entity must appear in the generic catalog while staying domain-owned, create `XProjectedEntityRepository.cs` with explicit conversion from the domain model to `Entity`.
11. Register the projected repository in `EntityProjectionRepositoryRegistry` or in the explicit composition root that builds the registry.
12. Use native `EntityLogic` persistence only if `Entity` is the sole persisted model for that slice.
13. Keep naming, normalization, and rule patterns aligned with the current Core codebase.

## Minimum Checklist

Before considering the slice complete, verify:

- identifiers are normalized consistently;
- pure rules do not call persistence;
- Mongo operations do not own business decisions;
- logic methods orchestrate the sequence clearly;
- contracts were introduced only if cross-repo exposure is justified;
- projected generic entities are produced by a dedicated projected repository, not by ad hoc controller code;
- projected generic entities are read-only and keep the domain model as source of truth;
- the same business object is not persisted both as a domain Mongo document and as a native generic `Entity`;
- the registry wiring is explicit.

## Output Format

Return or create:

- the target files to add;
- what each file owns;
- which methods are pure;
- which methods touch Mongo;
- which model types belong in Contracts and which stay internal;
- whether the slice also exposes a generic `Entity` projection;
- whether `Entity` is only a projection or the actual persisted source of truth;
- where the projection repository must be registered.

## Targeted Excerpts

Use targeted patterns like these in the answer instead of referencing files from this repository.

### Logic Root Example

```csharp
public sealed partial class DeviceLogic
{
	private readonly DeviceMongoOperations _mongoOperations;

	public DeviceLogic()
	{
		_mongoOperations = new DeviceMongoOperations();
	}
}
```

### Pure Rules Example

```csharp
public sealed partial class DeviceLogic
{
	internal static string NormalizeDeviceId(string deviceId)
	{
		if (string.IsNullOrWhiteSpace(deviceId))
			return null;

		return deviceId.Trim().ToLowerInvariant();
	}
}
```

### Persistence Orchestration Example

```csharp
public sealed partial class DeviceLogic
{
	public async Task<Device> GetByIdAsync(string deviceId, CancellationToken cancellationToken = default)
	{
		string normalizedDeviceId = NormalizeDeviceId(deviceId);
		if (normalizedDeviceId == null)
			return null;

		return await _mongoOperations.GetByIdAsync(normalizedDeviceId, cancellationToken);
	}
}
```

### Entity Constants Example

```csharp
public static class DeviceEntityConstants
{
	public static class Kinds
	{
		public const string Device = "manoirapp:device";
	}

	public static class Categories
	{
		public const string Identity = "identity";
		public const string Status = "status";
	}
}
```

### Projected Entity Repository Example

```csharp
using MaNoir.Core.Contracts.Models.Entities;
using MaNoir.Core.Entities;

public sealed class DeviceProjectedEntityRepository : IProjectedEntityRepository
{
	public string Source => "devices/catalog";

	public IReadOnlyCollection<string> SupportedKinds => [DeviceEntityConstants.Kinds.Device];

	public async Task<Entity> GetByIdAsync(string kind, string entityId, CancellationToken cancellationToken = default)
	{
		string normalizedKind = EntityLogic.NormalizeEntityKind(kind);
		string normalizedEntityId = EntityLogic.NormalizeEntityId(entityId);
		if (normalizedKind != DeviceEntityConstants.Kinds.Device || normalizedEntityId == null)
			return null;

		Device device = await new DeviceLogic().GetByIdAsync(normalizedEntityId, cancellationToken);
		return ToProjectedEntity(device);
	}

	public async Task<List<Entity>> GetByKindsAsync(IReadOnlyCollection<string> kinds, CancellationToken cancellationToken = default)
	{
		List<string> normalizedKinds = EntityLogic.NormalizeEntityKinds(kinds);
		if (!normalizedKinds.Contains(DeviceEntityConstants.Kinds.Device))
			return [];

		List<Device> devices = await new DeviceLogic().GetAllAsync(cancellationToken);
		List<Entity> entities = [];

		foreach (Device device in devices)
		{
			Entity entity = ToProjectedEntity(device);
			if (entity != null)
				entities.Add(entity);
		}

		return entities;
	}

	private static Entity ToProjectedEntity(Device device)
	{
		string deviceId = DeviceLogic.NormalizeDeviceId(device?.Id);
		if (device == null || deviceId == null)
			return null;

		return new Entity()
		{
			Id = deviceId,
			EntityKind = DeviceEntityConstants.Kinds.Device,
			Name = device.Name,
			Datas =
			{
				["DisplayName"] = new EntityData()
				{
					SimpleType = "System.String",
					SimpleValue = device.Name,
					Category = DeviceEntityConstants.Categories.Identity
				}
			}
		};
	}
}
```

### Registry Wiring Example

```csharp
EntityProjectionRepositoryRegistry registry = EntityProjectionRepositoryRegistry.CreateDefault();
registry.Register(new DeviceProjectedEntityRepository());

EntityLogic entityLogic = new EntityLogic(registry);
```
