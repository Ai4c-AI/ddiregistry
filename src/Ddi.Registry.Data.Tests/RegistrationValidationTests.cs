using Ddi.Registry.Data;
using Xunit;

namespace Ddi.Registry.Data.Tests;

public class RegistrationValidationTests
{
    [Fact]
    public void RequestedReferences_AreAllowed_ButNotPublishable()
    {
        var publishable = RegistrationValidation.IsVariablePublishable(
            ApprovalState.Requested,
            ApprovalState.Approved,
            ApprovalState.Approved);

        Assert.False(publishable);
    }

    [Fact]
    public void ApprovedVariable_WithApprovedDependencies_IsPublishable()
    {
        var publishable = RegistrationValidation.IsVariablePublishable(
            ApprovalState.Approved,
            ApprovalState.Approved,
            ApprovalState.Approved);

        Assert.True(publishable);
    }

    [Fact]
    public void CrossAgencyVariableReference_ShouldBeRejectedForNonAdminFlow()
    {
        var result = RegistrationValidation.ValidateVariableReferences(
            variableAgencyId: "us.demo",
            conceptAgencyId: "uk.demo",
            representationAgencyId: "us.demo",
            allowCrossAgency: false);

        Assert.False(result.IsValid);
        Assert.Equal("CrossAgencyReference", result.ErrorCode);
    }
}