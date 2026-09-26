using ShortcutForge.Core.Catalog;
using ShortcutForge.Dsl;
using Xunit;

namespace ShortcutForge.Dsl.Tests;

public class AiPromptTests
{
    [Fact]
    public void System_prompt_is_stable_and_lists_every_plain_action()
    {
        var prompt = AiPrompt.SystemPrompt;
        Assert.Same(prompt, AiPrompt.SystemPrompt); // built once, so the prompt cache always hits
        foreach (var action in ActionCatalog.Default.Actions.Where(a => a.Control == ControlFlowKind.None))
            Assert.Contains(action.Dsl + "(", prompt);
    }

    [Fact]
    public void Extracts_the_last_code_block()
    {
        var reply = "Here you go:\n```sfdsl\nAlert(\"first\")\n```\nFixed:\n```sfdsl\nAlert(\"second\")\n```\n";
        Assert.Equal("Alert(\"second\")", AiPrompt.ExtractCode(reply).Trim());
    }

    [Fact]
    public void Without_a_code_block_the_whole_reply_is_code()
    {
        Assert.Equal("Vibrate()", AiPrompt.ExtractCode("  Vibrate()  ").Trim());
    }

    [Fact]
    public void Repair_request_carries_description_code_and_error()
    {
        var request = AiPrompt.RepairRequest("buzz", "Vibrate(", "line 1: expected )");
        Assert.Contains("buzz", request);
        Assert.Contains("Vibrate(", request);
        Assert.Contains("expected )", request);
    }
}
