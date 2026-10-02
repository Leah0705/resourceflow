using System.Text.Json;
using System.Text.Json.Serialization;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Core.Application.Services;

/// <summary>
/// Reads and writes <see cref="Venue.DurationRulesJson"/>. Resolving a party's slot length
/// is <see cref="BookingDuration.For"/>; this class only owns the stored shape.
/// </summary>
public static class DurationRulesHelper
{
    private sealed class Rule
    {
        [JsonPropertyName("minPartySize")]
        public int MinPartySize { get; set; }

        [JsonPropertyName("minutes")]
        public int Minutes { get; set; }
    }

    /// <summary>The stored rules ordered by party size, or empty when there are none or the JSON is unreadable.</summary>
    public static List<DurationRuleDto> Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return (JsonSerializer.Deserialize<List<Rule>>(json) ?? [])
                .Where(r => r != null)
                .Select(r => new DurationRuleDto { MinPartySize = r.MinPartySize, Minutes = r.Minutes })
                .OrderBy(r => r.MinPartySize)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// Validates and stores the rules. An empty list clears them, so every party gets
    /// <see cref="Venue.DefaultBookingDurationMinutes"/> again.
    /// </summary>
    /// <seealso>DurationRulesHelperTests.Apply_Rejects_MinutesOutsideTheAllowedDurations</seealso>
    /// <seealso>DurationRulesHelperTests.Apply_Rejects_APartySizeAboveTheBookingLimit</seealso>
    /// <seealso>DurationRulesHelperTests.Apply_Rejects_TwoRulesForTheSamePartySize</seealso>
    public static void Apply(Venue venue, List<DurationRuleDto> rules)
    {
        if (rules.Count == 0)
        {
            venue.DurationRulesJson = null;
            return;
        }

        var seen = new HashSet<int>();
        foreach (DurationRuleDto rule in rules)
        {
            if (rule.MinPartySize < BookingLimits.MinPartySize || rule.MinPartySize > BookingLimits.MaxPartySize)
            {
                throw new ValidationException(
                    $"Duration rule party sizes must be between {BookingLimits.MinPartySize} and {BookingLimits.MaxPartySize}.")
                {
                    Code = ErrorCodes.VenueDurationRulePartySizeOutOfRange,
                    Args = new Dictionary<string, object> { ["min"] = BookingLimits.MinPartySize, ["max"] = BookingLimits.MaxPartySize }
                };
            }

            if (!BookingDuration.AllowedMinutes.Contains(rule.Minutes))
            {
                string allowed = string.Join(", ", BookingDuration.AllowedMinutes.Order());
                throw new ValidationException($"Duration rule minutes must be one of: {allowed}.")
                {
                    Code = ErrorCodes.VenueDurationRuleMinutesInvalid,
                    Args = new Dictionary<string, object> { ["allowed"] = allowed }
                };
            }

            if (!seen.Add(rule.MinPartySize))
            {
                throw new ValidationException($"There is more than one duration rule for parties of {rule.MinPartySize}.")
                {
                    Code = ErrorCodes.VenueDurationRuleDuplicatePartySize,
                    Args = new Dictionary<string, object> { ["partySize"] = rule.MinPartySize }
                };
            }
        }

        venue.DurationRulesJson = JsonSerializer.Serialize(rules
            .OrderBy(r => r.MinPartySize)
            .Select(r => new Rule { MinPartySize = r.MinPartySize, Minutes = r.Minutes }));
    }
}
