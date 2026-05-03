using Home.Common.Model;
using MaNoir.Core.DataAccess;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed class DiscoveredDeviceMongoOperations
{
    private readonly MongoDbHelper _mongo;
    private readonly IMongoCollection<DiscoveredDevice> _collection;

    public DiscoveredDeviceMongoOperations()
    {
        _mongo = new MongoDbHelper();
        _collection = _mongo.GetCollection<DiscoveredDevice>();
    }

    public Task<DiscoveredDevice> GetByIdAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("The discovered device identifier cannot be empty.", nameof(deviceId));

        return _collection.Find(device => device.Id == deviceId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<DiscoveredDevice> GetByInternalNameAndAgentAsync(string deviceInternalName, string deviceAgentId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceInternalName))
            throw new ArgumentException("The discovered device internal name cannot be empty.", nameof(deviceInternalName));
        if (string.IsNullOrWhiteSpace(deviceAgentId))
            throw new ArgumentException("The discovered device agent identifier cannot be empty.", nameof(deviceAgentId));

        return _collection.Find(device => device.DeviceInternalName == deviceInternalName && device.DeviceAgentId == deviceAgentId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task InsertAsync(DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrWhiteSpace(device.Id))
            throw new ArgumentException("The discovered device identifier cannot be empty.", nameof(device));

        return _collection.InsertOneAsync(device, null, cancellationToken);
    }

    public Task SaveAsync(DiscoveredDevice device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrWhiteSpace(device.Id))
            throw new ArgumentException("The discovered device identifier cannot be empty.", nameof(device));

        return _collection.ReplaceOneAsync(current => current.Id == device.Id, device, new ReplaceOptions() { IsUpsert = true }, cancellationToken);
    }

    public Task<List<DiscoveredDevice>> FindAsync(string kind, string agentId, string meshId, CancellationToken cancellationToken = default)
    {
        FilterDefinitionBuilder<DiscoveredDevice> filterBuilder = Builders<DiscoveredDevice>.Filter;
        FilterDefinition<DiscoveredDevice> filter = filterBuilder.Eq(device => device.MeshId, meshId);

        if (!string.IsNullOrWhiteSpace(kind))
            filter &= filterBuilder.Eq(device => device.DeviceKind, kind);

        if (!string.IsNullOrWhiteSpace(agentId))
            filter &= filterBuilder.Eq(device => device.DeviceAgentId, agentId);

        return _collection.Find(filter).ToListAsync(cancellationToken);
    }

    public Task DeleteOlderThanAsync(string meshId, DateTimeOffset threshold, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(meshId))
            throw new ArgumentException("The mesh identifier cannot be empty.", nameof(meshId));

        return _collection.DeleteManyAsync(device => device.MeshId == meshId && device.DiscoveryDate < threshold, cancellationToken);
    }
}