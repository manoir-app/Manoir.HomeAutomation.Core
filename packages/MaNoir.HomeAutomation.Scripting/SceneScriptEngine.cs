using MaNoir.HomeAutomation.Devices;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Jint;

namespace MaNoir.HomeAutomation.Scripting;

public sealed class SceneScriptEngine
{
    private readonly TimeSpan _timeout;
    private readonly long _memoryLimit;
    private readonly int _maxStatements;

    public SceneScriptEngine(
        TimeSpan? timeout = null,
        long memoryLimit = 4_000_000,
        int maxStatements = 100_000)
    {
        if (timeout is not null && timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));
        if (memoryLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(memoryLimit));
        if (maxStatements <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxStatements));

        _timeout = timeout ?? TimeSpan.FromSeconds(5);
        _memoryLimit = memoryLimit;
        _maxStatements = maxStatements;
    }

    public void Execute(
        string script,
        IEnumerable<IDevice> devices,
        IReadOnlyDictionary<string, string> parameters = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(script))
            throw new ArgumentException("A script is required.", nameof(script));

        Engine engine = new Engine(options =>
        {
            options.TimeoutInterval(_timeout);
            options.LimitMemory(_memoryLimit);
            options.MaxStatements(_maxStatements);
            options.CancellationToken(cancellationToken);
        });

        ScriptHome home = new(devices ?? Enumerable.Empty<IDevice>(), parameters);
        engine.SetValue("home", home);
        engine.Execute(script);
    }

    private sealed class ScriptHome
    {
        public ScriptHome(IEnumerable<IDevice> devices, IReadOnlyDictionary<string, string> parameters)
        {
            Devices = new ScriptDevices(devices);
            Parameters = parameters == null
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(parameters, StringComparer.OrdinalIgnoreCase);
        }

        public ScriptDevices Devices { get; }

        public IReadOnlyDictionary<string, string> Parameters { get; }
    }

    private sealed class ScriptDevices
    {
        private readonly IReadOnlyList<IDevice> _devices;

        public ScriptDevices(IEnumerable<IDevice> devices)
        {
            _devices = devices.Where(device => device != null).ToArray();
        }

        public ScriptDevice Find(string id)
        {
            IDevice device = _devices.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.InternalId, id, StringComparison.OrdinalIgnoreCase));
            return device == null ? null : new ScriptDevice(device);
        }

        public IReadOnlyList<ScriptDevice> All()
        {
            return _devices.Select(device => new ScriptDevice(device)).ToArray();
        }
    }

    private sealed class ScriptDevice
    {
        private readonly IDevice _device;

        public ScriptDevice(IDevice device)
        {
            _device = device;
            Elements = (device.Elements ?? Array.Empty<IDeviceElement>())
                .Select(element => new ScriptElement(element))
                .ToArray();
        }

        public string Id => _device.Id;

        public string InternalId => _device.InternalId;

        public IReadOnlyList<ScriptElement> Elements { get; }
    }

    private sealed class ScriptElement
    {
        private readonly IDeviceElement _element;

        public ScriptElement(IDeviceElement element)
        {
            _element = element;
        }

        public string Name => _element.Name;

        public bool Has(string capability)
        {
            return FindCapability(capability) != null;
        }

        public void SetSwitch(bool isOn)
        {
            IToggleSwitchDevice toggle = FindCapability<IToggleSwitchDevice>();
            if (toggle == null)
                throw new InvalidOperationException($"Element '{Name}' does not expose a switch.");

            toggle.SetSwitchStateAsync(isOn).GetAwaiter().GetResult();
        }

        public void SetIntensity(double intensityPercent)
        {
            if (intensityPercent is < 0D or > 100D)
                throw new ArgumentOutOfRangeException(nameof(intensityPercent));

            IIntensityGradientDevice dimmer = FindCapability<IIntensityGradientDevice>();
            if (dimmer == null)
                throw new InvalidOperationException($"Element '{Name}' does not expose intensity.");

            dimmer.SetIntensityAsync((decimal)intensityPercent).GetAwaiter().GetResult();
        }

        public void SetColorRgb(string value)
        {
            if (!TryParseRgb(value, out DeviceColor.Rgb color))
                throw new ArgumentException("An RGB color must use #RRGGBB format.", nameof(value));

            IChromaticColorDevice chromatic = FindCapability<IChromaticColorDevice>();
            if (chromatic == null)
                throw new InvalidOperationException($"Element '{Name}' does not expose color.");

            chromatic.SetColorAsync(color).GetAwaiter().GetResult();
        }

        private IDeviceCapability FindCapability(string capability)
        {
            return capability?.Trim().ToLowerInvariant() switch
            {
                "switch" => FindCapability<IToggleSwitchDevice>(),
                "intensity" => FindCapability<IIntensityGradientDevice>(),
                "color" => FindCapability<IChromaticColorDevice>(),
                _ => null
            };
        }

        private T FindCapability<T>() where T : class, IDeviceCapability
        {
            return (_element.Capabilities ?? Array.Empty<IDeviceCapability>()).OfType<T>().FirstOrDefault();
        }

        private static bool TryParseRgb(string value, out DeviceColor.Rgb color)
        {
            color = null;
            string hex = value?.Trim().TrimStart('#');
            if (hex?.Length != 6
                || !byte.TryParse(hex[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red)
                || !byte.TryParse(hex[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green)
                || !byte.TryParse(hex[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue))
            {
                return false;
            }

            color = new DeviceColor.Rgb(red, green, blue);
            return true;
        }
    }
}