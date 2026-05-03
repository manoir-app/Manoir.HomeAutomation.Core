using Home.Common.Model;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;

namespace MaNoir.HomeAutomation.UnitTests;

[TestClass]
public sealed class TriggerLogicTests
{
    [TestMethod]
    public void PrepareTrigger_ShouldNormalizeIdentifierAndCollections()
    {
        Trigger trigger = new Trigger()
        {
            Id = " WAKE-UP ",
            Label = "  Morning wakeup  ",
            NetworkDeviceName = " Router ",
            Path = " sensor/path ",
            JsonPathInValue = " $.value ",
            RaisedMessages = null,
            ChangedProperties = null
        };

        Trigger prepared = TriggerLogic.PrepareTrigger(trigger);

        Assert.AreSame(trigger, prepared);
        Assert.AreEqual("wake-up", prepared.Id);
        Assert.AreEqual("Morning wakeup", prepared.Label);
        Assert.AreEqual("Router", prepared.NetworkDeviceName);
        Assert.AreEqual("sensor/path", prepared.Path);
        Assert.AreEqual("$.value", prepared.JsonPathInValue);
        Assert.IsNotNull(prepared.RaisedMessages);
        Assert.IsNotNull(prepared.ChangedProperties);
    }

    [TestMethod]
    public void ReplaceContent_ShouldKeepLegacyPlaceholders()
    {
        Trigger trigger = new Trigger()
        {
            NetworkDeviceName = "gateway"
        };

        string content = trigger.ReplaceContent("src={{source}} dev={{networkdevice}} raw={{rawdata}}", "sarah", "42");

        Assert.AreEqual("src=sarah dev=gateway raw=42", content);
    }

    [TestMethod]
    public void SetSettings_ShouldClearPastProbableNextOccurrenceWhenMissingInput()
    {
        Trigger trigger = new Trigger()
        {
            ProbableNextOccurence = DateTimeOffset.Now.AddMinutes(-5)
        };

        bool shouldKeep = TriggerLogic.ShouldKeepProbableNextOccurrence(trigger, null);

        Assert.IsFalse(shouldKeep);
    }
}