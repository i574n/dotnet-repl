using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using dotnet_repl;
using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Commands;
using Microsoft.DotNet.Interactive.Documents;
using Microsoft.DotNet.Interactive.Documents.Jupyter;
using Microsoft.DotNet.Interactive.Events;
using Spectre.Console;

namespace Automation;

public class NotebookRunner
{
    private readonly CompositeKernel _kernel;

    public NotebookRunner(CompositeKernel kernel)
    {
        _kernel = kernel;
    }

    public async Task<InteractiveDocument> RunNotebookAsync(
        InteractiveDocument notebook,
        IDictionary<string, string>? parameters = null,
        CancellationToken cancellationToken = default)
    {
        var resultDocument = new InteractiveDocument();

        if (parameters is not null)
        {
            // incoming parameters are treated case insensitively
            parameters = new Dictionary<string, string>(parameters, StringComparer.InvariantCultureIgnoreCase);

            var inputKernel = _kernel.ChildKernels.OfType<InputKernel>().FirstOrDefault();

            if (inputKernel is not null)
            {
                inputKernel.GetInputValueAsync = key =>
                {
                    if (parameters.TryGetValue(key, out var value))
                    {
                        return Task.FromResult<string?>(value);
                    }
                    else
                    {
                        return Task.FromResult<string?>(null);
                    }
                };
            }
        }

        var lengthLimit = 3000;

        // string ellipsis(string text, int lengthLimit) =>
        //     text.Length <= lengthLimit
        //         ? text
        //         : text[..lengthLimit] + "..."; ;

        string ellipsisEnd(string s, int max)
        {
            var len = s.Length;
            if (len <= max)
            {
                return s;
            }
            else
            {
                var half = (double)max / 2;
                var startHalf = (int)Math.Ceiling(half);
                var endHalf = (int)Math.Floor(half);
                var start = s[..(startHalf - 1)];
                var end = s[(len - endHalf)..];
                return $"{start}...{end}";
            }
        }


        var elementSubmissionMap = new Dictionary<int, HashSet<string>>();
        var elementIndex = -1;

        foreach (var element in notebook.Elements)
        {
            var command = new SubmitCode(element.Contents, element.KernelName);

            var events = _kernel.KernelEvents.Replay();

            using var connect = events.Connect();

            var startTime = DateTimeOffset.Now;

            string elapsedText() => (DateTimeOffset.Now - startTime) switch
            {
                var t when t.TotalSeconds < 1 => $"{t.TotalMilliseconds:F2}ms",
                var t when t.TotalMinutes < 1 => $"{t.TotalSeconds:F2}s",
                var t => $"{t.TotalMinutes:F2}m"
            };

            void printCell(Color? color, string? kernelName, string? code, (string?, string?)? output)
            {
                Style statusColor = new(color ?? Color.LightSlateGrey);

                AnsiConsole.Console.WriteLine();

                if (kernelName != null)
                {
                    var rule = new Rule(kernelName);
                    rule.LeftJustified();
                    rule.RuleStyle(statusColor);
                    AnsiConsole.Console.Write(rule);
                }

                if (code != null)
                {
                    AnsiConsole.Console.Write(new Text(Markup.Escape(code)));
                    AnsiConsole.Console.WriteLine();
                }

                if (output.HasValue)
                {
                    (string? outputHeader, string? outputText) = output.Value;
                    if (outputText?.Trim().Length > 0)
                    {
                        AnsiConsole.Console.Write(
                            new Panel(Markup.Escape(outputText ?? ""))
                                .Header(color != null ? Markup.Escape($"[ {elapsedText()} - {outputHeader} ]") : "")
                                .Expand()
                                .RoundedBorder()
                                .BorderStyle(statusColor)
                        );
                    }
                }
            }

            elementIndex++;

            if (element.Contents.StartsWith("//// ignore"))
            {
                printCell(null, $"{element.KernelName} - ignored", element.Contents, null);
                continue;
            }

            var result = _kernel.SendAsync(command, cancellationToken);

            var tcs = new TaskCompletionSource();
            StringBuilder? stdOut = default;
            StringBuilder? stdErr = default;

            var outputs = new List<InteractiveDocumentOutputElement>();

            using var _ = events.Subscribe(@event =>
            {
                switch (@event)
                {
                    // events that tell us whether the submission was valid
                    case IncompleteCodeSubmissionReceived incomplete when incomplete.Command == command:
                        break;

                    case CompleteCodeSubmissionReceived complete:
                        break;

                    case CodeSubmissionReceived codeSubmissionReceived:
                        if (!elementSubmissionMap.TryGetValue(elementIndex, out var codeSet))
                        {
                            codeSet = [];
                            elementSubmissionMap.Add(elementIndex, codeSet);
                        }

                        void tryPrintCode(string? kernelName, string code)
                        {
                            if (!codeSet.Contains(code))
                            {
                                printCell(null, kernelName, code, null);
                                codeSet.Add(code);
                            }
                        }

                        if (codeSet.Count > 0 || element.Contents != codeSubmissionReceived.Code)
                        {
                            tryPrintCode(element.KernelName, element.Contents);
                            tryPrintCode(
                                $"{element.KernelName} - import",
                                ellipsisEnd(codeSubmissionReceived.Code, lengthLimit)
                            );
                        }
                        else
                        {
                            tryPrintCode(element.KernelName, element.Contents);
                        }

                        break;

                    // output / display events

                    case ErrorProduced errorProduced:
                        printCell(Color.Red, element.KernelName, element.Contents, ("error", errorProduced.Message));
                        outputs.Add(CreateErrorOutputElement(errorProduced));

                        break;

                    case StandardOutputValueProduced standardOutputValueProduced:

                        stdOut ??= new StringBuilder();
                        stdOut.Append(standardOutputValueProduced.PlainTextValue());

                        break;

                    case StandardErrorValueProduced standardErrorValueProduced:

                        stdErr ??= new StringBuilder();
                        stdErr.Append(standardErrorValueProduced.PlainTextValue());

                        break;

                    case DisplayedValueProduced displayedValueProduced:
                        printCell(null, element.KernelName, null, (null, displayedValueProduced.PlainTextValue()));
                        outputs.Add(CreateDisplayOutputElement(displayedValueProduced));

                        break;

                    case DisplayedValueUpdated displayedValueUpdated:
                        printCell(null, null, null, (null, displayedValueUpdated.PlainTextValue())!);
                        outputs.Add(CreateDisplayOutputElement(displayedValueUpdated));
                        break;

                    case ReturnValueProduced returnValueProduced:

                        if (returnValueProduced.Value is DisplayedValue)
                        {
                            break;
                        }

                        var text = returnValueProduced.PlainTextValue();
                        if (text.Contains("<style>"))
                        {
                            text = text.Replace("\\n", "<br />");
                        }

                        printCell(
                            Color.Green,
                            null,
                            null,
                            ("return value", ellipsisEnd(text, lengthLimit))
                        );

                        outputs.Add(CreateDisplayOutputElement(returnValueProduced));
                        break;

                    // command completion events

                    case CommandFailed failed when failed.Command == command:
                        if (CreateBufferedStandardOutAndErrElement(stdOut, stdErr) is { } te)
                        {
                            printCell(Color.Red, null, null, ("stderr", te.Text));
                            outputs.Add(te);
                        }

                        outputs.Add(CreateErrorOutputElement(failed));
                        tcs.SetResult();

                        new Thread(() =>
                        {
                            AnsiConsole.Console.WriteLine("NotebookRunner.RunNotebookAsync / exiting... 3");
                            Thread.Sleep(300);
                            AnsiConsole.Console.WriteLine("NotebookRunner.RunNotebookAsync / exiting... 2");
                            System.Console.Out.Flush();
                            Thread.Sleep(60);
                            AnsiConsole.Console.WriteLine("NotebookRunner.RunNotebookAsync / exiting... 1");
                            System.Diagnostics.Process.GetCurrentProcess().Kill();
                        }).Start();

                        break;

                    case CommandSucceeded succeeded when succeeded.Command == command:
                        if (CreateBufferedStandardOutAndErrElement(stdOut, stdErr) is { } textElement)
                        {
                            printCell(Color.Green, null, null, ("stdout", textElement.Text));
                            outputs.Add(textElement);
                        }

                        tcs.SetResult();

                        break;

                    case DiagnosticsProduced diagnostics:
                        printCell(Color.Yellow, command.TargetKernelName, null, (
                            "diagnostics",
                            diagnostics.FormattedDiagnostics
                                .Select(d => d.Value)
                                .Aggregate((a, b) => a + "\n" + b)
                        ));

                        break;

                    case PackageAdded package:
                        printCell(null, command.TargetKernelName, null, (null, $" Package added: {package.PackageReference}")!);

                        break;

                    default:
                        printCell(
                            Color.Red,
                            "unhandled",
                            null,
                            ("unhandled",
                            System.Text.Json.JsonSerializer.Serialize(new
                            {
                                Command = @event.Command,
                                str = @event.ToString()
                            }))
                        );

                        break;
                }
            });

            await tcs.Task;

            var resultElement = new InteractiveDocumentElement(element.Contents, element.KernelName, outputs.ToArray());
            /*
            resultElement.Metadata ??= new Dictionary<string, object>();
            resultElement.Metadata.Add("dotnet_repl_cellExecutionStartTime", startTime);
            resultElement.Metadata.Add("dotnet_repl_cellExecutionEndTime", DateTimeOffset.Now);
            resultElement.Metadata.Add("dotnet_repl_cellExecutionTime", elapsedText());
            */

            resultDocument.Add(resultElement);
        }

        var defaultKernelName = _kernel.DefaultKernelName;

        var defaultKernel = _kernel.ChildKernels.SingleOrDefault(k => k.Name == defaultKernelName);

        var languageName = defaultKernel?.KernelInfo.LanguageName ??
                           notebook.GetDefaultKernelName() ??
                           "Spiral";

        // resultDocument.WithJupyterMetadata(languageName);

        return resultDocument;
    }

    private TextElement? CreateBufferedStandardOutAndErrElement(
        StringBuilder? stdOut,
        StringBuilder? stdErr)
    {
        if (stdOut is null && stdErr is null)
        {
            return null;
        }

        var sb = new StringBuilder();

        if (stdOut is { })
        {
            sb.Append(stdOut);
        }

        if (stdOut is { } && stdErr is { })
        {
            sb.Append("\n\n");
        }

        if (stdErr is { })
        {
            sb.Append(stdErr);
        }

        return new TextElement(sb.ToString(), "stdout");
    }

    private DisplayElement CreateDisplayOutputElement(DisplayEvent displayedValueProduced) =>
        new(displayedValueProduced
            .FormattedValues
            .ToDictionary(
                v => v.MimeType,
                v => (object)v.Value));

    private ErrorElement CreateErrorOutputElement(ErrorProduced errorProduced) =>
        new(errorName: "Error",
            errorValue: errorProduced.Message);

    private ErrorElement CreateErrorOutputElement(CommandFailed failed) =>
        new(errorName: "Error",
            errorValue: failed.Message,
            stackTrace: failed.Exception switch
            {
                { } ex => (ex.StackTrace ?? "").SplitIntoLines(),
                _ => Array.Empty<string>()
            });
}
