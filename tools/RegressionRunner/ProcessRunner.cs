using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Parquet.SourceGenerator.Tools.Regression;

/// <summary>
/// The result of running one child process.
/// </summary>
/// <param name="ExitCode">Process exit code. Non-zero fails the step.</param>
/// <param name="Output">Combined stdout and stderr.</param>
public sealed record ProcessResult(int ExitCode, string Output);

/// <summary>
/// Runs child processes. Injected so the executor's orchestration can be tested without a build.
/// </summary>
public interface IProcessRunner
{
    /// <summary>
    /// Runs a command to completion.
    /// </summary>
    /// <param name="executable">Program to run.</param>
    /// <param name="arguments">Arguments, passed without shell interpretation.</param>
    /// <param name="workingDirectory">Working directory.</param>
    /// <param name="environment">Extra environment variables.</param>
    /// <returns>The exit code and captured output.</returns>
    ProcessResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment
    );
}

/// <summary>
/// The real <see cref="IProcessRunner"/>, backed by <see cref="Process"/>.
/// </summary>
public sealed class SystemProcessRunner : IProcessRunner
{
    private readonly TextWriter? _echo;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemProcessRunner"/> class.
    /// </summary>
    /// <param name="echo">Optional writer that receives child output live.</param>
    public SystemProcessRunner(TextWriter? echo = null) => _echo = echo;

    /// <inheritdoc />
    public ProcessResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string> environment
    )
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(environment);

        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        for (int i = 0; i < arguments.Count; i++)
        {
            startInfo.ArgumentList.Add(arguments[i]);
        }

        foreach (KeyValuePair<string, string> variable in environment)
        {
            startInfo.Environment[variable.Key] = variable.Value;
        }

        var captured = new StringBuilder();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, e) => Append(captured, e.Data);
        process.ErrorDataReceived += (_, e) => Append(captured, e.Data);

        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            return new ProcessResult(127, exception.Message);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        process.WaitForExit();

        return new ProcessResult(process.ExitCode, captured.ToString());
    }

    private void Append(StringBuilder builder, string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (builder)
        {
            builder.AppendLine(line);
        }

        _echo?.WriteLine(line);
    }
}
