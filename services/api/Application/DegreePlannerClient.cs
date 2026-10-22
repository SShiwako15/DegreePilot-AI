using System.Net.Http.Json;
using System.Text.Json;
using DegreePilot.Api.Domain;

namespace DegreePilot.Api.Application;

public interface IDegreePlannerClient
{
    Task<DegreeSolverReply> SolveAsync(DegreeSolverRequest request, CancellationToken ct);
}

public sealed class DegreePlannerClient(HttpClient http) : IDegreePlannerClient
{
    public async Task<DegreeSolverReply> SolveAsync(DegreeSolverRequest request, CancellationToken ct)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(12));
            using var message = new HttpRequestMessage(HttpMethod.Post, "degree/solve") { Content = JsonContent.Create(request) };
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return new("ERROR", []);
            await response.Content.LoadIntoBufferAsync(262144, deadline.Token);
            return await response.Content.ReadFromJsonAsync<DegreeSolverReply>(deadline.Token) ?? new("ERROR", []);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { return new("TIMEOUT", []); }
        catch (HttpRequestException) { return new("UNAVAILABLE", []); }
        catch (JsonException) { return new("ERROR", []); }
    }
}
