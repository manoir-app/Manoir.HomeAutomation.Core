using Home.Common.Model;
using MaNoir.Core.Contracts.Models.Entities;
using MaNoir.Core.Entities;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed class DeviceProjectedEntityRepository : IProjectedEntityRepository
{
    public string Source => "devices/catalog";

    public IReadOnlyCollection<string> SupportedKinds =>
    [
        DeviceEntityConstants.Kinds.MainServer,
        DeviceEntityConstants.Kinds.HomeAutomation,
        DeviceEntityConstants.Kinds.Display,
        DeviceEntityConstants.Kinds.Security,
        DeviceEntityConstants.Kinds.Network,
        DeviceEntityConstants.Kinds.MobileDevice
    ];

    public async Task<Entity> GetByIdAsync(string kind, string entityId, CancellationToken cancellationToken = default)
    {
        string normalizedKind = EntityLogic.NormalizeEntityKind(kind);
        string normalizedEntityId = EntityLogic.NormalizeEntityId(entityId);
        if (normalizedKind == null || normalizedEntityId == null)
            return null;

        if (!EntityLogic.NormalizeEntityKinds(SupportedKinds).Contains(normalizedKind))
            return null;

        Device device = await new DeviceLogic().GetByIdAsync(normalizedEntityId, cancellationToken);
        Entity projectedEntity = CreateProjectedEntity(device);
        if (projectedEntity == null || !string.Equals(projectedEntity.EntityKind, normalizedKind, System.StringComparison.OrdinalIgnoreCase))
            return null;

        return projectedEntity;
    }

    public async Task<List<Entity>> GetByKindsAsync(IReadOnlyCollection<string> kinds, CancellationToken cancellationToken = default)
    {
        List<string> normalizedKinds = EntityLogic.NormalizeEntityKinds(kinds);
        if (normalizedKinds.Count == 0)
            return [];

        List<string> supportedKinds = EntityLogic.NormalizeEntityKinds(SupportedKinds);
        if (!normalizedKinds.Exists(kind => supportedKinds.Contains(kind)))
            return [];

        List<Device> devices = await new DeviceLogic().GetAllAsync(cancellationToken: cancellationToken);
        List<Entity> entities = [];

        foreach (Device device in devices)
        {
            Entity projectedEntity = CreateProjectedEntity(device);
            if (projectedEntity == null)
                continue;

            if (!normalizedKinds.Contains(projectedEntity.EntityKind))
                continue;

            entities.Add(projectedEntity);
        }

        return entities;
    }

    public static Entity CreateProjectedEntity(Device device)
    {
        string normalizedDeviceId = DeviceLogic.NormalizeDeviceId(device?.Id);
        string entityKind = DeviceLogic.GetEntityKind(device);
        if (device == null || normalizedDeviceId == null || entityKind == null)
            return null;

        Entity entity = new Entity()
        {
            Id = normalizedDeviceId,
            EntityKind = entityKind,
            Name = ResolveDisplayName(device),
            MeshId = DeviceLogic.NormalizeMeshId(device.MeshId)
        };

        if (!string.IsNullOrWhiteSpace(device.DeviceKind))
            entity.Roles.Add(device.DeviceKind);

        if (device.DeviceRoles != null)
        {
            foreach (string role in device.DeviceRoles)
            {
                if (!string.IsNullOrWhiteSpace(role))
                    entity.Roles.Add(string.Concat(device.DeviceKind, ":", role));
            }
        }

        if (device.Datas != null)
        {
            foreach (DeviceData data in device.Datas)
            {
                if (data == null || string.IsNullOrWhiteSpace(data.Name))
                    continue;

                entity.Datas[data.Name] = CreateData(data.Value, DeviceEntityConstants.Categories.Default);
            }
        }

        return entity;
    }

    private static string ResolveDisplayName(Device device)
    {
        if (!string.IsNullOrWhiteSpace(device?.DeviceGivenName))
            return device.DeviceGivenName;

        if (!string.IsNullOrWhiteSpace(device?.DeviceInternalName))
            return device.DeviceInternalName;

        return DeviceLogic.NormalizeDeviceId(device?.Id);
    }

    private static EntityData CreateData(string value, string category)
    {
        if (value == null)
            return null;

        return new EntityData()
        {
            SimpleType = "System.String",
            SimpleValue = value,
            Category = category
        };
    }
}