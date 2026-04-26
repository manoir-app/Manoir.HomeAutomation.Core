---
name: register-plugin-catalog
description: 'Prepare or implement a real MaNoir plugin catalog registration. Use when wiring a plugin to RegisterPlugin, creating a PluginDescriptor provider, publishing contributions and access zones together, declaring repository URL dependencies, migrating a legacy Core capability into a plugin catalog, or validating transitive rights references and dependency-cycle rules.'
argument-hint: 'Describe the plugin to register: plugin id, repository URL, parent repository URLs, contributions to publish, access zones to publish, and where startup registration should happen.'
user-invocable: true
---

# Register Plugin Catalog

Use this skill when a real MaNoir plugin must publish its catalog through the current Core plugin registration model.

## Goal

Publish one coherent plugin descriptor that contains:

- plugin metadata;
- contributions;
- access zones;
- repository URL identity;
- parent repository dependencies.

The target model is the current `RegisterPlugin(...)` startup flow, not ad hoc publication of contributions and rights through separate calls.

## Use This Skill For

Use this skill when you need to:

- connect a real plugin to `RegisterPlugin(...)`;
- extract a plugin descriptor provider similar to the Core provider;
- migrate legacy Core capabilities into one plugin catalog;
- declare `RepositoryUrl` and `DependencyRepositoryUrls` for cross-plugin rights inheritance;
- validate that Admin UI contributions reference only local or dependency-published access zones;
- validate that repository dependency graphs are acyclic.

## Do Not Use This Skill For

Do not use this skill when:

- you only need to change one existing contribution label or URL;
- you are designing a brand-new repo placement and the component family is still unclear;
- you need a global always-on coding rule rather than a plugin-registration workflow.

Use the `component-placement` skill first when plugin ownership or repo placement is still uncertain.

## Current Model

The current publication model in this repo is:

1. Build one `PluginDescriptor`.
2. Publish it through `RegisterPlugin(...)` at startup.
3. Let `PluginRegistrationLogic` persist the plugin catalog and the access-zone catalog together.
4. Let validation ensure that:
- a contribution access zone is either published by the plugin itself or by one of its repository dependencies;
- repository dependency traversal is transitive;
- dependency cycles are rejected.

## Procedure

1. Identify the real plugin boundary.
The plugin must represent a real catalog owner, not an arbitrary code folder.

2. Identify the repository URL.
Use `RepositoryUrl` as the stable parent identity for dependency traversal. Do not use plugin id as the parent relationship key.

3. Identify parent repositories.
Fill `DependencyRepositoryUrls` only with direct parent repository URLs. The runtime validation already traverses the dependency tree transitively.

4. Define the access zones first.
List every zone the plugin publishes itself. Keep them stable, lowercase, and explicit.

5. Define contributions in the same descriptor.
Each contribution belongs to the plugin catalog and must be published together with its zones.

6. Validate every `AdminUi.AccessZoneId`.
A contribution may reference:
- one zone published by the same plugin;
- one zone published by a plugin reachable through `DependencyRepositoryUrls`.
It must not reference an unrelated external zone.

7. Register the plugin at startup.
Use `app.RegisterPlugin(pluginDescriptor)` from the startup surface that owns local plugin publication.

8. Add focused tests.
At minimum, cover:
- plugin persistence;
- rights persistence;
- valid direct dependency reference;
- valid transitive dependency reference when applicable;
- invalid external reference;
- dependency cycle rejection.

## Implementation Shape

Prefer this structure:

1. Add or update a dedicated descriptor provider for the plugin.
Keep descriptor construction out of startup plumbing when the catalog is non-trivial.

2. Keep the startup call thin.
The startup surface should mostly call `RegisterPlugin(...)` with one already-built descriptor.

3. Keep descriptor data cohesive.
Do not split plugin metadata, contributions, and rights across unrelated helpers unless there is a very strong reason.

## Checklist

Before considering the work complete, verify:

- plugin id is stable and normalized;
- `RepositoryUrl` is set when dependency traversal matters;
- `DependencyRepositoryUrls` lists only direct parents;
- all published access zones are declared in the descriptor;
- all contribution `AccessZoneId` references are valid;
- no repository dependency cycle is introduced;
- targeted tests are added or updated.

## Targeted Excerpts

Use targeted patterns like these in the answer instead of referencing files from this repository.

### Plugin Descriptor Example

```csharp
PluginDescriptor descriptor = new PluginDescriptor()
{
	Id = "weather",
	Label = "Weather",
	Version = "1.0.0",
	Description = "Weather plugin.",
	Publisher = "MaNoir",
	RepositoryUrl = "https://example.net/plugins/weather",
	DependencyRepositoryUrls = ["https://example.net/plugins/core"],
	AccessZones =
	[
		new AccessZoneDefinition() { Id = "weather.forecast", Label = "Weather forecast" }
	],
	Contributions =
	[
		new ContributionDefinition()
		{
			Id = "weather.admin.pages",
			Kind = ContributionKind.AdminUiPage,
			Label = "Weather admin",
			AdminUi = new AdminUiContributionDefinitionData()
			{
				Domain = "Weather",
				AccessZoneId = "weather.forecast",
				RequiredAccessLevel = AccessLevel.Read
			}
		}
	]
};
```

### Startup Registration Example

```csharp
app.RegisterPlugin(descriptor);
```

### Validation Expectations Example

```text
- A contribution may reference a local access zone.
- A contribution may reference an access zone published by a direct or transitive repository dependency.
- A contribution must not reference an unrelated external access zone.
- The repository dependency graph must be acyclic.
```
