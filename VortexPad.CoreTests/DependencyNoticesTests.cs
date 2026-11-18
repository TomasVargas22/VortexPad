using VortexPad.Core.Diagnostics;
using Xunit;

namespace VortexPad.CoreTests;

public class DependencyNoticesTests
{
    private static DependencyState AllGood => new(
        ViGEmBusInstalled: true, HidHideInstalled: true, IsElevated: true,
        ProfileWantsHidHide: true, ProfileNeedsViGEm: true);

    [Fact]
    public void EverythingInstalledAndElevated_NoNotices()
    {
        Assert.Empty(DependencyNotices.Evaluate(AllGood));
    }

    [Fact]
    public void MissingViGEm_IsAnError_WithDownloadLink()
    {
        var notice = Assert.Single(DependencyNotices.Evaluate(AllGood with { ViGEmBusInstalled = false }));
        Assert.Equal("vigembus", notice.Id);
        Assert.Equal(NoticeSeverity.Error, notice.Severity);
        Assert.Equal(NoticeAction.OpenUrl, notice.Action);
        Assert.Equal(DependencyNotices.ViGEmBusUrl, notice.Url);
    }

    [Fact]
    public void MissingViGEm_DoesNotMatter_InKeyboardMouseMode()
    {
        Assert.Empty(DependencyNotices.Evaluate(AllGood with { ViGEmBusInstalled = false, ProfileNeedsViGEm = false }));
    }

    [Fact]
    public void Elevation_IsRequestedOnlyWhenHidHideWillBeUsed()
    {
        var notElevated = AllGood with { IsElevated = false };

        var notice = Assert.Single(DependencyNotices.Evaluate(notElevated));
        Assert.Equal(NoticeAction.RestartElevated, notice.Action);

        // Profile without HidHide → never asks for administrator
        Assert.Empty(DependencyNotices.Evaluate(notElevated with { ProfileWantsHidHide = false }));
        // HidHide not installed → offers to download it, not to elevate (it would be useless)
        var hidHide = Assert.Single(DependencyNotices.Evaluate(notElevated with { HidHideInstalled = false }));
        Assert.Equal("hidhide", hidHide.Id);
        Assert.Equal(NoticeSeverity.Info, hidHide.Severity);
    }

    [Fact]
    public void SeveralProblems_ShowSeveralNotices_InOrder()
    {
        var notices = DependencyNotices.Evaluate(new DependencyState(false, false, false, true, true));
        Assert.Equal(new[] { "vigembus", "hidhide" }, notices.Select(n => n.Id));
    }
}
