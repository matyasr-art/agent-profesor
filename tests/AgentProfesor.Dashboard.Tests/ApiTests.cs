using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AgentProfesor.Dashboard.Tests;

/// <summary>
/// Integrační testy dashboard API: bootnou celou ASP.NET Core aplikaci v paměti
/// (WebApplicationFactory) v demo režimu (naseedovaná dočasná DB) a ověří kontrakt endpointů
/// end-to-end přes skutečné HTTP volání a skutečné jádro + SQLite.
/// </summary>
public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Meta_reports_demo_mode()
    {
        var meta = await _client.GetFromJsonAsync<JsonElement>("/api/meta");
        Assert.True(meta.GetProperty("demo").GetBoolean());
    }

    [Fact]
    public async Task Stats_reports_the_seeded_totals()
    {
        var s = await _client.GetFromJsonAsync<JsonElement>("/api/stats");
        Assert.True(s.GetProperty("documents").GetInt32() >= 3);
        Assert.True(s.GetProperty("versions").GetInt32() >= 9);
        Assert.True(s.GetProperty("storedBytes").GetInt64() > 0);
        Assert.True(s.GetProperty("keyframes").GetInt32() >= 1);
    }

    [Fact]
    public async Task Documents_include_the_proposal()
    {
        var docs = await _client.GetFromJsonAsync<JsonElement>("/api/documents");
        var titles = docs.EnumerateArray().Select(d => d.GetProperty("title").GetString()).ToList();
        Assert.Contains(titles, t => t != null && t.Contains("Nabídka Zikmundov"));
    }

    [Fact]
    public async Task Proposal_has_six_versions_with_keyframe_and_diff_mix()
    {
        var docs = await _client.GetFromJsonAsync<JsonElement>("/api/documents");
        var proposalId = docs.EnumerateArray()
            .First(d => d.GetProperty("title").GetString()!.Contains("Nabídka Zikmundov"))
            .GetProperty("id").GetInt64();

        var versions = await _client.GetFromJsonAsync<JsonElement>($"/api/documents/{proposalId}/versions");
        var list = versions.EnumerateArray().ToList();
        Assert.Equal(6, list.Count);
        Assert.Contains(list, v => v.GetProperty("isKeyframe").GetBoolean());
        Assert.Contains(list, v => !v.GetProperty("isKeyframe").GetBoolean());
    }

    [Fact]
    public async Task Diff_against_previous_version_returns_marked_lines()
    {
        var docs = await _client.GetFromJsonAsync<JsonElement>("/api/documents");
        var proposalId = docs.EnumerateArray()
            .First(d => d.GetProperty("title").GetString()!.Contains("Nabídka Zikmundov"))
            .GetProperty("id").GetInt64();
        var versions = (await _client.GetFromJsonAsync<JsonElement>($"/api/documents/{proposalId}/versions"))
            .EnumerateArray().ToList();

        var v5 = versions[4].GetProperty("id").GetInt64();
        var v4 = versions[3].GetProperty("id").GetInt64();

        var diff = await _client.GetFromJsonAsync<JsonElement>($"/api/versions/{v5}/diff?base={v4}");
        Assert.False(diff.GetProperty("first").GetBoolean());
        var lines = diff.GetProperty("lines").EnumerateArray().ToList();
        Assert.NotEmpty(lines);
        // Verze 5 přidává harmonogram → musí tam být přidaný (+) řádek s tím slovem.
        Assert.Contains(lines, l => l.GetProperty("marker").GetString() == "+"
            && l.GetProperty("line").GetString()!.Contains("Harmonogram"));
    }

    [Fact]
    public async Task Search_finds_hits_across_documents()
    {
        var hits = await _client.GetFromJsonAsync<JsonElement>("/api/search?q=harmonogram");
        Assert.True(hits.GetArrayLength() >= 2);
    }

    [Fact]
    public async Task Search_matches_partial_word()
    {
        var hits = await _client.GetFromJsonAsync<JsonElement>("/api/search?q=schůzk");
        Assert.True(hits.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Version_text_endpoint_returns_the_reconstructed_document()
    {
        var docs = await _client.GetFromJsonAsync<JsonElement>("/api/documents");
        var proposalId = docs.EnumerateArray()
            .First(d => d.GetProperty("title").GetString()!.Contains("Nabídka Zikmundov"))
            .GetProperty("id").GetInt64();
        var versions = (await _client.GetFromJsonAsync<JsonElement>($"/api/documents/{proposalId}/versions"))
            .EnumerateArray().ToList();
        var lastId = versions[^1].GetProperty("id").GetInt64();

        var resp = await _client.GetAsync($"/api/versions/{lastId}/text");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var text = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Harmonogram", text);
        Assert.Contains("bez DPH", text);
    }
}
