using Home.Common.Model;
using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Protocols.Hue;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.Devices.Hue;

public sealed class HueLightDevice : IDevice
{
    private readonly RuntimeDevice _runtimeDevice;
    private readonly HueLight _light;
    private readonly HueAvailabilityCapability _availability;

    private HueLightDevice(
        string id,
        RuntimeDevice runtimeDevice,
        IReadOnlyList<ColorModel> supportedColorModels,
        HueLight light,
        HueAvailabilityCapability availability)
    {
        Id = id;
        _runtimeDevice = runtimeDevice;
        SupportedColorModels = supportedColorModels;
        _light = light;
        _availability = availability;
    }

    public string Id { get; }

    public IReadOnlyList<IDeviceCapability> Capabilities => _runtimeDevice.Capabilities;

    public IReadOnlyList<IDeviceElement> Elements => _runtimeDevice.Elements;

    public IReadOnlyList<ColorModel> SupportedColorModels { get; }

    public static HueLightDevice Create(
        string id,
        string bridgeAddress,
        string apiKey,
        HueLight light,
        HueProtocol protocol,
        Func<SceneStep, Device, Dictionary<string, object>> createCommand)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("A Hue light identifier is required.", nameof(id));
        if (light == null)
            throw new ArgumentNullException(nameof(light));
        if (protocol == null)
            throw new ArgumentNullException(nameof(protocol));
        if (createCommand == null)
            throw new ArgumentNullException(nameof(createCommand));

        Device legacyDevice = CreateLegacyDevice(id, light);
        HueSwitchCapability switchCapability = new(protocol, bridgeAddress, apiKey, id, legacyDevice, light, createCommand);
        List<IDeviceCapability> capabilities = [switchCapability];
        if (light.State?.Brightness.HasValue == true)
            capabilities.Add(new HueIntensityCapability(protocol, bridgeAddress, apiKey, id, legacyDevice, light, createCommand));

        HueColorCapability colorCapability = null;
        bool supportsColor = light.Capabilities?.Control?.ColorGamut?.Length >= 3
            || light.State?.Color != null
            || light.State?.Hue.HasValue == true && light.State?.Saturation.HasValue == true;
        if (supportsColor)
        {
            colorCapability = new HueColorCapability(protocol, bridgeAddress, apiKey, id, legacyDevice, light, createCommand);
            capabilities.Add(colorCapability);
        }

        if (light.Capabilities?.Control?.ColorTemperature != null)
            capabilities.Add(new HueColorTemperatureCapability(protocol, bridgeAddress, apiKey, id, legacyDevice, light, createCommand));
        HueAvailabilityCapability availability = new(light);
        List<IDeviceCapability> deviceCapabilities = [availability];
        RuntimeDevice runtimeDevice = new(
            id,
            [new DeviceElement("Light", capabilities)],
            deviceCapabilities);

