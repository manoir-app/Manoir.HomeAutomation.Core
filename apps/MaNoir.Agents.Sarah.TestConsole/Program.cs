using MaNoir.HomeAutomation.Devices.Shelly;
using MaNoir.Agents.Sarah.Shelly;
using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Protocols.Shelly;
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

        Dictionary<string, string> options = ParseOptions(args);
        if (!options.TryGetValue("ip", out string ipAddress))
        {
            PrintUsage();
            return 1;
        }

        string generation = args[0].ToLowerInvariant();
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
        commands.Add("quit");
        return string.Join(", ", commands);
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
        Console.WriteLine("Le broker MQTT reste configure par MQTT_SERVICE_HOST et MQTT_SERVICE_PORT.");
    }
}
