using Home.Common.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation;

public sealed partial class TriggerLogic
{
    private async Task<bool> EvaluateConditionAsync(Condition condition, CancellationToken cancellationToken)
    {
        if (condition == null)
            return true;

        switch (condition.Kind)
        {
            case ConditionKind.And:
                return await EvaluateAndConditionAsync(condition, cancellationToken);
            case ConditionKind.Or:
                return await EvaluateOrConditionAsync(condition, cancellationToken);
            case ConditionKind.SceneCheck:
                return await EvaluateSceneConditionAsync(condition, cancellationToken);
            case ConditionKind.DeviceCheck:
                return await EvaluateDeviceConditionAsync(condition, cancellationToken);
            case ConditionKind.UserCheck:
            case ConditionKind.EntityCheck:
            case ConditionKind.RoomPropertyCheck:
            case ConditionKind.MeshPropertyCheck:
            default:
                Console.WriteLine($"Trigger condition kind {condition.Kind} is not wired yet.");
                return false;
        }
    }

    private async Task<bool> EvaluateAndConditionAsync(Condition condition, CancellationToken cancellationToken)
    {
        if (condition.SubConditions == null || condition.SubConditions.Length == 0)
            return true;

        foreach (Condition subCondition in condition.SubConditions)
        {
            if (!await EvaluateConditionAsync(subCondition, cancellationToken))
                return false;
        }

        return true;
    }

    private async Task<bool> EvaluateOrConditionAsync(Condition condition, CancellationToken cancellationToken)
    {
        if (condition.SubConditions == null || condition.SubConditions.Length == 0)
            return true;

        foreach (Condition subCondition in condition.SubConditions)
        {
            if (await EvaluateConditionAsync(subCondition, cancellationToken))
                return true;
        }

        return false;
    }

    private static bool EvaluateBooleanAliasCondition(string propertyName, string expectedPropertyName)
    {
        return string.Equals(propertyName, expectedPropertyName, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<bool> EvaluateSceneConditionAsync(Condition condition, CancellationToken cancellationToken)
    {
        if (condition == null || string.IsNullOrWhiteSpace(condition.PropertyName))
            return true;

        string propertyName = condition.PropertyName.Trim();
        bool isActive = await IsSceneActiveAsync(condition.ElementId, cancellationToken);

        if (EvaluateBooleanAliasCondition(propertyName, "isactive")
            || EvaluateBooleanAliasCondition(propertyName, "is-active"))
        {
            return isActive;
        }

        if (EvaluateBooleanAliasCondition(propertyName, "isinactive")
            || EvaluateBooleanAliasCondition(propertyName, "isnotactive")
            || EvaluateBooleanAliasCondition(propertyName, "is-no-active")
            || EvaluateBooleanAliasCondition(propertyName, "is-not-active")
            || EvaluateBooleanAliasCondition(propertyName, "is-inactive"))
        {
            return !isActive;
        }

        return false;
    }

    private async Task<bool> IsSceneActiveAsync(string sceneId, CancellationToken cancellationToken)
    {
        string normalizedSceneId = SceneLogic.NormalizeSceneId(sceneId);
        if (normalizedSceneId == null)
            return false;

        List<SceneGroup> groups = await new SceneLogic().GetGroupsAsync(false, cancellationToken);
        return groups.Any(group => group.CurrentActiveScenes != null
            && group.CurrentActiveScenes.Exists(current => string.Equals(current, normalizedSceneId, StringComparison.OrdinalIgnoreCase)));
    }

    private async Task<bool> EvaluateDeviceConditionAsync(Condition condition, CancellationToken cancellationToken)
    {
        if (condition == null)
            return true;

        Device device = await new DeviceLogic().GetByIdAsync(condition.ElementId, cancellationToken);
        if (device == null)
            return false;

        string propertyName = condition.PropertyName?.Trim();
        if (string.IsNullOrWhiteSpace(propertyName))
            return true;

        string actualValue = GetDeviceConditionValue(device, propertyName);
        return EvaluateComparison(actualValue, condition);
    }

    private static string GetDeviceConditionValue(Device device, string propertyName)
    {
        if (device == null || string.IsNullOrWhiteSpace(propertyName))
            return null;

        if (string.Equals(propertyName, "status", StringComparison.OrdinalIgnoreCase)
            || string.Equals(propertyName, "mainstatus", StringComparison.OrdinalIgnoreCase)
            || string.Equals(propertyName, "mainstatusinfo", StringComparison.OrdinalIgnoreCase))
        {
            return device.MainStatusInfo;
        }

        DeviceData data = device.Datas?.FirstOrDefault(current => string.Equals(current.Name, propertyName, StringComparison.OrdinalIgnoreCase));
        return data?.Value;
    }

    private static bool EvaluateComparison(string actualValue, Condition condition)
    {
        condition.Normalize();

        if (condition.InValues != null && condition.InValues.Length > 0)
        {
            bool contains = condition.InValues.Any(value => string.Equals(value, actualValue, StringComparison.OrdinalIgnoreCase));
            return condition.Operator == "!=" ? !contains : contains;
        }

        string expectedValue = condition.Value;
        return condition.Operator switch
        {
            "==" => CompareComparable(actualValue, expectedValue) == 0,
            "!=" => CompareComparable(actualValue, expectedValue) != 0,
            ">" => CompareComparable(actualValue, expectedValue) > 0,
            ">=" => CompareComparable(actualValue, expectedValue) >= 0,
            "<" => CompareComparable(actualValue, expectedValue) < 0,
            "<=" => CompareComparable(actualValue, expectedValue) <= 0,
            _ => false,
        };
    }

    private static int CompareComparable(string actualValue, string expectedValue)
    {
        if (bool.TryParse(actualValue, out bool actualBool) && bool.TryParse(expectedValue, out bool expectedBool))
            return actualBool.CompareTo(expectedBool);

        if (decimal.TryParse(actualValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal actualDecimal)
            && decimal.TryParse(expectedValue, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal expectedDecimal))
        {
            return actualDecimal.CompareTo(expectedDecimal);
        }

        if (DateTimeOffset.TryParse(actualValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset actualDate)
            && DateTimeOffset.TryParse(expectedValue, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTimeOffset expectedDate))
        {
            return actualDate.CompareTo(expectedDate);
        }

        return string.Compare(actualValue ?? string.Empty, expectedValue ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}