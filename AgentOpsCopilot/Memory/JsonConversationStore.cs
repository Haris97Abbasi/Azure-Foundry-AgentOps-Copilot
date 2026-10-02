using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgentOpsCopilot.Memory;

public sealed record StoredMessage(string Role, string Content, DateTimeOffset Timestamp);

public sealed class JsonConversationStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly string _path;
    private readonly int _maxMessages;
    private readonly TimeProvider _clock;
    private readonly List<StoredMessage> _messages;

    public JsonConversationStore(string path, int maxMessages = 10, TimeProvider? clock = null)
    {
        _path = path;
        _maxMessages = maxMessages;
        _clock = clock ?? TimeProvider.System;
        _messages = Load();
    }

    public string Path => _path;

    public string? LoadWarning { get; private set; }

    public IReadOnlyList<ChatMessage> Messages =>
        _messages.Select(m => new ChatMessage(new ChatRole(m.Role), m.Content)).ToList();

    public void AddExchange(string userMessage, string assistantMessage)
    {
        var now = _clock.GetUtcNow();
        _messages.Add(new StoredMessage(ChatRole.User.Value, userMessage, now));
        _messages.Add(new StoredMessage(ChatRole.Assistant.Value, assistantMessage, now));

        var excess = _messages.Count - _maxMessages;
        if (excess > 0) _messages.RemoveRange(0, excess);

        Save();
    }

    public void Clear()
    {
        _messages.Clear();
        Save();
    }

    private List<StoredMessage> Load()
    {
        if (!File.Exists(_path)) return [];

        try
        {
            var loaded = JsonSerializer.Deserialize<List<StoredMessage>>(File.ReadAllText(_path), Json) ?? [];
            var valid = loaded.Where(m => m.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(m.Content)).ToList();
            return valid.Skip(Math.Max(0, valid.Count - _maxMessages)).ToList();
        }
        catch (JsonException)
        {
            var backup = _path + ".corrupt";
            File.Move(_path, backup, overwrite: true);
            LoadWarning = $"Session file was unreadable and has been moved to {backup}; starting a new conversation.";
            return [];
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_messages, Json));
        File.Move(temp, _path, overwrite: true);
    }
}