        return new HueLightDevice(id, runtimeDevice, colorCapability?.SupportedColorModels ?? [], light, availability);
    }

    public void ApplyState(HueLight source)
    {
        if (source == null)
            throw new ArgumentNullException(nameof(source));

        _light.Name = source.Name;
        _light.State = source.State;
        _light.Capabilities = source.Capabilities;
        _availability.ApplyState(_light);
    }

    private static Device CreateLegacyDevice(string id, HueLight light)
    {
        List<string> capabilities = [];
        if (light.Capabilities?.Control?.ColorGamut?.Length >= 3)
        {
            capabilities.Add(Device.CapabilityColorXy);
            capabilities.Add(Device.CapabilityColorHs);
        }
        if (light.Capabilities?.Control?.ColorTemperature != null)
            capabilities.Add(Device.CapabilityColorTemperature);

        if (light.State?.Hue is int && light.State.Saturation is int)
            capabilities.Add(Device.CapabilityColorHs);

        return new Device()
        {
            Id = id,
            DeviceCapabilities = capabilities,
            ConfigurationData = JsonSerializer.Serialize(light)
        };
    }

    private abstract class HueCapability
    {
        protected HueCapability(
            HueProtocol protocol,
            string bridgeAddress,
            string apiKey,
            string lightId,
            Device legacyDevice,
            HueLight light,
            Func<SceneStep, Device, Dictionary<string, object>> createCommand)
        {
            Protocol = protocol;
            BridgeAddress = bridgeAddress;
            ApiKey = apiKey;
            LightId = lightId;
            LegacyDevice = legacyDevice;
            Light = light;
            CreateCommand = createCommand;
        }

        protected HueProtocol Protocol { get; }
        protected string BridgeAddress { get; }
        protected string ApiKey { get; }
        protected string LightId { get; }
        protected Device LegacyDevice { get; }
        protected HueLight Light { get; }
        protected Func<SceneStep, Device, Dictionary<string, object>> CreateCommand { get; }

        protected async Task SendAsync(
            SceneStep step,
            CancellationToken cancellationToken)
        {
            Dictionary<string, object> command = CreateCommand(step, LegacyDevice);
            if (command == null)
                throw new ArgumentException("The requested Hue command is not supported by this light.", nameof(step));

            using HttpResponseMessage response = await Protocol.SendLightCommandAsync(
                BridgeAddress,
                ApiKey,
                LightId[4..],
                command,
                cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    private sealed class HueAvailabilityCapability : IRuntimeAvailabilityDevice
    {
        public HueAvailabilityCapability(HueLight light)
        {
            ApplyState(light);
        }

        public bool? IsAvailable { get; private set; }

        public DateTimeOffset? LastSeenUtc { get; private set; }

        public void ApplyState(HueLight light)
        {
            IsAvailable = light?.State?.Reachable;
            LastSeenUtc = DateTimeOffset.UtcNow;
        }
    }

    private sealed class HueSwitchCapability : HueCapability, IToggleSwitchDevice
    {
        private readonly HueLight _light;

        public HueSwitchCapability(
            HueProtocol protocol,
            string bridgeAddress,
            string apiKey,
            string lightId,
            Device legacyDevice,
            HueLight light,
            Func<SceneStep, Device, Dictionary<string, object>> createCommand)
            : base(protocol, bridgeAddress, apiKey, lightId, legacyDevice, light, createCommand)
        {
            _light = light;
        }

        public bool? IsOn => _light.State?.On;

        public async Task SetSwitchStateAsync(bool isOn, CancellationToken cancellationToken = default)
        {
            await SendAsync(
                new SceneStep()
                {
                    Message = Device.HomeAutomationRoleSwitch,
                    MessageBody = isOn ? "on" : "off"
                },
                cancellationToken);
            _light.State ??= new HueLightState();
            _light.State.On = isOn;
        }
    }

    private sealed class HueIntensityCapability : HueCapability, IIntensityGradientDevice
    {
        private readonly HueLight _light;

        public HueIntensityCapability(
            HueProtocol protocol,
            string bridgeAddress,
            string apiKey,
            string lightId,
            Device legacyDevice,
            HueLight light,
            Func<SceneStep, Device, Dictionary<string, object>> createCommand)
            : base(protocol, bridgeAddress, apiKey, lightId, legacyDevice, light, createCommand)
        {
            _light = light;
        }

        public decimal? IntensityPercent => _light.State?.Brightness is int brightness
            ? Math.Round(brightness * 100M / 254M, 2)
            : null;

        public async Task SetIntensityAsync(decimal intensityPercent, CancellationToken cancellationToken = default)
        {
            if (intensityPercent is < 0M or > 100M)
                throw new ArgumentOutOfRangeException(nameof(intensityPercent));

            await SendAsync(
                new SceneStep()
                {
                    Message = Device.HomeAutomationRoleDimmer,
                    MessageBody = intensityPercent.ToString(CultureInfo.InvariantCulture)
                },
                cancellationToken);
            _light.State ??= new HueLightState();
            _light.State.Brightness = Math.Clamp(
                (int)Math.Round(intensityPercent * 254M / 100M, MidpointRounding.AwayFromZero),
                1,
                254);
        }
    }

    private sealed class HueColorCapability : HueCapability, IChromaticColorDevice
    {
        private readonly HueLight _light;
        private DeviceColor _lastRequestedColor;

        public HueColorCapability(
            HueProtocol protocol,
            string bridgeAddress,
            string apiKey,
            string lightId,
            Device legacyDevice,
            HueLight light,
            Func<SceneStep, Device, Dictionary<string, object>> createCommand)
            : base(protocol, bridgeAddress, apiKey, lightId, legacyDevice, light, createCommand)
        {
            _light = light;
            List<ColorModel> models = [];
            if (legacyDevice.DeviceCapabilities.Contains(Device.CapabilityColorXy, StringComparer.OrdinalIgnoreCase))
                models.Add(ColorModel.Xy);
            if (legacyDevice.DeviceCapabilities.Contains(Device.CapabilityColorHs, StringComparer.OrdinalIgnoreCase))
                models.Add(ColorModel.Hsv);
            SupportedColorModels = models;
        }

        public IReadOnlyList<ColorModel> SupportedColorModels { get; }

        public DeviceColor CurrentColor
        {
            get
            {
                if (_lastRequestedColor != null)
                    return _lastRequestedColor;
                if (_light.State?.Color != null)
                    return new DeviceColor.Xy(_light.State.Color.X, _light.State.Color.Y);
                if (_light.State?.Hue is int hue && _light.State.Saturation is int saturation)
                    return new DeviceColor.Hsv(hue * 360D / 65535D, saturation / 254D, 1D);
                return null;
            }
        }

        public async Task SetColorAsync(DeviceColor color, CancellationToken cancellationToken = default)
        {
            if (color == null)
                throw new ArgumentNullException(nameof(color));

            string messageBody = color switch
            {
                DeviceColor.Rgb rgb => JsonSerializer.Serialize(new { rgb = $"#{rgb.Red:X2}{rgb.Green:X2}{rgb.Blue:X2}" }),
                DeviceColor.Xy xy => JsonSerializer.Serialize(new { x = xy.X, y = xy.Y }),
                DeviceColor.Hsv hsv => JsonSerializer.Serialize(new { hue = hsv.HueDegrees, saturation = hsv.Saturation * 100D }),
                _ => throw new ArgumentException("The Hue color format is not supported.", nameof(color))
            };

            await SendAsync(
                new SceneStep()
                {
                    Message = Device.HomeAutomationRoleColorBound,
                    MessageBody = messageBody
                },
                cancellationToken);
            UpdateState(color);
        }

        private void UpdateState(DeviceColor color)
        {
            _lastRequestedColor = color;
            _light.State ??= new HueLightState();
            switch (color)
            {
                case DeviceColor.Xy xy:
                    _light.State.Color = new HueColor() { X = xy.X, Y = xy.Y };
                    break;
                case DeviceColor.Hsv hsv:
                    _light.State.Hue = (int)Math.Round(hsv.HueDegrees * 65535D / 360D, MidpointRounding.AwayFromZero);
                    _light.State.Saturation = (int)Math.Round(hsv.Saturation * 254D, MidpointRounding.AwayFromZero);
                    break;
            }
        }
    }

    private sealed class HueColorTemperatureCapability : HueCapability, IColorTemperatureDevice
    {
        private readonly HueLight _light;
        private readonly int _minimumKelvin;
        private readonly int _maximumKelvin;

        public HueColorTemperatureCapability(
            HueProtocol protocol,
            string bridgeAddress,
            string apiKey,
            string lightId,
            Device legacyDevice,
            HueLight light,
            Func<SceneStep, Device, Dictionary<string, object>> createCommand)
            : base(protocol, bridgeAddress, apiKey, lightId, legacyDevice, light, createCommand)
        {
            _light = light;
            HueColorTemperatureRange range = light.Capabilities?.Control?.ColorTemperature;
            int minimumMired = range?.Minimum > 0 ? range.Minimum : 153;
            int maximumMired = range?.Maximum >= minimumMired ? range.Maximum : 500;
            _minimumKelvin = (int)Math.Round(1_000_000D / maximumMired, MidpointRounding.AwayFromZero);
            _maximumKelvin = (int)Math.Round(1_000_000D / minimumMired, MidpointRounding.AwayFromZero);
        }

        public int? CurrentKelvin => _light.State?.ColorTemperature is int temperature
            ? (int)Math.Round(1_000_000D / temperature, MidpointRounding.AwayFromZero)
            : null;

        public int MinimumKelvin => _minimumKelvin;

        public int MaximumKelvin => _maximumKelvin;

        public async Task SetColorTemperatureAsync(int kelvin, CancellationToken cancellationToken = default)
        {
            if (kelvin < MinimumKelvin || kelvin > MaximumKelvin)
                throw new ArgumentOutOfRangeException(nameof(kelvin));

            await SendAsync(
                new SceneStep()
                {
                    Message = Device.HomeAutomationRoleColorBound,
                    MessageBody = JsonSerializer.Serialize(new { temperatureKelvin = kelvin })
                },
                cancellationToken);
            _light.State ??= new HueLightState();
            _light.State.ColorTemperature = (int)Math.Round(1_000_000D / kelvin, MidpointRounding.AwayFromZero);
        }
    }
}