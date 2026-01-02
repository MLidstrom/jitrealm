using JitRealm.Mud.Formatting;
using Spectre.Console;
using Xunit;

namespace JitRealm.Tests;

public sealed class SpectreSessionRendererTests
{
    [Fact]
    public void Render_NormalizesLineEndingsToCrlf()
    {
        var output = SpectreSessionRenderer.Render(
            console =>
            {
                console.MarkupLine("hello");
                console.MarkupLine("world");
            },
            new SpectreRenderOptions(
                EnableAnsi: false,
                EnableUnicode: true,
                Width: 80,
                Height: 24));

        Assert.Contains("\r\n", output);

        // No bare LF/CR should remain after normalization.
        Assert.DoesNotContain("\n", output.Replace("\r\n", ""));
        Assert.DoesNotContain("\r", output.Replace("\r\n", ""));
    }

    [Fact]
    public void Render_WhenAnsiDisabled_EmitsNoEscapeCodes()
    {
        var output = SpectreSessionRenderer.Render(
            console => console.MarkupLine("[red]hi[/]"),
            new SpectreRenderOptions(
                EnableAnsi: false,
                EnableUnicode: true,
                Width: 80,
                Height: 24));

        Assert.True(!output.Contains('\u001b'), $"Output contained ESC: {output.Replace("\u001b", "<ESC>")}");
        Assert.Contains("hi", output);
    }

    [Fact]
    public void Render_WhenAnsiEnabled_EndsWithResetIgnoringTrailingCrlf()
    {
        var output = SpectreSessionRenderer.Render(
            console => console.MarkupLine("[red]hi[/]"),
            new SpectreRenderOptions(
                EnableAnsi: true,
                EnableUnicode: true,
                Width: 80,
                Height: 24,
                ColorSystem: ColorSystemSupport.Standard));

        // Strip trailing CRLF so we can assert on the "final" control sequence.
        while (output.EndsWith("\r\n", StringComparison.Ordinal))
            output = output[..^2];

        Assert.EndsWith("\u001b[0m", output);
    }
}


