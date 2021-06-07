using System;
using RadLine;
using Spectre.Console;

namespace dotnet_repl
{
    public class Theme
    {
        public Style AnnouncementTextStyle { get; set; } = new(Color.SandyBrown);

        public Style AnnouncementBorderStyle { get; set; } = new(Color.Aqua);

        public static Theme Default { get; set; } = new();
    }

    public abstract class KernelSpecificTheme : Theme
    {
        public abstract Style AccentStyle { get; }

        public Style ErrorOutputBorderStyle { get; set; } = new(Color.Red);

        public Style SuccessOutputBorderStyle { get; set; } = new(Color.Green);

        public abstract string PromptText { get; }

        public abstract ILineEditorPrompt Prompt { get; }

        public IStatusMessageGenerator StatusMessageGenerator { get; set; } = new SillyExecutionStatusMessageGenerator();

        public static KernelSpecificTheme GetTheme(string kernelName) => kernelName switch
        {
            "csharp" => new CSharpTheme(),
            "fsharp" => new FSharpTheme(),
            _ => throw new ArgumentOutOfRangeException(nameof(kernelName), kernelName, null)
        };
    }

    public class CSharpTheme : KernelSpecificTheme
    {
        public override Style AccentStyle => new(Color.Aqua);

        public override string PromptText => "C#";

        public override ILineEditorPrompt Prompt => new LineEditorPrompt(
            $"[{AnnouncementTextStyle.Foreground}]{PromptText} [/][{Decoration.Bold} {AccentStyle.Foreground} {Decoration.SlowBlink}]>[/]",
            $"[{Decoration.Bold} {AccentStyle.Foreground} {Decoration.SlowBlink}] ...[/]");
    }

    public class FSharpTheme : KernelSpecificTheme
    {
        public override Style AccentStyle => new(Color.Magenta1);

        public override string PromptText => "F#";

        public override ILineEditorPrompt Prompt => new LineEditorPrompt(
            $"[{AnnouncementTextStyle.Foreground}]{PromptText} [/][{Decoration.Bold} {AccentStyle.Foreground} {Decoration.SlowBlink}]>[/]",
            $"[{Decoration.Bold} {AccentStyle.Foreground} {Decoration.SlowBlink}] ...[/]");
    }
}