using Piko.Context.Events;
using System.Text.Json;

namespace Piko.Agent.Tools;

public sealed record AgentToolDescriptor(
    string Name,
    string Description,
    AgentToolRisk Risk,
    ContextCapability RequiredCapability,
    bool SupportsDryRun = false,
    string InputJsonSchema = "{\"type\":\"object\",\"additionalProperties\":false}")
{
    public AgentToolDescriptor Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 128 ||
            Name.Any(character => !(char.IsLetterOrDigit(character) || character is '.' or '-' or '_')))
        {
            throw new ArgumentException("Agent tool names must be stable identifiers up to 128 characters.", nameof(Name));
        }

        if (string.IsNullOrWhiteSpace(Description) || Description.Length > 512)
        {
            throw new ArgumentException("Agent tools require a concise description.", nameof(Description));
        }

        if (string.IsNullOrWhiteSpace(InputJsonSchema) || InputJsonSchema.Length > 8192)
        {
            throw new ArgumentException("Agent tool input schema is required and must be bounded.", nameof(InputJsonSchema));
        }

        try
        {
            using var schema = JsonDocument.Parse(InputJsonSchema);
            if (schema.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ArgumentException("Agent tool input schema must be a JSON object.", nameof(InputJsonSchema));
            }
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Agent tool input schema is invalid JSON.", nameof(InputJsonSchema), exception);
        }

        return this;
    }
}
