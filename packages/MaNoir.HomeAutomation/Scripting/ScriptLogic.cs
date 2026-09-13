using Home.Common.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Scripting;

public sealed class ScriptLogic
{
    private readonly ScriptMongoOperations _operations = new();

    public Task<List<ScriptDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _operations.GetAllAsync(cancellationToken);
    }

    public Task<ScriptDefinition> GetByIdAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        string normalizedId = NormalizeId(scriptId);
        return normalizedId == null ? Task.FromResult<ScriptDefinition>(null) : _operations.GetByIdAsync(normalizedId, cancellationToken);
    }

    public async Task<ScriptDefinition> UpsertAsync(ScriptDefinition script, CancellationToken cancellationToken = default)
    {
        ScriptDefinition prepared = Prepare(script);
        if (prepared == null)
            return null;

        if (prepared.Id == null)
            prepared.Id = Guid.NewGuid().ToString("D").ToLowerInvariant();

        ScriptDefinition existing = await _operations.GetByIdAsync(prepared.Id, cancellationToken);
        if (existing == null)
            await _operations.InsertAsync(prepared, cancellationToken);
        else
            await _operations.SaveAsync(prepared, cancellationToken);

        return await _operations.GetByIdAsync(prepared.Id, cancellationToken);
    }

    public async Task<bool> DeleteAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        string normalizedId = NormalizeId(scriptId);
        if (normalizedId == null)
            return true;

        await _operations.DeleteAsync(normalizedId, cancellationToken);
        return true;
    }

    public static string NormalizeId(string scriptId)
    {
        return string.IsNullOrWhiteSpace(scriptId) ? null : scriptId.Trim().ToLowerInvariant();
    }

    public static ScriptDefinition Prepare(ScriptDefinition script)
    {
        if (script == null || string.IsNullOrWhiteSpace(script.Content))
            return null;

        script.Id = NormalizeId(script.Id);
        script.Label = string.IsNullOrWhiteSpace(script.Label) ? script.Id : script.Label.Trim();
        script.Description = script.Description?.Trim();
        script.Content = script.Content.Trim();
        script.Tags = (script.Tags ?? new List<string>())
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Select(tag => tag.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        return script;
    }
}