using System.Text;

namespace Piko.Runtime.Ipc;

/// <summary>In-memory conversation context; never stores tool output or incomplete turns.</summary>
public sealed class AgentConversationHistory
{
    public const int MaximumQuestionCharacters = 4000;
    private const int MaximumRequestCharacters = 8192;
    private readonly List<(string User, string Piko)> _turns = new();

    public void Clear() => _turns.Clear();

    public void Add(string question, string response)
    {
        ValidateQuestion(question);
        ArgumentNullException.ThrowIfNull(response);
        if (response.Length > 500) throw new ArgumentException("Response is too long.", nameof(response));
        _turns.Add((question, response));
        if (_turns.Count > 6) _turns.RemoveAt(0);
    }

    public string BuildRequest(string question)
    {
        ValidateQuestion(question);
        if (_turns.Count == 0) return question;
        const string prefix = "Recent conversation in this local window:\n";
        var current = "User: " + question;
        var remaining = MaximumRequestCharacters - prefix.Length - current.Length;
        var selected = new Stack<string>();
        foreach (var turn in _turns.AsEnumerable().Reverse())
        {
            var text = $"User: {turn.User}\nPiko: {turn.Piko}\n";
            if (text.Length > remaining) break;
            selected.Push(text);
            remaining -= text.Length;
        }
        if (selected.Count == 0) return question;
        var request = new StringBuilder(prefix);
        foreach (var text in selected) request.Append(text);
        return request.Append(current).ToString();
    }

    private static void ValidateQuestion(string question)
    {
        if (string.IsNullOrWhiteSpace(question) || question.Length > MaximumQuestionCharacters)
            throw new ArgumentException("Question must contain 1 to 4000 characters.", nameof(question));
    }
}
