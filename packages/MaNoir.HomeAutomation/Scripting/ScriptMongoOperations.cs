using Home.Common.Model;
using MaNoir.Core.DataAccess;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Scripting;

public sealed class ScriptMongoOperations
{
    private readonly IMongoCollection<ScriptDefinition> _collection;

    public ScriptMongoOperations()
    {
        _collection = new MongoDbHelper().Database.GetCollection<ScriptDefinition>("Scripts");
    }

    public Task<List<ScriptDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _collection.Find(Builders<ScriptDefinition>.Filter.Empty)
            .Sort(Builders<ScriptDefinition>.Sort.Ascending(script => script.Label))
            .ToListAsync(cancellationToken);
    }

    public Task<ScriptDefinition> GetByIdAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ArgumentException("The script identifier cannot be empty.", nameof(scriptId));

        return _collection.Find(script => script.Id == scriptId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task InsertAsync(ScriptDefinition script, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        return _collection.InsertOneAsync(script, null, cancellationToken);
    }

    public Task SaveAsync(ScriptDefinition script, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(script);
        return _collection.ReplaceOneAsync(current => current.Id == script.Id, script, new ReplaceOptions() { IsUpsert = true }, cancellationToken);
    }

    public Task DeleteAsync(string scriptId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(scriptId))
            throw new ArgumentException("The script identifier cannot be empty.", nameof(scriptId));

        return _collection.DeleteOneAsync(script => script.Id == scriptId, cancellationToken);
    }
}