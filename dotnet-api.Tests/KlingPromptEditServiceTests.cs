using Xunit;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests;

public class KlingPromptEditServiceTests
{
    // EditWithPromptAsync itself makes a real, unmocked HTTP call to fal.ai, so it isn't unit-testable
    // without live network access. This guards the one thing that matters without that: the constraint
    // text actually asks the model to preserve the asset and the rest of the scene/clip.
    [Theory]
    [InlineData("asset")]
    [InlineData("scene")]
    [InlineData("clip")]
    public void BrandIntegrityRules_AsksModelToPreserveAssetAndScene(string requiredTerm)
    {
        Assert.Contains(requiredTerm, KlingPromptEditService.BrandIntegrityRules);
    }

    [Fact]
    public void BrandIntegrityRules_StaysShort()
    {
        // A prior, longer version (itemizing text/wording/logo/colors/keyframes/regions/frames)
        // was observed live to make Kling overcorrect and edit unrelated parts of the scene.
        // Guard against reintroducing that by capping length — short, terse constraints stay
        // closer to the user's actual request.
        Assert.True(KlingPromptEditService.BrandIntegrityRules.Length < 150,
            $"BrandIntegrityRules is {KlingPromptEditService.BrandIntegrityRules.Length} chars — " +
            "long prompts have caused the model to overcorrect in the past, keep this terse.");
    }
}
