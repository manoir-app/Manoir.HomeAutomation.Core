using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices;

public interface IDeviceCapability
{
}

public interface IToggleSwitchDevice : IDeviceCapability
{
    bool? IsOn { get; }
    Task SetSwitchStateAsync(bool isOn, CancellationToken cancellationToken = default);
}

public interface IIntensityGradientDevice : IDeviceCapability
{
    decimal? IntensityPercent { get; }
    Task SetIntensityAsync(decimal intensityPercent, CancellationToken cancellationToken = default);
}

public interface ICoverDevice : IDeviceCapability
{
    string State { get; }
    Task OpenAsync(CancellationToken cancellationToken = default);
    Task CloseAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public interface IPositionableCoverDevice : ICoverDevice
{
    decimal? PositionPercent { get; }
    bool SupportsPosition { get; }
    Task SetPositionAsync(decimal positionPercent, CancellationToken cancellationToken = default);
}

public interface IShutterDevice : IPositionableCoverDevice
{
}

public interface IChromaticColorDevice : IDeviceCapability
{
    IReadOnlyList<ColorModel> SupportedColorModels { get; }

    DeviceColor CurrentColor { get; }

    Task SetColorAsync(DeviceColor color, CancellationToken cancellationToken = default);
}

public interface IColorTemperatureDevice : IDeviceCapability
{
    int? CurrentKelvin { get; }

    int MinimumKelvin { get; }

    int MaximumKelvin { get; }

    Task SetColorTemperatureAsync(int kelvin, CancellationToken cancellationToken = default);
}

public interface ISensorDevice : IDeviceCapability
{
    IReadOnlyDictionary<string, RuntimeSensorReading> Readings { get; }
}

public sealed record RuntimeSensorDefinition(string Type, string Label, string CanonicalUnit);

public sealed record RuntimeSensorReading(RuntimeSensorDefinition Definition, object Value);

public interface IRuntimeActionDevice : IDeviceCapability
{
    IReadOnlyList<RuntimeDeviceAction> AvailableActions { get; }

    RuntimeDeviceAction LastAction { get; }
}

public interface IRuntimeAvailabilityDevice : IDeviceCapability
{
    bool? IsAvailable { get; }

    DateTimeOffset? LastSeenUtc { get; }
}

public sealed record RuntimeDeviceAction(
    string Kind,
    string Action,
    string RawAction,
    IReadOnlyDictionary<string, string> Attributes);

public enum ColorModel
{
    Rgb,
    Rgbw,
    Hsv,
    Xy
}

public abstract record DeviceColor(ColorModel Model)
{
    public sealed record Rgb(byte Red, byte Green, byte Blue) : DeviceColor(ColorModel.Rgb);

    public sealed record Rgbw(byte Red, byte Green, byte Blue, byte White) : DeviceColor(ColorModel.Rgbw);

    public sealed record Hsv : DeviceColor
    {
        public Hsv(double hueDegrees, double saturation, double value)
            : base(ColorModel.Hsv)
        {
            if (hueDegrees < 0D || hueDegrees >= 360D || double.IsNaN(hueDegrees) || double.IsInfinity(hueDegrees))
                throw new ArgumentOutOfRangeException(nameof(hueDegrees));
            if (saturation is < 0D or > 1D || double.IsNaN(saturation))
                throw new ArgumentOutOfRangeException(nameof(saturation));
            if (value is < 0D or > 1D || double.IsNaN(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            HueDegrees = hueDegrees;
            Saturation = saturation;
            Value = value;
        }

        public double HueDegrees { get; }
        public double Saturation { get; }
        public double Value { get; }
    }

    public sealed record Xy : DeviceColor
    {
        public Xy(double x, double y)
            : base(ColorModel.Xy)
        {
            if (x is < 0D or > 1D || double.IsNaN(x) || double.IsInfinity(x))
                throw new ArgumentOutOfRangeException(nameof(x));
            if (y is < 0D or > 1D || double.IsNaN(y) || double.IsInfinity(y))
                throw new ArgumentOutOfRangeException(nameof(y));

            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }
    }

}

public interface IDeviceElement
{
    string Name { get; }

    IReadOnlyList<IDeviceCapability> Capabilities { get; }
}

public interface IDevice
{
    string Id { get; }

    string InternalId => Id;

    IReadOnlyList<IDeviceCapability> Capabilities { get; }

    IReadOnlyList<IDeviceElement> Elements { get; }
}

public interface IHubDevice : IDeviceCapability
{
    IReadOnlyList<DeviceReference> ChildDevices { get; }
}

public interface IDeviceDiscoverySource
{
    string SourceId { get; }

