using ShortcutForge.App.Services;
using Xunit;

namespace ShortcutForge.App.Tests;

public class ShortcutAiServiceTests
{
    [Fact]
    public async Task Missing_key_fails_before_any_request()
    {
        await Assert.ThrowsAsync<AiGenerationException>(() => new ShortcutAiService("").GenerateAsync("vibrate"));
    }
}
