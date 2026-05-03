using Home.Common.Model;
using MaNoir.Core.DataAccess;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed class SceneMongoOperations
{
    private readonly MongoDbHelper _mongo;
    private readonly IMongoCollection<Scene> _collection;

    public SceneMongoOperations()
    {
        _mongo = new MongoDbHelper();
        _collection = _mongo.Database.GetCollection<Scene>("Scenes");
    }

    public Task<List<Scene>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _collection.Find(Builders<Scene>.Filter.Empty)
            .Sort(Builders<Scene>.Sort.Ascending(scene => scene.OrderInGroup))
            .ToListAsync(cancellationToken);
    }

    public Task<List<Scene>> GetByGroupIdAsync(string groupId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            throw new ArgumentException("The scene group identifier cannot be empty.", nameof(groupId));

        return _collection.Find(scene => scene.GroupId == groupId)
            .Sort(Builders<Scene>.Sort.Ascending(scene => scene.OrderInGroup))
            .ToListAsync(cancellationToken);
    }

    public Task<Scene> GetByIdAsync(string sceneId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            throw new ArgumentException("The scene identifier cannot be empty.", nameof(sceneId));

        return _collection.Find(scene => scene.Id == sceneId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task InsertAsync(Scene scene, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (string.IsNullOrWhiteSpace(scene.Id))
            throw new ArgumentException("The scene identifier cannot be empty.", nameof(scene));

        return _collection.InsertOneAsync(scene, null, cancellationToken);
    }

    public Task SaveAsync(Scene scene, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        if (string.IsNullOrWhiteSpace(scene.Id))
            throw new ArgumentException("The scene identifier cannot be empty.", nameof(scene));

        return _collection.ReplaceOneAsync(current => current.Id == scene.Id, scene, new ReplaceOptions() { IsUpsert = true }, cancellationToken);
    }

    public Task DeleteAsync(string sceneId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
            throw new ArgumentException("The scene identifier cannot be empty.", nameof(sceneId));

        return _collection.DeleteOneAsync(scene => scene.Id == sceneId, cancellationToken);
    }

    public Task DeleteByGroupIdAsync(string groupId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(groupId))
            throw new ArgumentException("The scene group identifier cannot be empty.", nameof(groupId));

        return _collection.DeleteManyAsync(scene => scene.GroupId == groupId, cancellationToken);
    }
}