using System.Net.Http.Json;
using System.Text.Json;
using DegreePilot.Api.Domain;

namespace DegreePilot.Api.Application;

public interface IPlannerClient
{
    Task<SolverResult> SolveAsync(SolverRequest request, CancellationToken cancellationToken);
}

public sealed class PlannerClient(HttpClient http) : IPlannerClient
{
    public async Task<SolverResult> SolveAsync(SolverRequest request, CancellationToken cancellationToken)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            using var message = new HttpRequestMessage(HttpMethod.Post, "solve") { Content = JsonContent.Create(request) };
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return new("ERROR", []);
            // Bound an untrusted internal response before deserializing it.
            await response.Content.LoadIntoBufferAsync(65536, deadline.Token);
            return await response.Content.ReadFromJsonAsync<SolverResult>(deadline.Token) ?? new("ERROR", []);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return new("TIMEOUT", []); }
        catch (HttpRequestException) { return new("UNAVAILABLE", []); }
        catch (JsonException) { return new("ERROR", []); }
    }
}
