using System.Text;
using Piko.Runtime.Ipc;

namespace Piko.Runtime.Tests;

public sealed class AgentConversationHistoryTests
{
    [Fact]
    public void BudgetKeepsWholeRecentTurnsAndTheCompleteCurrentQuestion()
    {
        var history = new AgentConversationHistory();
        history.Add(new string('A', 4000), "older answer");
        history.Add(new string('B', 3000), "recent answer");
        var question = new string('猫', 4000);
        var request = history.BuildRequest(question);
        Assert.True(request.Length <= 8192);
        Assert.DoesNotContain("AAAA", request);
        Assert.Contains(new string('B', 3000), request);
        Assert.EndsWith("User: " + question, request);
    }

    [Fact]
    public void NewConversationForgetsPreviousTurns()
    {
        var history = new AgentConversationHistory();
        history.Add("private previous question", "answer");
        history.Clear();
        Assert.Equal("new question", history.BuildRequest("new question"));
    }

    [Fact]
    public void RetainsOnlySixSuccessfulTurnsAndRejectsOversizedQuestions()
    {
        var history = new AgentConversationHistory();
        for (var i = 0; i < 8; i++) history.Add($"turn-{i}", $"answer-{i}");
        var request = history.BuildRequest("current");
        Assert.DoesNotContain("turn-0", request);
        Assert.DoesNotContain("turn-1", request);
        Assert.Contains("turn-2", request);
        Assert.Contains("turn-7", request);
        Assert.Throws<ArgumentException>(() => history.BuildRequest(new string('x', 4001)));
    }

    [Theory]
    [InlineData("oversized", 4)]
    [InlineData("partial", 100)]
    public async Task TransportRejectsOversizedOrIncompleteFrames(string text, int limit)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = new StreamReader(stream);
        await Assert.ThrowsAsync<InvalidDataException>(() => RuntimeIpcTransport.ReadLineAsync(reader, limit, default));
    }

    [Fact]
    public async Task TransportAcceptsAnExactLimitFrame()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("abcd\n"));
        using var reader = new StreamReader(stream);
        Assert.Equal("abcd", await RuntimeIpcTransport.ReadLineAsync(reader, 4, default));
    }
}
