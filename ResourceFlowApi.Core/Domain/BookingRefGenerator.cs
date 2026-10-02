using System.Globalization;
using System.Security.Cryptography;

namespace ResourceFlowApi.Core.Domain;

/// <summary>
/// Default booking-reference format: <c>adjective-noun-noun-NNNN</c>, e.g.
/// <c>swift-cedar-river-0482</c>. A reference paired with the guest's email address is the whole
/// of a guest's identity — there is no account behind it — so it is the secret that stands between
/// a stranger and a booking's name, phone, party, time and cancel button. It is therefore drawn
/// from a CSPRNG rather than <c>Random.Shared</c>, whose xoshiro256** stream anyone can sample by
/// making bookings of their own, and it is wide enough that guessing it online is not a plan.
/// <para>
/// 50 adjectives x 60 nouns x 59 remaining nouns x 10,000 tails = 1,770,000,000 combinations
/// (~30.7 bits), against the 177,000 (~17.4 bits) the bare three-word shape gave. The tail is one
/// extra spoken segment, which is the constraint that rules out a hex blob: staff read these down
/// the phone.
/// </para>
/// <para>
/// The width carries the defence on its own and is not merely a supplement to the per-IP throttle
/// on the by-ref endpoints. Addresses are cheap: a hundred-address pool walks a 177,000-wide space
/// in roughly ninety minutes even at ten guesses per minute per address, so a rate limit alone
/// cannot protect a 17-bit secret. Entropy is what survives a pool; the throttle raises the cost
/// for the attacker who has not built one. Neither substitutes for the other.
/// </para>
/// <seealso>BookingRefGeneratorTests.Generate_ReturnsThreeWordsAndAFixedWidthNumericTail</seealso>
/// <seealso>BookingRefGeneratorTests.CombinationCount_MatchesTheDocumentedSpace</seealso>
/// <seealso>BookingRefGeneratorTests.Generate_TailSpansTheWholeRangeIncludingLeadingZeros</seealso>
/// </summary>
public static class BookingRefGenerator
{
    private static readonly string[] _adjectives =
    [
        "amber", "bold", "brave", "bright", "brisk", "calm", "clear", "cobalt", "cosmic", "crisp",
        "dusty", "eager", "fair", "gentle", "glad", "golden", "grand", "hazel", "humble", "ivory",
        "jolly", "keen", "lively", "lucky", "mellow", "misty", "noble", "pale", "plain", "proud",
        "quick", "quiet", "rapid", "rosy", "royal", "rustic", "sandy", "scarlet", "sharp", "silent",
        "silver", "simple", "sleek", "smooth", "snowy", "steady", "sunny", "swift", "vivid", "wild"
    ];

    private static readonly string[] _nouns =
    [
        "anchor", "arch", "aspen", "atlas", "beacon", "birch", "bluff", "breeze", "brook", "canyon",
        "cedar", "cliff", "cloud", "comet", "cove", "crane", "creek", "delta", "dune", "ember",
        "falcon", "fern", "fjord", "forest", "glacier", "grove", "harbor", "hawk", "heron", "island",
        "lagoon", "lake", "lantern", "maple", "marsh", "meadow", "plateau", "moss", "oak", "orbit",
        "otter", "pebble", "pine", "planet", "prairie", "quartz", "raven", "reef", "ridge", "river",
        "robin", "shore", "sparrow", "spruce", "stone", "summit", "thunder", "tide", "valley", "willow"
    ];

    /// <summary>
    /// Digits in the trailing segment. Zero-padded to this width and unconstrained at the leading
    /// digit — unlike <see cref="NumericBookingRefGenerator"/> the reference is not a number
    /// overall, so nothing downstream can coerce it and shorten it, and forbidding a leading zero
    /// would only shave a tenth off the space.
    /// </summary>
    public const int TailDigits = 4;

    private static readonly int _tailExclusiveMax = (int)Math.Pow(10, TailDigits);

    /// <summary>
    /// Size of the space <see cref="Generate"/> draws from. Derived from the word lists rather
    /// than restated, so editing one moves this number and fails the test that pins the figure
    /// quoted on this class.
    /// </summary>
    public static long CombinationCount =>
        (long)_adjectives.Length * _nouns.Length * (_nouns.Length - 1) * _tailExclusiveMax;

    public static string Generate()
    {
        string adj = _adjectives[RandomNumberGenerator.GetInt32(_adjectives.Length)];
        string noun1 = _nouns[RandomNumberGenerator.GetInt32(_nouns.Length)];
        string noun2;
        do { noun2 = _nouns[RandomNumberGenerator.GetInt32(_nouns.Length)]; }
        while (noun2 == noun1);

        string tail = RandomNumberGenerator.GetInt32(_tailExclusiveMax)
            .ToString($"D{TailDigits}", CultureInfo.InvariantCulture);

        return $"{adj}-{noun1}-{noun2}-{tail}";
    }
}
