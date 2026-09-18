using Grpc.Core;
using NinjagoScanner.Web.Components.Pages;

namespace NinjagoScanner.Web.Tests.Pages;

public sealed class ReviewReanalysisErrorMessageTests
{
    [Theory]
    [InlineData(StatusCode.Unavailable)]
    [InlineData(StatusCode.FailedPrecondition)]
    public void GetReanalysisErrorMessage_ExplainsUnavailability_ForServiceOutages(StatusCode statusCode)
    {
        var message = Review.GetReanalysisErrorMessage(new RpcException(new Status(statusCode, "raw detail")));

        Assert.Contains("nicht möglich", message);
        Assert.DoesNotContain("raw detail", message);
    }

    [Theory]
    [InlineData(StatusCode.NotFound)]
    [InlineData(StatusCode.Internal)]
    public void GetReanalysisErrorMessage_IsGeneric_ForOtherRpcFailures(StatusCode statusCode)
    {
        var message = Review.GetReanalysisErrorMessage(new RpcException(new Status(statusCode, "raw detail")));

        Assert.Equal("Die Neu-Analyse ist fehlgeschlagen.", message);
    }

    [Fact]
    public void GetReanalysisErrorMessage_IsGeneric_ForNonRpcExceptions()
    {
        var message = Review.GetReanalysisErrorMessage(new InvalidOperationException("boom"));

        Assert.Equal("Die Neu-Analyse ist fehlgeschlagen.", message);
    }
}
