﻿using System.Linq;
using System.Text;
using Microsoft.DotNet.Interactive;
using Microsoft.DotNet.Interactive.Events;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace dotnet_repl;

internal static class AnsiConsoleExtensions
{
    public static void Announce(
        this IAnsiConsole ansiConsole,
        string text)
    {
        ansiConsole.Write(
            new Panel(
                    new Paragraph(text, Theme.Default.AnnouncementTextStyle))
                .BorderStyle(Theme.Default.AnnouncementBorderStyle)
                .HeavyBorder()
                .Expand());
    }

    public static void Announce(
        this IAnsiConsole ansiConsole,
        IRenderable content)
    {
        ansiConsole.Write(
            new Panel(content)
                .BorderStyle(Theme.Default.AnnouncementBorderStyle)
                .HeavyBorder()
                .Expand());
    }

    public static IRenderable GetErrorDisplay(
        DisplayEvent @event,
        KernelSpecificTheme theme,
        string header = "❌") =>
        new Panel(GetMarkup(@event))
            .Header(header)
            .Expand()
            .RoundedBorder()
            .BorderStyle(theme.ErrorOutputBorderStyle);

    public static IRenderable GetErrorDisplay(
        CommandFailed failed,
        KernelSpecificTheme theme,
        string header = "❌") =>
        new Panel(failed.Exception?.GetRenderable() ?? new Text(failed.Message ?? ""))
            .Header(header)
            .Expand()
            .RoundedBorder()
            .BorderStyle(theme.ErrorOutputBorderStyle);

    public static Panel GetErrorDisplay(
        string message,
        KernelSpecificTheme theme,
        string header = "❌")
    {
        return new Panel(Markup.Escape(message))
               .Header(header)
               .Expand()
               .RoundedBorder()
               .BorderStyle(theme.ErrorOutputBorderStyle);
    }

    public static Panel GetSuccessDisplay(
        DisplayEvent @event,
        KernelSpecificTheme theme,
        string header = "✔")
    {
        return new Panel(GetMarkup(@event))
               .Header(header)
               .Expand()
               .RoundedBorder()
               .BorderStyle(theme.SuccessOutputBorderStyle);
    }

    public static Panel GetSuccessDisplay(
        string message,
        string header,
        KernelSpecificTheme theme)
    {
        return new Panel(Markup.Escape(message))
               .Header(header)
               .Expand()
               .RoundedBorder()
               .BorderStyle(theme.SuccessOutputBorderStyle);
    }

    public static void RenderErrorMessage(
        this IAnsiConsole ansiConsole,
        string message,
        KernelSpecificTheme theme,
        string header = "❌")
    {
        ansiConsole.Write(GetErrorDisplay(message, theme, header));
    }

    public static void RenderSuccessMessage(
        this IAnsiConsole ansiConsole,
        string message,
        KernelSpecificTheme theme,
        string header = "✔")
    {
        ansiConsole.Write(GetSuccessDisplay(message, header, theme));
    }

    public static void RenderBufferedStandardOutAndErr(
        this IAnsiConsole ansiConsole,
        KernelSpecificTheme theme,
        StringBuilder? stdOut = null,
        StringBuilder? stdErr = null)
    {
        if (stdOut is { })
        {
            ansiConsole.RenderSuccessMessage(stdOut.ToString(), theme, "✒");
        }

        if (stdErr is { })
        {
            ansiConsole.RenderErrorMessage(stdErr.ToString(), theme, "✒");
        }
    }

    private static Markup GetMarkup(DisplayEvent @event)
    {
        var formattedValue = @event.FormattedValues.FirstOrDefault() ?? new FormattedValue("text/plain", "");

        var markup = formattedValue.MimeType switch
        {
            "text/plain+spectre" => new Markup(formattedValue.Value),
            _ => new Markup(Markup.Escape(formattedValue.Value))
        };

        return markup;
    }
}
