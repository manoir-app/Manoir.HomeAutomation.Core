using System;
using System.Collections.Generic;

namespace Home.Common.Model;

public enum TimeOffsetKind
{
    FromMidnight = 0,
    FromSunrise = 1,
    FromSunset = 2,
    FromEarliestWakeup = 3,
    FromLatestWakeup = 4
}

public enum TriggerKind
{
    Clock = 0,
    NetworkDeviceConnectionChanged = 8,
    Webhook = 15,
    MqttValue = 16,
}

public enum NetworkDeviceTriggerKind
{
    Connection,
    Disconnection
}

public class Trigger
{
    public Trigger()
    {
        RaisedMessages = [];
        ChangedProperties = [];
    }

    public string Id { get; set; }
    public TriggerKind Kind { get; set; }
    public string Label { get; set; }
    public TimeSpan? Offset { get; set; }
    public TimeOffsetKind? OffsetKind { get; set; }
    public string NetworkDeviceName { get; set; }
    public NetworkDeviceTriggerKind? NetworkDeviceTriggerKind { get; set; }
    public string Path { get; set; }
    public string JsonPathInValue { get; set; }
    public decimal? ThredsholdForChange { get; set; }
    public List<TriggerRaisedMessage> RaisedMessages { get; set; }
    public List<TriggerPropertyChange> ChangedProperties { get; set; }
    public DateTimeOffset? LatestOccurence { get; set; }
    public DateTimeOffset? ProbableNextOccurence { get; set; }

    public string ReplaceContent(string messageContent, string source, string data = null)
    {
        messageContent ??= string.Empty;
        messageContent = messageContent.Replace("{{source}}", source ?? "(inconnu)", StringComparison.Ordinal);
        messageContent = messageContent.Replace("{{date}}", DateTimeOffset.Now.ToString("d"), StringComparison.Ordinal);
        messageContent = messageContent.Replace("{{time}}", DateTimeOffset.Now.ToString("HH:mm:ss.ffff"), StringComparison.Ordinal);
        messageContent = messageContent.Replace("{{networkdevice}}", NetworkDeviceName ?? string.Empty, StringComparison.Ordinal);
        messageContent = messageContent.Replace("{{rawdata}}", data ?? "-NODATA-", StringComparison.Ordinal);
        return messageContent;
    }

    public override string ToString()
    {
        return Kind switch
        {
            TriggerKind.Clock => OffsetKind.GetValueOrDefault(TimeOffsetKind.FromMidnight) switch
            {
                TimeOffsetKind.FromMidnight => $"A {Offset.GetValueOrDefault()}",
                TimeOffsetKind.FromSunrise => $"Après {Offset.GetValueOrDefault()} après le lever du soleil",
                TimeOffsetKind.FromSunset => $"Après {Offset.GetValueOrDefault()} après le coucher du soleil",
                TimeOffsetKind.FromEarliestWakeup => $"Après {Offset.GetValueOrDefault()} après l'heure de lever la plus tôt",
                TimeOffsetKind.FromLatestWakeup => $"Après {Offset.GetValueOrDefault()} après l'heure de lever la plus tard",
                _ => "inconnu"
            },
            TriggerKind.NetworkDeviceConnectionChanged => $"Sur connexion/deconnexion de {NetworkDeviceName}",
            TriggerKind.Webhook => "Webhook",
            TriggerKind.MqttValue => "Mqtt",
            _ => "inconnu"
        };
    }
}

public class TriggerRaisedMessage
{
    public Condition Condition { get; set; }
    public string MessageTopic { get; set; }
    public string MessageContent { get; set; }
}

public enum TriggerPropertyChangeKind
{
    GenericProperty,
    RoomProperty
}

public class TriggerPropertyChange
{
    public string PropertyName { get; set; }
    public string RoomId { get; set; }
    public TriggerPropertyChangeKind Kind { get; set; }
}