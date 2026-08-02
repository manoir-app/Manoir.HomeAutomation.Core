using Home.Common.Model;
using MaNoir.Core.DataAccess;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed class DeviceMongoOperations
{
    private readonly MongoDbHelper _mongo;
    private readonly IMongoCollection<Device> _collection;

    public DeviceMongoOperations()
    {
        _mongo = new MongoDbHelper();
        _collection = _mongo.GetCollection<Device>();
    }

    public Task<Device> GetByIdAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("The device identifier cannot be empty.", nameof(deviceId));

        return _collection.Find(device => device.Id == deviceId).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Device> GetByInternalNameAsync(string deviceInternalName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceInternalName))
            throw new ArgumentException("The device internal name cannot be empty.", nameof(deviceInternalName));

        return _collection.Find(device => device.DeviceInternalName == deviceInternalName).FirstOrDefaultAsync(cancellationToken);
    }

    public Task<Device> GetByInternalNameAndPlatformAsync(string deviceInternalName, string devicePlatform, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(deviceInternalName))
            throw new ArgumentException("The device internal name cannot be empty.", nameof(deviceInternalName));
        if (string.IsNullOrWhiteSpace(devicePlatform))
            throw new ArgumentException("The device platform cannot be empty.", nameof(devicePlatform));

        return _collection.Find(device => device.DeviceInternalName == deviceInternalName && device.DevicePlatform == devicePlatform).FirstOrDefaultAsync(cancellationToken);
    }

    public Task InsertAsync(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrWhiteSpace(device.Id))
            throw new ArgumentException("The device identifier cannot be empty.", nameof(device));

        return _collection.InsertOneAsync(device, null, cancellationToken);
    }

    public Task SaveAsync(Device device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrWhiteSpace(device.Id))
            throw new ArgumentException("The device identifier cannot be empty.", nameof(device));

        return _collection.ReplaceOneAsync(current => current.Id == device.Id, device, new ReplaceOptions() { IsUpsert = true }, cancellationToken);
    }

    public Task<List<Device>> FindAsync(string agentId, string kind, string role, string id, bool returnIgnored, string meshId, CancellationToken cancellationToken = default)
    {
        FilterDefinitionBuilder<Device> filterBuilder = Builders<Device>.Filter;
        FilterDefinition<Device> filter = filterBuilder.Empty;

        if (!returnIgnored)
            filter &= filterBuilder.Eq("IsIgnored", false);

        if (!string.IsNullOrEmpty(kind))
            filter &= filterBuilder.Eq("DeviceKind", kind);

        if (!string.IsNullOrEmpty(agentId))
            filter &= filterBuilder.Eq("DeviceAgentId", agentId);

        if (!string.IsNullOrEmpty(id))
            filter &= filterBuilder.Eq("Id", id);

        if (!string.IsNullOrEmpty(role))
            filter &= filterBuilder.AnyEq("DeviceRoles", role);

        filter &= filterBuilder.Eq("MeshId", meshId);
        return _collection.Find(filter).ToListAsync(cancellationToken);
    }
}