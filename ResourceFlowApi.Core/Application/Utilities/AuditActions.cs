using System.Globalization;

namespace ResourceFlowApi.Core.Application.Utilities;

/// <summary>
/// The dotted action keys an audit entry can carry, and the target types they point at. Keys
/// are <c>noun.verb</c> and are matched by prefix on the read side, so everything a reviewer
/// would filter as one group shares a first segment.
/// </summary>
public static class AuditActions
{
    // ── Bookings ─────────────────────────────────────────────────────────────
    public const string BookingCreate = "booking.create";
    public const string BookingUpdate = "booking.update";
    public const string BookingCancel = "booking.cancel";
    public const string BookingRestore = "booking.restore";
    public const string BookingExtend = "booking.extend";
    public const string BookingPurge = "booking.purge";
    public const string BookingEmail = "booking.email";
    public const string BookingStatus = "booking.status";

    // ── Locations ────────────────────────────────────────────────────────────
    public const string VenueCreate = "venue.create";
    public const string VenueUpdate = "venue.update";
    public const string VenueArchive = "venue.archive";
    public const string VenueRestore = "venue.restore";
    public const string VenueDelete = "venue.delete";
    public const string VenuePause = "venue.pause";
    public const string VenueUnpause = "venue.unpause";
    public const string VenueExtendBookings = "venue.extend_bookings";
    public const string VenueReorderSections = "venue.reorder_sections";

    public const string SectionCreate = "section.create";
    public const string SectionUpdate = "section.update";
    public const string SectionDelete = "section.delete";

    public const string ResourceCreate = "resource.create";
    public const string ResourceUpdate = "resource.update";
    public const string ResourceDelete = "resource.delete";

    public const string ResourceGroupCreate = "resource_group.create";
    public const string ResourceGroupUpdate = "resource_group.update";
    public const string ResourceGroupDelete = "resource_group.delete";

    // ── Waitlist ─────────────────────────────────────────────────────────────
    public const string WaitlistAdd = "waitlist.add";
    public const string WaitlistNotify = "waitlist.notify";
    public const string WaitlistAssign = "waitlist.assign";
    public const string WaitlistRemove = "waitlist.remove";

    // ── Accounts ─────────────────────────────────────────────────────────────
    public const string UserCreate = "user.create";
    public const string UserRoleChange = "user.role_change";
    public const string UserActivate = "user.activate";
    public const string UserDeactivate = "user.deactivate";
    public const string UserPasswordReset = "user.password_reset";

    public const string ApiKeyCreate = "api_key.create";
    public const string ApiKeyRevoke = "api_key.revoke";

    public const string AuthLogin = "auth.login";
    public const string AuthLoginFailed = "auth.login_failed";
    public const string AuthLogout = "auth.logout";
    public const string AuthPasswordChange = "auth.password_change";
    public const string AuthEmailChange = "auth.email_change";
    public const string AuthPvqSetup = "auth.pvq_setup";
    public const string AuthPasswordReset = "auth.password_reset";

    // ── Instance settings ────────────────────────────────────────────────────
    public const string BrandUpdate = "brand.update";
    public const string EmailSettingsUpdate = "email_settings.update";
    public const string EmailSettingsTest = "email_settings.test";

    public const string MediaUpload = "media.upload";
    public const string MediaDelete = "media.delete";

    public const string HighlightCreate = "highlight.create";
    public const string HighlightUpdate = "highlight.update";
    public const string HighlightDelete = "highlight.delete";

    public const string SocialLinkCreate = "social_link.create";
    public const string SocialLinkUpdate = "social_link.update";
    public const string SocialLinkDelete = "social_link.delete";

    public const string NotificationDelete = "notification.delete";
    public const string PushSubscribe = "push.subscribe";
    public const string PushUnsubscribe = "push.unsubscribe";

    /// <summary>
    /// The floor: a mutating admin request no service described still lands a row, keyed by its
    /// method. New endpoints are therefore audited the day they ship, just without a readable
    /// label until someone enriches them.
    /// </summary>
    public static string ForUndescribedRequest(string httpMethod)
        => $"http.{httpMethod.ToLowerInvariant()}";
}

/// <summary>The <c>TargetType</c> values an entry can carry.</summary>
public static class AuditTargets
{
    /// <summary>
    /// An entity's primary key as a <c>TargetId</c>. The column is a string so booking refs and
    /// numeric ids both fit, and every service coerces through here so the stored form is
    /// invariant rather than the request thread's culture.
    /// </summary>
    public static string IdOf(int id) => id.ToString(CultureInfo.InvariantCulture);

    public const string Booking = "Booking";
    public const string Venue = "Venue";
    public const string Section = "Section";
    public const string Resource = "Resource";
    public const string ResourceGroup = "ResourceGroup";
    public const string User = "User";
    public const string ApiKey = "ApiKey";
    public const string Brand = "Brand";
    public const string EmailSettings = "EmailSettings";
    public const string Media = "Media";
    public const string Highlight = "Highlight";
    public const string SocialLink = "SocialLink";
    public const string Notification = "Notification";
    public const string WaitlistEntry = "WaitlistEntry";
}
