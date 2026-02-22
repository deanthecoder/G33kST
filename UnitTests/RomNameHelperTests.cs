// Code authored by Dean Edis (DeanTheCoder).
// Anyone is free to copy, modify, use, compile, or distribute this software,
// either in source code form or as a compiled binary, for any purpose.
//
// If you modify the code, please retain this copyright header,
// and consider contributing back to the repository or letting us know
// about your modifications. Your contributions are valued!
//
// THE SOFTWARE IS PROVIDED AS IS, WITHOUT WARRANTY OF ANY KIND.

using DTC.Emulation.Rom;

namespace UnitTests;

[TestFixture]
public sealed class RomNameHelperTests
{
    [Test]
    public void GetDisplayNameShouldStripPathExtensionAndMetadata()
    {
        var name = RomNameHelper.GetDisplayName("/Users/dean/Desktop/STROMS/Thunder Cats (1988)(Elite)[cr Loudness].st");

        Assert.That(name, Is.EqualTo("Thunder Cats"));
    }

    [Test]
    public void GetDisplayNameShouldReplaceUnderscoresWithSpaces()
    {
        var name = RomNameHelper.GetDisplayName("arkanoid_revenge_of_doh.zip");

        Assert.That(name, Is.EqualTo("Arkanoid Revenge of Doh"));
    }

    [Test]
    public void GetDisplayNameShouldStripKnownPublisherSuffixFromSlug()
    {
        var name = RomNameHelper.GetDisplayName("mega_lo_mania_imageworks.zip");

        Assert.That(name, Is.EqualTo("Mega Lo Mania"));
    }

    [Test]
    public void GetDisplayNameShouldStripLanguageSuffixFromSlug()
    {
        var name = RomNameHelper.GetDisplayName("prince_of_persia_fr.zip");

        Assert.That(name, Is.EqualTo("Prince of Persia"));
    }

    [Test]
    public void GetDisplayNameShouldKeepRomanNumeralsUppercaseInSlug()
    {
        var name = RomNameHelper.GetDisplayName("xenon_ii_-_megablast_imageworks.zip");

        Assert.That(name, Is.EqualTo("Xenon II - Megablast"));
    }

    [Test]
    public void GetDisplayNameShouldKeepLeadingArticleCapitalized()
    {
        var name = RomNameHelper.GetDisplayName("the_secret_of_monkey_island.zip");

        Assert.That(name, Is.EqualTo("The Secret of Monkey Island"));
    }

    [Test]
    public void GetDisplayNameShouldTitleCasePlainLowercaseName()
    {
        var name = RomNameHelper.GetDisplayName("nebulus.zip");

        Assert.That(name, Is.EqualTo("Nebulus"));
    }
}
