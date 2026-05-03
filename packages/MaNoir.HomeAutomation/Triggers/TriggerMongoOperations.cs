using Home.Common.Model;
using MaNoir.Core.DataAccess;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed class TriggerMongoOperations
{
    private readonly MongoDbHelper _mongo;
    private readonly IMongoCollection<Trigger> _collection;

    public TriggerMongoOperations()
    {
        _mongo = new MongoDbHelper();
        _collection = _mongo.GetCollection<Trigger>();
    }

    public Task<List<Trigger>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _collection.Find(Builders<Trigger>.Filter.Empty).ToListAsync(cancellationToken);
    }

    public Task<Trigger> GetByIdAsync(string triggerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(triggerId))
            throw new ArgumentException("The trigger identifier cannot be empty.", nameof(triggerId));

        return _collection.Find(trigger => trigger.Id == triggerId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task InsertAsync(Trigger trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (string.IsNullOrWhiteSpace(trigger.Id))
            throw new ArgumentException("The trigger identifier cannot be empty.", nameof(trigger));

        return _collection.InsertOneAsync(trigger, null, cancellationToken);
    }

    public Task SaveAsync(Trigger trigger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trigger);
        if (string.IsNullOrWhiteSpace(trigger.Id))
            throw new ArgumentException("The trigger identifier cannot be empty.", nameof(trigger));

        return _collection.ReplaceOneAsync(current => current.Id == trigger.Id, trigger, new ReplaceOptions() { IsUpsert = true }, cancellationToken);
    }

    public Task DeleteAsync(string triggerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(triggerId))
            throw new ArgumentException("The trigger identifier cannot be empty.", nameof(triggerId));

        return _collection.DeleteOneAsync(trigger => trigger.Id == triggerId, cancellationToken);
    }
}