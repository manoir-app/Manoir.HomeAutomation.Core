using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using MaNoir.HomeAutomation.Devices;

namespace MaNoir.HomeAutomation.Devices.Zigbee2Mqtt;

public sealed partial class ZigbeeDevice
{
    /// <summary>
    /// Stores action normalization and runtime action projection for a Zigbee device.
    /// </summary>
    private sealed class ZigbeeActionCapability : IRuntimeActionDevice
    {
        public ZigbeeActionCapability(IEnumerable<string> availableActions)
        {
            AvailableActions = availableActions
                .Select(NormalizeAction)
                .ToArray();
        }

        public IReadOnlyList<RuntimeDeviceAction> AvailableActions { get; }

        public RuntimeDeviceAction LastAction { get; private set; }

        public bool TryApplyAction(JsonElement state, out RuntimeDeviceAction action)
        {
            action = null;
            if (!state.TryGetProperty("action", out JsonElement value)
                || value.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(value.GetString()))
                return false;

            string rawAction = value.GetString().Trim();
            RuntimeDeviceAction normalized = NormalizeAction(rawAction);
            Dictionary<string, string> attributes = new(normalized.Attributes, StringComparer.OrdinalIgnoreCase);
            foreach (JsonProperty property in state.EnumerateObject())
            {
                if (string.Equals(property.Name, "action", StringComparison.OrdinalIgnoreCase)
                    || property.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Object or JsonValueKind.Array)
                    continue;

                attributes[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => property.Value.GetRawText()
                };
            }

            action = new RuntimeDeviceAction(normalized.Kind, normalized.Action, normalized.RawAction, attributes);
            LastAction = action;
            return true;
        }

        private static RuntimeDeviceAction NormalizeAction(string rawAction)
        {
            Dictionary<string, string> attributes = new(StringComparer.OrdinalIgnoreCase);
            string kind = "button";
            string action = rawAction;
            if (rawAction.Contains("rotate_left", StringComparison.OrdinalIgnoreCase))
            {
                kind = "rotary";
                action = "rotate";
                attributes["direction"] = "left";
                attributes["delta"] = "-1";
            }
            else if (rawAction.Contains("rotate_right", StringComparison.OrdinalIgnoreCase))
            {
                kind = "rotary";
                action = "rotate";
                attributes["direction"] = "right";
                attributes["delta"] = "1";
            }
            else if (string.Equals(rawAction, "brightness_move_up", StringComparison.OrdinalIgnoreCase))
            {
                kind = "continuous";
                action = "brightness_move";
                attributes["direction"] = "up";
                attributes["delta"] = "1";
            }
            else if (string.Equals(rawAction, "brightness_move_down", StringComparison.OrdinalIgnoreCase))
            {
                kind = "continuous";
                action = "brightness_move";
                attributes["direction"] = "down";
                attributes["delta"] = "-1";
            }
            else if (string.Equals(rawAction, "brightness_stop", StringComparison.OrdinalIgnoreCase))
            {
                kind = "continuous";
                action = "brightness_stop";
            }

            return new RuntimeDeviceAction(kind, action, rawAction, attributes);
        }
    }
}
