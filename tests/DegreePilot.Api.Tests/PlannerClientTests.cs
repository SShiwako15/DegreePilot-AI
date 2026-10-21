using System.Net;
using DegreePilot.Api.Application;
using DegreePilot.Api.Domain;

namespace DegreePilot.Api.Tests;

public class PlannerClientTests
{
    private sealed class Handler(string behavior) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (behavior == "offline") throw new HttpRequestException("offline");
            if (behavior == "timeout") throw new TaskCanceledException();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{broken") });
        }
    }

    [Theory]
    [InlineData("offline", "UNAVAILABLE")]
    [InlineData("timeout", "TIMEOUT")]
    [InlineData("malformed", "ERROR")]
    public async Task TransportFailuresDoNotFabricatePlans(string behavior, string status)
    {
        using var http = new HttpClient(new Handler(behavior)) { BaseAddress = new("http://planner/") };
        var response = await new PlannerClient(http).SolveAsync(new([], 12, 18, [], true), CancellationToken.None);
        Assert.Equal(status, response.Status);
        Assert.Empty(response.SelectionIds);
    }
}
