using System.Net.Http.Json;
using System.Text.Json;
using DegreePilot.Api.Domain;

namespace DegreePilot.Api.Application;

public interface IAdvisorClient
{
    Task<AdvisorStatus> StatusAsync(CancellationToken ct);
    Task<ProviderReply> AnswerAsync(AdvisorEnvelope request, CancellationToken ct);
}

public sealed class AdvisorClient(HttpClient http) : IAdvisorClient
{
    public async Task<AdvisorStatus> StatusAsync(CancellationToken ct)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            return await http.GetFromJsonAsync<AdvisorStatus>("advisor/status", deadline.Token)
                ?? new(false, "AI advising is unavailable. Deterministic features remain available.");
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
        { return new(false, "AI advising is unavailable. Deterministic features remain available."); }
    }

    public async Task<ProviderReply> AnswerAsync(AdvisorEnvelope request, CancellationToken ct)
    {
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(25));
            using var message = new HttpRequestMessage(HttpMethod.Post, "advisor/answer") { Content = JsonContent.Create(request) };
            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
            if (!response.IsSuccessStatusCode) return new("ERROR", null, [], []);
            await response.Content.LoadIntoBufferAsync(32768, deadline.Token);
            return await response.Content.ReadFromJsonAsync<ProviderReply>(deadline.Token) ?? new("ERROR", null, [], []);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException || e is OperationCanceledException && !ct.IsCancellationRequested)
        { return new("UNAVAILABLE", null, [], []); }
    }
}
