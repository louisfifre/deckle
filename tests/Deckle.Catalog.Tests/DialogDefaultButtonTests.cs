using Deckle.Catalog;
using Microsoft.UI.Xaml.Controls;
using Xunit;

namespace Deckle.Catalog.Tests;

// Enter picks the default button, so the default is the safety decision: a
// consent or a destructive confirmation must default to Cancel (Close), and
// only a plain confirmation keeps its verb as default.
[Trait("Category", "unit")]
public class DialogDefaultButtonTests
{
    [Fact]
    public void AConsentDefaultsToCancel()
    {
        var request = new ConsentRequest("t", "intro", "what", "where", "remind");

        Assert.Equal(ContentDialogButton.Close, request.DefaultButton);
    }

    [Fact]
    public void AConsentReadsIntroWhatWhereRemindInThatOrder()
    {
        var request = new ConsentRequest("t", "intro", "what", "where", "remind");

        Assert.Equal(
            new (string?, string)[]
            {
                (null, "intro"),
                ("Common_Consent_WhatHeader", "what"),
                ("Common_Consent_WhereHeader", "where"),
                ("Common_Consent_RemindHeader", "remind"),
            },
            request.Sections.Select(s => (s.HeaderKey, s.Text)));
    }

    [Fact]
    public void ADestructiveConfirmationDefaultsToCancel()
    {
        var request = new ConfirmationRequest("t", "b", "Delete", IsDestructive: true);

        Assert.Equal(ContentDialogButton.Close, request.DefaultButton);
    }

    [Fact]
    public void APlainConfirmationKeepsItsVerbAsDefault()
    {
        var request = new ConfirmationRequest("t", "b", "Apply");

        Assert.Equal(ContentDialogButton.Primary, request.DefaultButton);
    }
}
