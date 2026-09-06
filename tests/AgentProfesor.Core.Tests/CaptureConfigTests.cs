using AgentProfesor.Core;
using Xunit;

namespace AgentProfesor.Core.Tests;

public class CaptureConfigTests
{
    [Fact]
    public void Default_allowlist_permits_document_apps_case_insensitively()
    {
        var config = new CaptureConfig(); // AppAllowlist == null → výchozí sada

        Assert.True(config.IsCaptureAllowed("WINWORD"));
        Assert.True(config.IsCaptureAllowed("winword"));   // nerozlišuje velikost písmen
        Assert.True(config.IsCaptureAllowed("OUTLOOK"));
        Assert.True(config.IsCaptureAllowed("POWERPNT"));  // PowerPoint (přednášky)
        Assert.True(config.IsCaptureAllowed("notepad"));
    }

    [Theory]
    [InlineData("chrome")]
    [InlineData("firefox")]
    [InlineData("msedge")]
    [InlineData("Telegram")]
    [InlineData("1Password")]
    [InlineData("explorer")]
    public void Default_allowlist_blocks_browsers_chats_and_password_managers(string app)
    {
        var config = new CaptureConfig();
        Assert.False(config.IsCaptureAllowed(app));
    }

    [Fact]
    public void Explicit_allowlist_is_respected()
    {
        var config = new CaptureConfig { AppAllowlist = new[] { "kate", "gedit" } };

        Assert.True(config.IsCaptureAllowed("kate"));
        Assert.True(config.IsCaptureAllowed("GEDIT"));
        Assert.False(config.IsCaptureAllowed("WINWORD")); // vlastní seznam přebíjí výchozí
    }

    [Fact]
    public void Empty_allowlist_disables_capture_everywhere()
    {
        var config = new CaptureConfig { AppAllowlist = System.Array.Empty<string>() };

        Assert.False(config.IsCaptureAllowed("WINWORD"));
        Assert.False(config.IsCaptureAllowed("notepad"));
        Assert.Empty(config.EffectiveAllowlist);
    }

    [Fact]
    public void EffectiveAllowlist_falls_back_to_default_when_unset()
    {
        Assert.Equal(CaptureConfig.DefaultAppAllowlist, new CaptureConfig().EffectiveAllowlist);
    }
}
