using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Home.Common.Messages;

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

public interface IDisplayDevice : IDeviceCapability
{
    Task SetDisplayTextAsync(string text, DeviceColor color = null, string icon = null, CancellationToken cancellationToken = default);

    Task ClearDisplayAsync(CancellationToken cancellationToken = default);
}

public interface INotificationDevice : IDeviceCapability
{
    Task SendNotificationAsync(RuntimeDeviceNotification notification, CancellationToken cancellationToken = default);
}

public sealed record RuntimeDeviceNotification(
    string Text,
    DeviceColor Color = null,
    string Icon = null,
    int? DurationSeconds = null,
    int? Repeat = null,
    bool? Hold = null);

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

    public Hsv ToHsv()
    {
        if (this is Hsv hsv)
            return hsv;
        if (this is not Rgb rgb)
            throw new InvalidOperationException($"Cannot convert {Model} to HSV.");

        double red = rgb.Red / 255D;
        double green = rgb.Green / 255D;
        double blue = rgb.Blue / 255D;
        double maximum = Math.Max(red, Math.Max(green, blue));
        double minimum = Math.Min(red, Math.Min(green, blue));
        double difference = maximum - minimum;
        double hue = difference <= double.Epsilon
            ? 0D
            : maximum == red
                ? 60D * ((green - blue) / difference % 6D)
                : maximum == green
                    ? 60D * ((blue - red) / difference + 2D)
                    : 60D * ((red - green) / difference + 4D);
        if (hue < 0D)
            hue += 360D;
        return new Hsv(hue, maximum <= double.Epsilon ? 0D : difference / maximum, maximum);
    }

    public Xy ToXy(IReadOnlyList<(double X, double Y)> gamut = null)
    {
        if (this is Xy xy)
            return xy;
        if (this is not Rgb rgb)
            throw new InvalidOperationException($"Cannot convert {Model} to XY.");

        double red = ToLinear(rgb.Red / 255D);
        double green = ToLinear(rgb.Green / 255D);
        double blue = ToLinear(rgb.Blue / 255D);
        double x = red * 0.664511D + green * 0.154324D + blue * 0.162028D;
        double y = red * 0.283881D + green * 0.668433D + blue * 0.047685D;
        double z = red * 0.000088D + green * 0.072310D + blue * 0.986039D;
        double total = x + y + z;
        if (total <= double.Epsilon)
            return new Xy(0D, 0D);

        (double clippedX, double clippedY) = ClipToGamut(x / total, y / total, gamut);
        return new Xy(clippedX, clippedY);
    }

    private static double ToLinear(double component)
    {
        return component <= 0.04045D
            ? component / 12.92D
            : Math.Pow((component + 0.055D) / 1.055D, 2.4D);
    }

    private static (double X, double Y) ClipToGamut(double x, double y, IReadOnlyList<(double X, double Y)> gamut)
    {
        if (gamut == null || gamut.Count < 3 || IsInsideTriangle(x, y, gamut[0], gamut[1], gamut[2]))
            return (x, y);

        (double X, double Y, double Distance) closest = ClosestPoint(x, y, gamut[0], gamut[1]);
        (double X, double Y, double Distance) candidate = ClosestPoint(x, y, gamut[1], gamut[2]);
        if (candidate.Distance < closest.Distance)
            closest = candidate;
        candidate = ClosestPoint(x, y, gamut[2], gamut[0]);
        if (candidate.Distance < closest.Distance)
            closest = candidate;
        return (closest.X, closest.Y);
    }

    private static bool IsInsideTriangle(double x, double y, (double X, double Y) first, (double X, double Y) second, (double X, double Y) third)
    {
        double firstCross = Cross(second.X - first.X, second.Y - first.Y, x - first.X, y - first.Y);
        double secondCross = Cross(third.X - second.X, third.Y - second.Y, x - second.X, y - second.Y);
        double thirdCross = Cross(first.X - third.X, first.Y - third.Y, x - third.X, y - third.Y);
        return (firstCross >= 0D && secondCross >= 0D && thirdCross >= 0D)
            || (firstCross <= 0D && secondCross <= 0D && thirdCross <= 0D);
    }

    private static (double X, double Y, double Distance) ClosestPoint(double x, double y, (double X, double Y) start, (double X, double Y) end)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double lengthSquared = deltaX * deltaX + deltaY * deltaY;
        double factor = lengthSquared <= double.Epsilon
            ? 0D
            : Math.Clamp(((x - start.X) * deltaX + (y - start.Y) * deltaY) / lengthSquared, 0D, 1D);
        double closestX = start.X + factor * deltaX;
        double closestY = start.Y + factor * deltaY;
        double distanceX = x - closestX;
        double distanceY = y - closestY;
        return (closestX, closestY, distanceX * distanceX + distanceY * distanceY);
    }

    private static double Cross(double firstX, double firstY, double secondX, double secondY)
    {
        return firstX * secondY - firstY * secondX;
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

public sealed class RuntimeDeviceStateChangedEventArgs : EventArgs
{
    public RuntimeDeviceStateChangedEventArgs(
        IDevice device,
        string platform,
        string role,
        string mainStatus,
        IReadOnlyList<DeviceStateChangedMessage.DeviceStateValue> changes)
    {
        Device = device ?? throw new ArgumentNullException(nameof(device));
        Platform = platform;
        Role = role;
        MainStatus = mainStatus;
        Changes = changes ?? Array.Empty<DeviceStateChangedMessage.DeviceStateValue>();
    }

    public IDevice Device { get; }

    public string Platform { get; }

    public string Role { get; }

    public string MainStatus { get; }

    public IReadOnlyList<DeviceStateChangedMessage.DeviceStateValue> Changes { get; }
}

public interface IRuntimeDeviceEvents
{
    event EventHandler<RuntimeDeviceStateChangedEventArgs> StateChanged;
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

    public event EventHandler<RuntimeDeviceChangeSet> DeviceAdded;

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
        RuntimeDeviceChangeSet changes = new(sourceId, added, updated, removed);
        if (added.Count > 0)
            DeviceAdded?.Invoke(this, changes);

        return changes;
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