using System;
using System.Collections.Generic;
using System.Linq;
using Hollow.Client;
using Hollow.Net.Commands;

namespace Hollow.Tests;

public static class AdmireBindingTests
{
    [Test]
    public static void AdmireDefaultsToNAndRemainsALocalNonAdminCommand()
    {
        var bindings = new ConsoleBindings();
        Assert.Equal("admire", bindings.Find("n"), "new players receive the physical N shortcut");
        var parsed = ConsoleCatalog.Parse(bindings.Find("N")!);
        Assert.True(parsed.Command is { Side: CommandSide.Client, Admin: false } && parsed.Args.Count == 0,
            "cosmetic admiration neither sends a server intent nor requires admin rights");
    }

    [Test]
    public static void AdmireDefaultMergesWithLegacyBindingsWithoutReplacingOverrides()
    {
        var bindings = new ConsoleBindings();
        bindings.Restore(new Dictionary<string, string> { ["F6"] = "noclip", ["shift+x"] = "fps" });
        Assert.Equal("admire", bindings.Find("N"), "a pre-feature save receives the new default");
        Assert.Equal("noclip", bindings.Find("F6"), "existing admin shortcut is preserved");
        Assert.Equal("fps", bindings.Find("SHIFT+X"), "existing modifier shortcut is preserved");
        bindings.Restore(new Dictionary<string, string> { ["n"] = "netgraph", ["j"] = "admire" });
        Assert.Equal("netgraph", bindings.Find("N"), "the player's saved N binding wins over the default");
        Assert.Equal("admire", bindings.Find("J"), "a custom admire shortcut survives merging");
    }

    [Test]
    public static void AdmireUnbindAndRemapSurviveRestart()
    {
        var bindings = new ConsoleBindings();
        Assert.True(bindings.Remove("n"), "the default can be explicitly removed");
        Assert.True(bindings.Set("j", "admire", out _), "admire can move to another key");
        var restarted = new ConsoleBindings();
        restarted.Restore(bindings.Commands.ToArray(), bindings.UnboundDefaults.ToArray());
        Assert.True(restarted.Find("N") == null, "restart must not restore an explicitly unbound default");
        Assert.Equal("admire", restarted.Find("J"), "restart retains the selected replacement");
        Assert.True(restarted.Set("N", "admire", out _) && !restarted.UnboundDefaults.Contains("N"),
            "rebinding a default clears its removal marker");
        var rebound = new ConsoleBindings();
        rebound.Restore(restarted.Commands.ToArray(), restarted.UnboundDefaults.ToArray());
        Assert.Equal("admire", rebound.Find("N"), "the rebound default survives another restart");
    }

    [Test]
    public static void AdmireBindingRestoreHandlesEmptyOrConflictingSavedState()
    {
        var bindings = new ConsoleBindings();
        bindings.Restore(Array.Empty<KeyValuePair<string, string>>(), new[] { "N" });
        Assert.True(bindings.Commands.Count == 0, "a file with only the unbound section keeps N disabled");
        bindings.Restore(new Dictionary<string, string> { ["N"] = "fps" }, new[] { "N" });
        Assert.Equal("fps", bindings.Find("N"), "a concrete saved override takes precedence over a stale removal");
        Assert.True(!bindings.UnboundDefaults.Contains("N"), "the stale removal is cleared on restore");
    }
}
