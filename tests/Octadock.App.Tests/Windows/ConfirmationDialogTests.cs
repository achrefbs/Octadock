using FluentAssertions;
using Octadock.App.Windows;
using Xunit;

namespace Octadock.App.Tests.Windows;

public sealed class ConfirmationDialogTests
{
    [Fact]
    public void Headless_opt_in_prompt_declines()
        => ConfirmationDialog.Ask(
                owner: null,
                title: "Send reviewed text?",
                message: "Nothing should be sent without a visible confirmation.",
                primaryText: "Send",
                cancelText: "Don’t send")
            .Should()
            .BeFalse();

    [Fact]
    public void Headless_three_way_prompt_returns_cancel()
        => ConfirmationDialog.Choose(
                owner: null,
                title: "Unsaved annotations",
                message: "Save before closing?",
                primaryText: "Save",
                secondaryText: "Discard",
                cancelText: "Cancel")
            .Should()
            .Be(ConfirmationDialogChoice.Cancel);
}
