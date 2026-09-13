using Home.Common.Model;
using MaNoir.HomeAutomation;
using MaNoir.HomeAutomation.Devices;
using MaNoir.HomeAutomation.Scripting;
using System;
using System.Threading;

namespace MaNoir.Agents.Sarah;

public sealed class RuntimeSceneScriptExecutor
{
    private readonly RuntimeDeviceRegistry _runtimeRegistry;
    private readonly SceneScriptEngine _engine;

    public RuntimeSceneScriptExecutor(RuntimeDeviceRegistry runtimeRegistry, SceneScriptEngine engine = null)
    {
        _runtimeRegistry = runtimeRegistry ?? throw new ArgumentNullException(nameof(runtimeRegistry));
        _engine = engine ?? new SceneScriptEngine();
    }

    public bool Execute(SceneStep step, CancellationToken cancellationToken = default)
    {
        if (step == null || step.TargetKind != SceneStepTargetKind.Script)
            return false;
        if (!string.IsNullOrWhiteSpace(step.ScriptContent) && !string.IsNullOrWhiteSpace(step.ScriptId))
            return false;

        string script = step.ScriptContent;
        if (string.IsNullOrWhiteSpace(script))
        {
            ScriptDefinition definition = new ScriptLogic().GetByIdAsync(step.ScriptId, cancellationToken).GetAwaiter().GetResult();
            script = definition?.Content;
        }

        if (string.IsNullOrWhiteSpace(script))
            return false;

        _engine.Execute(script, _runtimeRegistry.Devices, step.ScriptParameters, cancellationToken);
        return true;
    }
}