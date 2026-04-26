using System;

namespace MaNoir.HomeAutomation.FunctionalTests.Infrastructure;

internal sealed class ProcessEnvironmentVariableScope : IDisposable
{
    private readonly string _name;
    private readonly string _previousValue;

    public ProcessEnvironmentVariableScope(string name, string value)
    {
        _name = name;
        _previousValue = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(_name, _previousValue);
    }
}