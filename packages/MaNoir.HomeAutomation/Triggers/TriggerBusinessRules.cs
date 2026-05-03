using Home.Common.Model;
using System;
using System.Collections.Generic;

namespace MaNoir.HomeAutomation;

public sealed partial class TriggerLogic
{
    public static string NormalizeTriggerId(string triggerId)
    {
        if (string.IsNullOrWhiteSpace(triggerId))
            return null;

        return triggerId.Trim().ToLowerInvariant();
    }

    public static Trigger PrepareTrigger(Trigger trigger)
    {
        if (trigger == null)
            return null;

        trigger.Id = NormalizeTriggerId(trigger.Id);
        trigger.Label = string.IsNullOrWhiteSpace(trigger.Label) ? null : trigger.Label.Trim();
        trigger.NetworkDeviceName = string.IsNullOrWhiteSpace(trigger.NetworkDeviceName) ? null : trigger.NetworkDeviceName.Trim();
        trigger.Path = string.IsNullOrWhiteSpace(trigger.Path) ? null : trigger.Path.Trim();
        trigger.JsonPathInValue = string.IsNullOrWhiteSpace(trigger.JsonPathInValue) ? null : trigger.JsonPathInValue.Trim();
        trigger.RaisedMessages ??= [];
        trigger.ChangedProperties ??= [];
        return trigger;
    }

    public static void ApplyTriggerUpdate(Trigger existing, Trigger incoming)
    {
        if (existing == null || incoming == null)
            return;

        existing.Kind = incoming.Kind;
        existing.Label = incoming.Label;
        existing.Offset = incoming.Offset;
        existing.OffsetKind = incoming.OffsetKind;
        existing.NetworkDeviceName = incoming.NetworkDeviceName;
        existing.NetworkDeviceTriggerKind = incoming.NetworkDeviceTriggerKind;
        existing.Path = incoming.Path;
        existing.JsonPathInValue = incoming.JsonPathInValue;
        existing.ThredsholdForChange = incoming.ThredsholdForChange;
        existing.RaisedMessages = incoming.RaisedMessages == null ? [] : [.. incoming.RaisedMessages];
        existing.ChangedProperties = incoming.ChangedProperties == null ? [] : [.. incoming.ChangedProperties];
    }

    public static bool ShouldKeepProbableNextOccurrence(Trigger trigger, DateTimeOffset? probableNextOccurrence)
    {
        if (trigger == null)
            return false;

        if (probableNextOccurrence.HasValue)
            return true;

        return !trigger.ProbableNextOccurence.HasValue || trigger.ProbableNextOccurence.Value >= DateTimeOffset.Now;
    }
}