using System;
using System.Threading.Tasks;
using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Events;
using Spectre.Console;

namespace dotnet_repl;

public class InputKernel :
    Kernel,
    IKernelCommandHandler<RequestInput>
{
    public InputKernel() : base("ask")
    {
    }

    public Func<string, Task<string?>>? GetInputValueAsync { get; set; }

    public async Task HandleAsync(RequestInput command, KernelInvocationContext context)
    {
        if (GetInputValueAsync is {} getInput &&
            await getInput(command.TargetKernelName) is { } value &&
            !string.IsNullOrWhiteSpace(value))
        {
            context.Publish(new InputProduced(value, command));
        }
        else
        {
            switch (command.InputTypeHint)
            {
                default:
                    value = AnsiConsole.Ask<string>($"Please provide a value for {command.TargetKernelName}");

                    if (value is not null)
                    {
                        context.Publish(new InputProduced(value, command));
                    }

                    break;
            }
        }
    }
}