    Task<IReadOnlyList<IDevice>> DiscoverAsync(CancellationToken cancellationToken = default);
}

public sealed record RuntimeDeviceChangeSet(
    string SourceId,
    IReadOnlyList<IDevice> Added,
    IReadOnlyList<IDevice> Updated,
    IReadOnlyList<IDevice> Removed);

public sealed class RuntimeDeviceRegistry
{
    private readonly Dictionary<string, Dictionary<string, IDevice>> _devicesBySource = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<IDevice> Devices => _devicesBySource.Values
        .SelectMany(devices => devices.Values)
        .ToArray();

    public IDevice GetById(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            return null;

        foreach (Dictionary<string, IDevice> devices in _devicesBySource.Values)
        {
            if (devices.TryGetValue(deviceId, out IDevice device))
                return device;

            device = devices.Values.FirstOrDefault(candidate => string.Equals(candidate.Id, deviceId, StringComparison.OrdinalIgnoreCase));
            if (device != null)
                return device;
        }

        return null;
    }

    public RuntimeDeviceChangeSet ApplySnapshot(string sourceId, IEnumerable<IDevice> devices)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("A discovery source identifier is required.", nameof(sourceId));

        Dictionary<string, IDevice> next = (devices ?? Enumerable.Empty<IDevice>())
            .Where(device => device != null && !string.IsNullOrWhiteSpace(device.InternalId))
            .ToDictionary(device => device.InternalId, StringComparer.OrdinalIgnoreCase);
        _devicesBySource.TryGetValue(sourceId, out Dictionary<string, IDevice> previous);
        previous ??= new Dictionary<string, IDevice>(StringComparer.OrdinalIgnoreCase);

        List<IDevice> added = next.Values.Where(device => !previous.ContainsKey(device.InternalId)).ToList();
        List<IDevice> updated = next.Values
            .Where(device => previous.TryGetValue(device.InternalId, out IDevice previousDevice)
                && !ReferenceEquals(previousDevice, device))
            .ToList();
        List<IDevice> removed = previous.Values.Where(device => !next.ContainsKey(device.InternalId)).ToList();

        _devicesBySource[sourceId] = next;
        return new RuntimeDeviceChangeSet(sourceId, added, updated, removed);
    }
}

public sealed class RuntimeDiscoveryCoordinator
{
    private readonly IReadOnlyList<IDeviceDiscoverySource> _sources;
    private readonly RuntimeDeviceRegistry _registry;
    private readonly TimeSpan _pollInterval;

    public RuntimeDiscoveryCoordinator(
        IEnumerable<IDeviceDiscoverySource> sources,
        RuntimeDeviceRegistry registry,
        TimeSpan pollInterval)
    {
        if (pollInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pollInterval));

        _sources = (sources ?? Enumerable.Empty<IDeviceDiscoverySource>()).Where(source => source != null).ToArray();
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _pollInterval = pollInterval;
    }

    public async Task<IReadOnlyList<RuntimeDeviceChangeSet>> DiscoverOnceAsync(CancellationToken cancellationToken = default)
    {
        List<RuntimeDeviceChangeSet> changes = [];
        foreach (IDeviceDiscoverySource source in _sources)
        {
            IReadOnlyList<IDevice> devices = await source.DiscoverAsync(cancellationToken);
            changes.Add(_registry.ApplySnapshot(source.SourceId, devices));
        }

        return changes;
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        await DiscoverOnceAsync(cancellationToken);
        using PeriodicTimer timer = new(_pollInterval);
        while (await timer.WaitForNextTickAsync(cancellationToken))
            await DiscoverOnceAsync(cancellationToken);
    }
}

public sealed class DeviceReference
{
    public DeviceReference(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A child device identifier is required.", nameof(id));

        Id = id;
    }

    public string Id { get; }
}

public sealed class DeviceElement : IDeviceElement
{
    public DeviceElement(string name, IEnumerable<IDeviceCapability> capabilities)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A device element name is required.", nameof(name));

        Name = name;
        Capabilities = (capabilities ?? Enumerable.Empty<IDeviceCapability>()).Where(capability => capability != null).ToArray();
    }

    public string Name { get; }

    public IReadOnlyList<IDeviceCapability> Capabilities { get; }
}

public sealed class RuntimeDevice : IDevice
{
    public RuntimeDevice(
        string id,
        IEnumerable<DeviceElement> elements,
        IEnumerable<IDeviceCapability> capabilities = null)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A device identifier is required.", nameof(id));

        Id = id;
        Elements = (elements ?? Enumerable.Empty<DeviceElement>()).Where(element => element != null).ToArray();
        Capabilities = (capabilities ?? Enumerable.Empty<IDeviceCapability>()).Where(capability => capability != null).ToArray();
    }

    public string Id { get; }

    public IReadOnlyList<IDeviceCapability> Capabilities { get; }

    public IReadOnlyList<IDeviceElement> Elements { get; }
}