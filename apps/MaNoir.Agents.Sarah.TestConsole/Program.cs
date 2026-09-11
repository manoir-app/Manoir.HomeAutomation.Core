using MaNoir.HomeAutomation.Devices.Shelly;
using MaNoir.Agents.Sarah.Awtrix;
using MaNoir.Agents.Sarah.Shelly;
using MaNoir.Agents.Sarah.Wled;
using MaNoir.Agents.Sarah.Zigbee2Mqtt;
using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Devices.Awtrix;
using MaNoir.HomeAutomation.Devices.Zigbee2Mqtt;
using MaNoir.HomeAutomation.Devices.Wled;
using MaNoir.HomeAutomation.Protocols.Shelly;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.Agents.Sarah.TestConsole;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || string.Equals(args[0], "help", StringComparison.OrdinalIgnoreCase))
        {
            PrintUsage();
            return args.Length == 0 ? 0 : 1;
        }

        string generation = args[0].ToLowerInvariant();
        Dictionary<string, string> options = ParseOptions(args);
        if (generation == "zigbee2mqtt")
            return await RunZigbee2MqttAsync(options);

        if (generation == "awtrix")
        {
            if (!options.TryGetValue("ip", out string awtrixIp))
            {
                PrintUsage();
                return 1;
            }

            return await RunAwtrixAsync(awtrixIp, options.TryGetValue("id", out string awtrixId) ? awtrixId : awtrixIp);
        }

        if (generation == "wled")
        {
            if (!options.TryGetValue("ip", out string wledIp))
            {
                PrintUsage();
                return 1;
            }

            return await RunWledAsync(wledIp, options.TryGetValue("id", out string wledId) ? wledId : wledIp);
        }

        if (!options.TryGetValue("ip", out string ipAddress))
        {
            PrintUsage();
            return 1;
        }

        string username = generation == "shelly-gen1"
            && options.TryGetValue("user", out string configuredUsername)
            ? configuredUsername
            : null;
        string password = options.TryGetValue("password", out string configuredPassword) ? configuredPassword : null;
        Console.WriteLine($"Console Shelly {generation} - IP cible: {ipAddress}");
        SetCredentials(generation, username, password);

        try
        {
            if (generation == "shelly-gen1")
            {
                ShellyGen1RuntimeService runtime = new ShellyGen1RuntimeService(NullLogger<ShellyGen1RuntimeService>.Instance);
                return await RunGen1Async(ipAddress, username, password);
            }

            if (generation == "shelly-gen2")
            {
                ShellyGen2RuntimeService runtime = new ShellyGen2RuntimeService(NullLogger<ShellyGen2RuntimeService>.Instance);
                return await RunGen2Async(ipAddress, username, password);
            }
        }
        catch (Exception exception)
        {
            PrintException("Erreur Shelly", exception);
            return 1;
        }

        PrintUsage();
        return 1;
    }

    private static async Task<bool> TryOnboardAsync(Func<Task<bool>> onboard)
    {
        try
        {
            return await onboard();
        }
        catch (Exception exception)
        {
            PrintException("Provisioning MQTT ignore", exception);
            return false;
        }
    }

    private static void PrintException(string context, Exception exception)
    {
        Console.Error.WriteLine($"{context}: {exception.GetType().Name}");
        for (Exception current = exception; current != null; current = current.InnerException)
        {
            if (current is HttpRequestException httpException && httpException.StatusCode.HasValue)
                Console.Error.WriteLine($"  HTTP {(int)httpException.StatusCode.Value} ({httpException.StatusCode.Value})");
            Console.Error.WriteLine($"  {current.Message}");
        }
    }

    private static async Task<int> RunGen1Async(string ipAddress, string username, string password)
    {
        ShellyGen1Protocol protocol = new ShellyGen1Protocol(ipAddress, username, password);
        using JsonDocument info = await protocol.GetInfoAsync(CancellationToken.None);
        using JsonDocument settings = await protocol.GetSettingsAsync(CancellationToken.None);
        string deviceId = GetString(info.RootElement, "id")
            ?? GetNestedString(settings.RootElement, "mqtt", "id")
            ?? GetNestedString(settings.RootElement, "device", "hostname")
            ?? protocol.Address;
        string mqttTopic = ShellyGen1Protocol.GetMqttTopic(settings.RootElement, deviceId);
        string model = GetString(info.RootElement, "type") ?? GetString(info.RootElement, "model") ?? string.Empty;
        string mode = GetString(settings.RootElement, "mode") ?? string.Empty;
        bool? supportsPositioning = mode.Equals("roller", StringComparison.OrdinalIgnoreCase)
            ? await protocol.GetRollerPositioningAsync(0)
            : null;
        ShellyGen1Device device = ShellyGen1Device.Create(deviceId, model, mode, protocol.SetRelayAsync, protocol.SetRollerAsync,
            (outputKind, outputIndex, isOn, brightness, token) => protocol.SetLightAsync(outputIndex, isOn, brightness, token),
            (outputIndex, isOn, brightness, color, token) => protocol.SetColorAsync(outputIndex, isOn, brightness, color, token),
            protocol.GetRollerPositioningAsync,
            supportsPositioning);
        Console.WriteLine($"Device Gen1: {deviceId}, model={model}, mode={mode}");
        PrintDeviceSummary(device, mqttTopic);
        Console.WriteLine($"Commandes directes: {GetCommandDescription(device, "relay", "roller", "light")}");
        return await RunCommandLoopAsync(async (command, index, value, token) =>
        {
            if (command is "on" or "off")
                await GetCapability<IToggleSwitchDevice>(device, index).SetSwitchStateAsync(command == "on", token);
            else if (command is "open" or "close" or "stop" or "position")
            {
                ICoverDevice cover = GetCapability<ICoverDevice>(device, index);
                if (command == "open")
                    await cover.OpenAsync(token);
                else if (command == "close")
                    await cover.CloseAsync(token);
                else if (command == "stop")
                    await cover.StopAsync(token);
                else if (decimal.TryParse(value, out decimal position))
                    await GetCapability<IPositionableCoverDevice>(device, index).SetPositionAsync(position, token);
                else
                    throw new InvalidOperationException("Position Gen1 invalide.");
            }
            else if (command == "intensity" && decimal.TryParse(value, out decimal intensity))
                await GetCapability<IIntensityGradientDevice>(device, index).SetIntensityAsync(intensity, token);
            else
                throw new InvalidOperationException("Commande Gen1 invalide.");
        });
    }

    private static TCapability GetCapability<TCapability>(ShellyGen1Device device, int index)
        where TCapability : class, IDeviceCapability
    {
        TCapability capability = device.Elements
            .Where(element => element.Capabilities.OfType<TCapability>().Any())
            .ElementAtOrDefault(index)?
            .Capabilities
            .OfType<TCapability>()
            .FirstOrDefault();
        return capability ?? throw new InvalidOperationException($"Aucune capability {typeof(TCapability).Name} a l'index {index}.");
    }

    private static string GetString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out JsonElement property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static async Task<int> RunGen2Async(string ipAddress, string username, string password)
    {
        ShellyGen2Protocol protocol = new ShellyGen2Protocol(ipAddress, password);
        using JsonDocument info = await protocol.GetInfoAsync(CancellationToken.None);
        using JsonDocument mqttConfig = await protocol.GetMqttConfigAsync(CancellationToken.None);
        using JsonDocument status = await protocol.GetStatusAsync(CancellationToken.None);
        string deviceId = GetString(info.RootElement, "id") ?? protocol.Address;
        string topicPrefix = GetString(mqttConfig.RootElement, "topic_prefix");
        if (string.IsNullOrWhiteSpace(topicPrefix))
            topicPrefix = deviceId;
        ShellyGen2Device device = ShellyGen2Device.Create(deviceId, status.RootElement, protocol);
        Console.WriteLine($"Device Gen2: {deviceId}");
        PrintDeviceSummary(device, topicPrefix);
        Console.WriteLine($"Commandes directes: {GetCommandDescription(device, "switch", "cover", "light")}");
        return await RunCommandLoopAsync(async (command, index, value, token) =>
        {
            if (command is "on" or "off")
                await GetCapability<IToggleSwitchDevice>(device, index).SetSwitchStateAsync(command == "on", token);
            else if (command is "open" or "close" or "stop" or "position")
            {
                ICoverDevice cover = GetCapability<ICoverDevice>(device, index);
                if (command == "open")
                    await cover.OpenAsync(token);
                else if (command == "close")
                    await cover.CloseAsync(token);
                else if (command == "stop")
                    await cover.StopAsync(token);
                else if (decimal.TryParse(value, out decimal position))
                    await GetCapability<IPositionableCoverDevice>(device, index).SetPositionAsync(position, token);
                else
                    throw new InvalidOperationException("Position Gen2 invalide.");
            }
            else if (command == "intensity" && decimal.TryParse(value, out decimal intensity))
                await GetCapability<IIntensityGradientDevice>(device, index).SetIntensityAsync(intensity, token);
            else
                throw new InvalidOperationException("Commande Gen2 invalide.");
        });
    }

    private static async Task<int> RunAwtrixAsync(string ipAddress, string deviceId)
    {
        AwtrixHttpClient client = new(ipAddress);
        AwtrixDevice device = AwtrixDevice.Create(
            deviceId,
            ipAddress,
            client.SetDisplayTextAsync,
            client.ClearDisplayAsync,
            client.SendNotificationAsync);

        Console.WriteLine($"Console AWTRIX - IP cible: {ipAddress}, device: {deviceId}");
        PrintDeviceSummary(device, string.Concat("awtrix/", deviceId));
        Console.WriteLine("Commandes directes: display <texte> [#RRGGBB], notify <texte> [#RRGGBB], clear, summary, quit");

        while (true)
        {
            Console.Write("awtrix> ");
            string line = Console.ReadLine();
            if (line == null || string.Equals(line.Trim(), "quit", StringComparison.OrdinalIgnoreCase))
                return 0;

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            string command = parts[0].ToLowerInvariant();
            try
            {
                if (command == "summary")
                    PrintDeviceSummary(device, string.Concat("awtrix/", deviceId));
                else if (command == "clear")
                    await ((IDisplayDevice)GetCapability<IDisplayDevice>(device)).ClearDisplayAsync();
                else if (command is "display" or "notify" && parts.Length >= 2)
                {
                    string text = string.Join(' ', parts.Skip(1).Where(part => !TryParseRgb(part, out _)));
                    DeviceColor color = parts.Skip(1).Select(part => TryParseRgb(part, out DeviceColor.Rgb parsed) ? parsed : null).FirstOrDefault();
                    if (string.IsNullOrWhiteSpace(text))
                        throw new InvalidOperationException("Le texte est obligatoire.");

                    if (command == "display")
                        await GetCapability<IDisplayDevice>(device).SetDisplayTextAsync(text, color);
                    else
                        await GetCapability<INotificationDevice>(device).SendNotificationAsync(new RuntimeDeviceNotification(text, color));
                }
                else
                    throw new InvalidOperationException("Commande AWTRIX invalide.");

                if (command != "summary")
                    Console.WriteLine("Commande envoyee directement a AWTRIX.");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Commande refusee: {exception.Message}");
            }
        }
    }

    private static async Task<int> RunWledAsync(string ipAddress, string deviceId)
    {
        WledHttpClient client = new(ipAddress);
        using JsonDocument info = await client.GetInfoAsync();
        using JsonDocument state = await client.GetStateAsync();
        WledDevice device = WledDevice.Create(deviceId, client.SetStateAsync);
        device.ApplyState(state.RootElement);

        string name = GetString(info.RootElement, "name") ?? deviceId;
        Console.WriteLine($"Console WLED - IP cible: {ipAddress}, device: {name}");
        PrintDeviceSummary(device, string.Concat("wled/", deviceId));
        Console.WriteLine("Commandes directes: on, off, intensity <0-100>, color <#RRGGBB>, summary, quit");

        while (true)
        {
            Console.Write("wled> ");
            string line = Console.ReadLine();
            if (line == null || string.Equals(line.Trim(), "quit", StringComparison.OrdinalIgnoreCase))
                return 0;

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            string command = parts[0].ToLowerInvariant();
            try
            {
                if (command is "on" or "off")
                    await GetCapability<IToggleSwitchDevice>(device).SetSwitchStateAsync(command == "on");
                else if (command == "intensity"
                    && parts.Length == 2
                    && decimal.TryParse(parts[1], out decimal intensity))
                    await GetCapability<IIntensityGradientDevice>(device).SetIntensityAsync(intensity);
                else if (command == "color"
                    && parts.Length == 2
                    && TryParseRgb(parts[1], out DeviceColor.Rgb color))
                    await GetCapability<IChromaticColorDevice>(device).SetColorAsync(color);
                else if (command == "summary")
                    PrintDeviceSummary(device, string.Concat("wled/", deviceId));
                else
                    throw new InvalidOperationException("Commande WLED invalide.");

                if (command != "summary")
                    Console.WriteLine("Commande envoyee directement a WLED.");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Commande refusee: {exception.Message}");
            }
        }
    }

    private static async Task<int> RunZigbee2MqttAsync(Dictionary<string, string> options)
    {
        if (options.TryGetValue("topic", out string topicRoot) && !string.IsNullOrWhiteSpace(topicRoot))
            Environment.SetEnvironmentVariable("ZIGBEE2MQTT_TOPIC", topicRoot);
        if (options.TryGetValue("host", out string mqttHost) && !string.IsNullOrWhiteSpace(mqttHost))
            Environment.SetEnvironmentVariable("MQTT_SERVICE_HOST", mqttHost);
        if (options.TryGetValue("port", out string mqttPort) && !string.IsNullOrWhiteSpace(mqttPort))
            Environment.SetEnvironmentVariable("MQTT_SERVICE_PORT", mqttPort);

        int waitSeconds = options.TryGetValue("wait-seconds", out string configuredWait)
            && int.TryParse(configuredWait, out int parsedWait)
            ? Math.Max(parsedWait, 1)
            : 30;
        using CancellationTokenSource cancellation = new();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddSimpleConsole(options => options.SingleLine = true));
        Zigbee2MqttRuntimeService runtime = new(loggerFactory.CreateLogger<Zigbee2MqttRuntimeService>());

        Console.WriteLine("Console Zigbee2MQTT");
        try
        {
            await runtime.StartAsync(cancellation.Token);
            Console.WriteLine($"En attente de la liste des devices ({waitSeconds}s maximum)...");
            DateTime deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
            while (!GetZigbeeDevices(runtime).Any()
                && runtime.LastError == null
                && DateTime.UtcNow < deadline)
                await Task.Delay(250, cancellation.Token);

            Console.WriteLine($"Connexion MQTT: {(runtime.IsConnected ? "OK" : "indisponible")}");
            if (runtime.LastError != null)
            {
                PrintException("Erreur de connexion MQTT", runtime.LastError);
                return 1;
            }

            List<ZigbeeDevice> devices = GetZigbeeDevices(runtime);
            if (devices.Count == 0)
            {
                Console.WriteLine("Aucun device Zigbee decouvert. Verifiez le broker, le topic et bridge/devices.");
                return 1;
            }

            return await RunZigbeeCommandLoopAsync(devices);
        }
        catch (Exception exception)
        {
            PrintException("Erreur Zigbee2MQTT", exception);
            return 1;
        }
        finally
        {
            await runtime.StopAsync(CancellationToken.None);
            cancellation.Dispose();
        }
    }

    private static List<ZigbeeDevice> GetZigbeeDevices(Zigbee2MqttRuntimeService runtime)
    {
        IHubDevice hub = runtime.RuntimeRegistry.Devices
            .SelectMany(device => device.Capabilities)
            .OfType<IHubDevice>()
            .FirstOrDefault();
        if (hub == null)
            return [];

        return hub.ChildDevices
            .Select(child => runtime.RuntimeRegistry.GetById(child.Id))
            .OfType<ZigbeeDevice>()
            .OrderBy(device => device.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static async Task<int> RunZigbeeCommandLoopAsync(IReadOnlyList<ZigbeeDevice> devices)
    {
        ZigbeeDevice selected = null;
        PrintZigbeeDevices(devices, selected);
        Console.WriteLine("Commandes: list, select <index>, deselect, summary, on, off, intensity <0-100>, color <#RRGGBB>, color-xy <x> <y>, color-hsv <hue> <saturation>, color-temp <kelvin>, quit");

        while (true)
        {
            Console.Write("zigbee> ");
            string line = Console.ReadLine();
            if (line == null || string.Equals(line.Trim(), "quit", StringComparison.OrdinalIgnoreCase))
                return 0;

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                continue;

            string command = parts[0].ToLowerInvariant();
            if (command == "list")
            {
                PrintZigbeeDevices(devices, selected);
                continue;
            }

            if (command == "select" && parts.Length == 2 && int.TryParse(parts[1], out int index)
                && index >= 0 && index < devices.Count)
            {
                selected = devices[index];
                Console.WriteLine($"Device selectionne: {selected.Id}");
                PrintDeviceSummary(selected, string.Concat(GetTopicRootForDisplay(), "/", selected.Id));
                Console.WriteLine($"Commandes disponibles: {GetZigbeeCommandDescription(selected)}");
                continue;
            }

            if (command is "deselect" or "unselect")
            {
                selected = null;
                Console.WriteLine("Aucun device selectionne.");
                continue;
            }

            if (command == "summary")
            {
                if (selected == null)
                    Console.WriteLine("Selectionnez d'abord un device.");
                else
                    PrintDeviceSummary(selected, string.Concat(GetTopicRootForDisplay(), "/", selected.Id));
                continue;
            }

            if (selected == null)
            {
                Console.WriteLine("Operation refusee: selectionnez d'abord un device.");
                continue;
            }

            try
            {
                await ExecuteZigbeeCommandAsync(selected, command, parts.Skip(1).ToArray());
                Console.WriteLine("Commande envoyee directement a Zigbee2MQTT.");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Commande refusee: {exception.Message}");
            }
        }
    }

    private static void PrintZigbeeDevices(IReadOnlyList<ZigbeeDevice> devices, ZigbeeDevice selected)
    {
        Console.WriteLine("Devices Zigbee decouverts:");
        for (int index = 0; index < devices.Count; index++)
        {
            ZigbeeDevice device = devices[index];
            string marker = ReferenceEquals(device, selected) ? "*" : " ";
            Console.WriteLine($"{marker} [{index}] {device.Id} ({device.Metadata.Manufacturer} {device.Metadata.Model})");
        }
    }

    private static async Task ExecuteZigbeeCommandAsync(ZigbeeDevice device, string command, string[] values)
    {
        if (command is "on" or "off")
        {
            await GetCapability<IToggleSwitchDevice>(device).SetSwitchStateAsync(command == "on");
            return;
        }

        if (command == "intensity" && values.Length == 1 && decimal.TryParse(values[0], out decimal intensity))
        {
            await GetCapability<IIntensityGradientDevice>(device).SetIntensityAsync(intensity);
            return;
        }

        if (command == "color-xy"
            && values.Length == 2
            && double.TryParse(values[0], out double x)
            && double.TryParse(values[1], out double y))
        {
            await GetCapability<IChromaticColorDevice>(device).SetColorAsync(new DeviceColor.Xy(x, y));
            return;
        }

        if (command == "color" && values.Length == 1 && TryParseRgb(values[0], out DeviceColor.Rgb rgb))
        {
            await GetCapability<IChromaticColorDevice>(device).SetColorAsync(rgb);
            return;
        }

        if (command == "color-hsv"
            && values.Length == 2
            && double.TryParse(values[0], out double hue)
            && double.TryParse(values[1], out double saturation))
        {
            await GetCapability<IChromaticColorDevice>(device).SetColorAsync(new DeviceColor.Hsv(hue, saturation / 100D, 1D));
            return;
        }

        if (command == "color-temp" && values.Length == 1 && int.TryParse(values[0], out int kelvin))
        {
            await GetCapability<IColorTemperatureDevice>(device).SetColorTemperatureAsync(kelvin);
            return;
        }

        throw new InvalidOperationException("Commande Zigbee invalide ou capability non exposee.");
    }

    private static TCapability GetCapability<TCapability>(IDevice device)
        where TCapability : class, IDeviceCapability
    {
        return device.Elements
            .SelectMany(element => element.Capabilities)
            .OfType<TCapability>()
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"Aucune capability {typeof(TCapability).Name} exposee par {device.Id}.");
    }

    private static string GetTopicRootForDisplay()
    {
        string topicRoot = Environment.GetEnvironmentVariable("ZIGBEE2MQTT_TOPIC");
        return string.IsNullOrWhiteSpace(topicRoot) ? "zigbee2mqtt" : topicRoot.Trim().Trim('/');
    }

    private static TCapability GetCapability<TCapability>(ShellyGen2Device device, int index)
        where TCapability : class, IDeviceCapability
    {
        TCapability capability = device.Elements
            .Where(element => element.Capabilities.OfType<TCapability>().Any())
            .ElementAtOrDefault(index)?
            .Capabilities
            .OfType<TCapability>()
            .FirstOrDefault();
        return capability ?? throw new InvalidOperationException($"Aucune capability {typeof(TCapability).Name} a l'index {index}.");
    }

    private static string GetCommandDescription(IDevice device, string switchName, string coverName, string lightName)
    {
        List<string> commands = [];
        if (HasCapability<IToggleSwitchDevice>(device))
            commands.Add($"on <{switchName}>, off <{switchName}>");
        if (HasCapability<ICoverDevice>(device))
        {
            commands.Add($"open <{coverName}>, close <{coverName}>, stop <{coverName}>");
            if (HasCapability<IPositionableCoverDevice>(device))
                commands.Add($"position <{coverName}> <0-100>");
        }
        if (HasCapability<IIntensityGradientDevice>(device))
            commands.Add($"intensity <{lightName}> <0-100>");
        if (HasCapability<IColorTemperatureDevice>(device))
            commands.Add("color-temp <kelvin>");
        commands.Add("quit");
        return string.Join(", ", commands);
    }

    private static string GetZigbeeCommandDescription(IDevice device)
    {
        List<string> commands = [];
        if (HasCapability<IToggleSwitchDevice>(device))
            commands.Add("on, off");
        if (HasCapability<IIntensityGradientDevice>(device))
            commands.Add("intensity <0-100>");
        IChromaticColorDevice color = device.Elements
            .SelectMany(element => element.Capabilities)
            .OfType<IChromaticColorDevice>()
            .FirstOrDefault();
        if (color?.SupportedColorModels.Contains(ColorModel.Xy) == true)
            commands.Add("color-xy <x> <y>");
        if (color?.SupportedColorModels.Contains(ColorModel.Hsv) == true)
            commands.Add("color-hsv <hue> <saturation>");
        if (color != null && color.SupportedColorModels.Any(model => model is ColorModel.Xy or ColorModel.Hsv))
            commands.Add("color <#RRGGBB>");
        if (HasCapability<IColorTemperatureDevice>(device))
            commands.Add("color-temp <kelvin>");
        commands.Add("quit");
        return string.Join(", ", commands);
    }

    private static bool TryParseRgb(string value, out DeviceColor.Rgb color)
    {
        color = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string hex = value.StartsWith('#') ? value[1..] : value;
        if (hex.Length != 6
            || !byte.TryParse(hex[0..2], System.Globalization.NumberStyles.HexNumber, null, out byte red)
            || !byte.TryParse(hex[2..4], System.Globalization.NumberStyles.HexNumber, null, out byte green)
            || !byte.TryParse(hex[4..6], System.Globalization.NumberStyles.HexNumber, null, out byte blue))
            return false;

        color = new DeviceColor.Rgb(red, green, blue);
        return true;
    }

    private static void PrintDeviceSummary(IDevice device, string topic)
    {
        int capabilityCount = device.Elements.Sum(element => element.Capabilities.Count);
        int sensorCount = device.Elements
            .SelectMany(element => element.Capabilities)
            .OfType<ISensorDevice>()
            .Count(sensor => sensor.Readings.Count > 0);
        Console.WriteLine($"MQTT topic device: {topic}");
        Console.WriteLine($"Elements: {device.Elements.Count}, capabilities: {capabilityCount}, capteurs: {sensorCount}");
        foreach (IDeviceElement element in device.Elements)
        {
            Console.WriteLine($"- {element.Name}: {string.Join(", ", element.Capabilities.Select(capability => capability.GetType().Name))}");
            foreach (IChromaticColorDevice color in element.Capabilities.OfType<IChromaticColorDevice>())
            {
                string current = color.CurrentColor?.ToString() ?? "aucune";
                Console.WriteLine($"  Couleur: {string.Join(", ", color.SupportedColorModels)} (actuelle: {current})");
            }
            foreach (ISensorDevice sensor in element.Capabilities.OfType<ISensorDevice>())
                foreach (RuntimeSensorReading reading in sensor.Readings.Values)
                    Console.WriteLine($"  {reading.Definition.Label}: {reading.Value} {reading.Definition.CanonicalUnit}");
        }

    }

    private static string GetNestedString(JsonElement element, string objectName, string propertyName)
    {
        return element.TryGetProperty(objectName, out JsonElement nested)
            ? GetString(nested, propertyName)
            : null;
    }

    private static bool HasCapability<TCapability>(IDevice device)
        where TCapability : class, IDeviceCapability
    {
        return device.Elements.Any(element => element.Capabilities.OfType<TCapability>().Any());
    }

    private static async Task<int> RunCommandLoopAsync(Func<string, int, string, CancellationToken, Task> execute)
    {
        while (true)
        {
            Console.Write("> ");
            string line = Console.ReadLine();
            if (line == null || string.Equals(line.Trim(), "quit", StringComparison.OrdinalIgnoreCase))
                return 0;

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !int.TryParse(parts[1], out int index))
            {
                Console.WriteLine("Usage: <commande> <index> [valeur].");
                continue;
            }

            try
            {
                await execute(parts[0].ToLowerInvariant(), index, parts.Length > 2 ? parts[2] : null, CancellationToken.None);
                Console.WriteLine("Commande envoyee directement au Shelly.");
            }
            catch (Exception exception)
            {
                Console.WriteLine($"Commande refusee: {exception.Message}");
            }
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        Dictionary<string, string> options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int index = 1; index + 1 < args.Length; index += 2)
        {
            if (args[index].StartsWith("--", StringComparison.Ordinal))
                options[args[index].Substring(2)] = args[index + 1];
        }
        return options;
    }

    private static void SetCredentials(string generation, string username, string password)
    {
        string prefix = generation == "shelly-gen1" ? "SHELLY_GEN1_" : "SHELLY_GEN2_";
        if (username != null)
            Environment.SetEnvironmentVariable(string.Concat(prefix, "USERNAME"), username);
        if (password != null)
            Environment.SetEnvironmentVariable(string.Concat(prefix, "PASSWORD"), password);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run -- shelly-gen1 --ip <ip> [--user <user>] [--password <password>]");
        Console.WriteLine("  dotnet run -- shelly-gen2 --ip <ip> [--password <password>]");
        Console.WriteLine("  dotnet run -- awtrix --ip <ip> [--id <device-id>]");
        Console.WriteLine("  dotnet run -- wled --ip <ip> [--id <device-id>]");
        Console.WriteLine("  dotnet run -- zigbee2mqtt [--host <mqtt-host>] [--port <mqtt-port>] [--topic <topic-root>] [--wait-seconds <seconds>]");
        Console.WriteLine("Le broker MQTT reste configure par MQTT_SERVICE_HOST et MQTT_SERVICE_PORT.");
    }
}
