using Home.Common.Model;
using MaNoir.Core.DataAccess;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed class SceneGroupMongoOperations
{
    private readonly MongoDbHelper _mongo;
    private readonly IMongoCollection<SceneGroup> _collection;

    public SceneGroupMongoOperations()
    {
        _mongo = new MongoDbHelper();
        _collection = _mongo.Database.GetCollection<SceneGroup>("SceneGroups");
    }

    public Task<List<SceneGroup>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _collection.Find(Builders<SceneGroup>.Filter.Empty)
            .Sort(Builders<SceneGroup>.Sort.Ascending(group => group.Order))
            .ToListAsync(cancellationToken);
    }

    public Task<SceneGroup> GetByIdAsync(string groupId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            throw new ArgumentException("The scene group identifier cannot be empty.", nameof(groupId));

        return _collection.Find(group => group.Id == groupId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task InsertAsync(SceneGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (string.IsNullOrWhiteSpace(group.Id))
            throw new ArgumentException("The scene group identifier cannot be empty.", nameof(group));

        return _collection.InsertOneAsync(group, null, cancellationToken);
    }

    public Task SaveAsync(SceneGroup group, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(group);
        if (string.IsNullOrWhiteSpace(group.Id))
            throw new ArgumentException("The scene group identifier cannot be empty.", nameof(group));

        return _collection.ReplaceOneAsync(current => current.Id == group.Id, group, new ReplaceOptions() { IsUpsert = true }, cancellationToken);
    }

    public Task DeleteAsync(string groupId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            throw new ArgumentException("The scene group identifier cannot be empty.", nameof(groupId));

        return _collection.DeleteOneAsync(group => group.Id == groupId, cancellationToken);
    }
}