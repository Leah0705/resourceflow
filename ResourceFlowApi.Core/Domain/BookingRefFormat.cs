namespace ResourceFlowApi.Core.Domain;

/// <summary>
/// Shape of the customer-facing booking reference a venue hands out.
/// Persisted as an int on <see cref="Venue"/>, so the member order is part of the
/// database contract — append new formats, never renumber existing ones.
/// </summary>
public enum BookingRefFormat
{
    /// <summary>Three hyphenated words, e.g. "swift-cedar-meadow". The original (and default) format.</summary>
    AlphaNumeric = 0,

    /// <summary>Digits only, e.g. "48273910" — easier to read out over the phone.</summary>
    Numeric = 1
}
