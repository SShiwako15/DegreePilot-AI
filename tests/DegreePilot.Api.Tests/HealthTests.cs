namespace DegreePilot.Api.Tests;

public class HealthTests
{
    [Fact]
    public async Task LivenessResponds()
    {
        await using var factory = new AcademicApiFactory();
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();
    }
}
