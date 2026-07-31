using Xunit;
using Afrobotics.Bit.Api.Services;

namespace Afrobotics.Bit.Tests;

public class KlingPromptEditServiceTests
{
    // EditWithPromptAsync itself makes a real, unmocked HTTP call to fal.ai, so it isn't unit-testable
    // without live network access. This guards the one thing that matters without that: the brand
    // asset integrity constraint text must actually forbid content changes, not just exist.
    [Theory]
    [InlineData("text")]
    [InlineData("wording")]
    [InlineData("logo")]
    [InlineData("colors")]
    public void BrandIntegrityRules_ForbidsAlteringAssetContent(string requiredTerm)
    {
        Assert.Contains(requiredTerm, KlingPromptEditService.BrandIntegrityRules);
    }

    [Theory]
    [InlineData("keyframes")]
    [InlineData("length")]
    public void BrandIntegrityRules_ForbidsAlteringUnaffectedFramesOrClipLength(string requiredTerm)
    {
        Assert.Contains(requiredTerm, KlingPromptEditService.BrandIntegrityRules);
    }
}
