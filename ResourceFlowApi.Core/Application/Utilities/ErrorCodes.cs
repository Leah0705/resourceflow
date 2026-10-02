namespace ResourceFlowApi.Core.Application.Utilities;

/// <summary>
/// Stable, machine-readable identifiers for the specific rule that rejected a request. Surfaced
/// as <c>ResourceFlowException.Code</c> and echoed on <c>MessageResponse.Code</c> alongside the
/// existing English <c>message</c> — the code is what a client branches on, the message is what
/// it shows. One constant per distinct rule, not per throw site: two throw sites rejecting for
/// the same reason share a code, even across different controllers or exception types.
/// </summary>
/// <remarks>
/// Does not cover the ~35 <c>[StringLength]</c>/<c>[Required]</c>-style <c>ErrorMessage</c>
/// values on request DTOs (e.g. <c>BrandRequest</c>). Model-state validation fails before a
/// controller action runs, so those never reach <c>GlobalExceptionHandler</c> — giving them a
/// code is separate follow-up work.
/// </remarks>
public static class ErrorCodes
{
    // ── Bookings ─────────────────────────────────────────────────────────────
    public const string BookingPastDate = "booking.past_date";
    public const string BookingPaused = "booking.paused";
    public const string BookingPausedIndefinitely = "booking.paused_indefinitely";
    public const string BookingWalkInOnly = "booking.walk_in_only";
    public const string BookingWalkInOnlyToday = "booking.walk_in_only_today";
    public const string BookingResourceConflict = "booking.resource_conflict";
    public const string BookingResourceHeld = "booking.resource_held";
    public const string BookingNoResourcesAvailable = "booking.no_resources_available";
    public const string BookingAllResourcesHeld = "booking.all_resources_held";
    public const string BookingAmbiguousResourceSelection = "booking.ambiguous_resource_selection";
    public const string BookingPartySizeOutOfRange = "booking.party_size_out_of_range";
    public const string BookingAlreadyPast = "booking.already_past";
    public const string BookingAlreadyActive = "booking.already_active";
    public const string BookingMoveConflict = "booking.move_conflict";
    public const string BookingResourceNotInSection = "booking.resource_not_in_section";
    public const string BookingInvalidResourceForVenue = "booking.invalid_resource_for_venue";
    public const string BookingSectionMismatch = "booking.section_mismatch";
    public const string BookingResourceIdRequiredForSectionChange = "booking.resource_id_required_for_section_change";
    public const string BookingEmailFieldsRequired = "booking.email_fields_required";
    public const string BookingReminderChannelInvalid = "booking.reminder_channel_invalid";
    public const string BookingReminderKeysRequired = "booking.reminder_keys_required";
    public const string BookingReminderEndpointInvalid = "booking.reminder_endpoint_invalid";
    public const string BookingReminderTooLate = "booking.reminder_too_late";
    public const string BookingReminderCancelled = "booking.reminder_cancelled";
    public const string BookingWalletNotConfigured = "booking.wallet_not_configured";
    public const string BookingWalletCancelled = "booking.wallet_cancelled";
    public const string BookingNoCustomerEmail = "booking.no_customer_email";
    public const string BookingEmailSendFailed = "booking.email_send_failed";
    public const string BookingLookupEmailRequired = "booking.lookup_email_required";
    public const string BookingLookupNotFound = "booking.lookup_not_found";
    public const string BookingCancelEmailRequired = "booking.cancel_email_required";
    public const string BookingStatusInvalid = "booking.status_invalid";
    public const string BookingStatusCancelled = "booking.status_cancelled";
    public const string BookingStatusTransitionInvalid = "booking.status_transition_invalid";
    public const string BookingNoShowBeforeStart = "booking.no_show_before_start";
    public const string BookingPacingFull = "booking.pacing_full";

    public const string ResourceCapacityExceeded = "resource.capacity_exceeded";
    public const string ResourceOversizeCap = "resource.oversize_cap";
    public const string ResourceCapacityOutOfRange = "resource.capacity_out_of_range";
    public const string ResourceWalkInOnly = "resource.walk_in_only";

    public const string ResourceGroupNotFound = "resource_group.not_found";
    public const string ResourceGroupBookingConflict = "resource_group.booking_conflict";
    public const string ResourceGroupHoldConflict = "resource_group.hold_conflict";
    public const string ResourceGroupDisbanded = "resource_group.disbanded";
    public const string ResourceGroupCapacityExceeded = "resource_group.capacity_exceeded";
    public const string ResourceGroupOversizeCap = "resource_group.oversize_cap";
    public const string ResourceGroupNoMembers = "resource_group.no_members";
    public const string ResourceGroupMinMembers = "resource_group.min_members";
    public const string ResourceGroupDuplicateMember = "resource_group.duplicate_member";
    public const string ResourceGroupInvalidMembers = "resource_group.invalid_members";
    public const string ResourceGroupMemberAlreadyGrouped = "resource_group.member_already_grouped";
    public const string ResourceGroupCombinedCapacityExceedsSum = "resource_group.combined_capacity_exceeds_sum";
    public const string ResourceGroupCombinedCapacityNotWorthCombining = "resource_group.combined_capacity_not_worth_combining";

    public const string HoldAmbiguousGroupAndResource = "hold.ambiguous_group_and_resource";
    public const string HoldGroupPartySizeRequired = "hold.group_party_size_required";
    public const string HoldAutoAssignPartySizeRequired = "hold.auto_assign_party_size_required";
    public const string HoldUnavailable = "hold.unavailable";
    public const string HoldClientLimit = "hold.client_limit";

    // ── Locations ────────────────────────────────────────────────────────────
    public const string VenueNotFound = "venue.not_found";
    public const string VenueNoSections = "venue.no_sections";
    public const string VenueNameRequired = "venue.name_required";
    public const string VenueArchiveBeforeDelete = "venue.archive_before_delete";
    public const string VenueClosedAtTime = "venue.closed_at_time";
    public const string VenueDurationInvalid = "venue.duration_invalid";
    public const string VenueSlotIntervalInvalid = "venue.slot_interval_invalid";
    public const string VenueOversizeCapInvalid = "venue.oversize_cap_invalid";
    public const string VenueMaxGuestsInvalid = "venue.max_guests_invalid";
    public const string VenueGuideUrlInvalid = "venue.guide_url_invalid";
    public const string VenueBookingRefFormatInvalid = "venue.booking_ref_format_invalid";
    public const string VenueWalkInDaysInvalid = "venue.walk_in_days_invalid";
    public const string VenueOpenHoursDayInvalid = "venue.open_hours_day_invalid";
    public const string VenueOpenHoursDuplicateDay = "venue.open_hours_duplicate_day";
    public const string VenueOpenHoursTimeInvalid = "venue.open_hours_time_invalid";
    public const string VenueSectionIdsMismatch = "venue.section_ids_mismatch";
    public const string VenueDurationRuleDuplicatePartySize = "venue.duration_rule_duplicate_party_size";
    public const string VenueDurationRuleMinutesInvalid = "venue.duration_rule_minutes_invalid";
    public const string VenueDurationRulePartySizeOutOfRange = "venue.duration_rule_party_size_out_of_range";

    // ── Waitlist ─────────────────────────────────────────────────────────────
    public const string WaitlistNotFound = "waitlist.not_found";
    public const string WaitlistNotWalkInNow = "waitlist.not_walk_in_now";
    public const string WaitlistClosedNow = "waitlist.closed_now";
    public const string WaitlistNameRequired = "waitlist.name_required";
    public const string WaitlistEmailInvalid = "waitlist.email_invalid";
    public const string WaitlistPartyTooLarge = "waitlist.party_too_large";
    public const string WaitlistNotActive = "waitlist.not_active";
    public const string WaitlistNoResourceFree = "waitlist.no_resource_free";
    public const string WaitlistPushInvalid = "waitlist.push_invalid";

    // ── Accounts ─────────────────────────────────────────────────────────────
    public const string UserEmailAlreadyExists = "user.email_already_exists";
    public const string UserCannotChangeOwnRole = "user.cannot_change_own_role";
    public const string UserCannotDeactivateSelf = "user.cannot_deactivate_self";
    public const string UserLastActiveOwnerDemote = "user.last_active_owner_demote";
    public const string UserLastActiveOwnerDeactivate = "user.last_active_owner_deactivate";
    public const string UserNotFound = "user.not_found";
    public const string UserEmailInvalid = "user.email_invalid";
    public const string UserEmailTooLong = "user.email_too_long";
    public const string UserDisplayNameTooLong = "user.display_name_too_long";
    public const string UserPasswordTooShort = "user.password_too_short";
    public const string UserRoleInvalid = "user.role_invalid";

    // ── API keys ─────────────────────────────────────────────────────────────
    public const string ApiKeyNameRequired = "api_key.name_required";
    public const string ApiKeyNameTooLong = "api_key.name_too_long";
    public const string ApiKeyScopesRequired = "api_key.scopes_required";
    public const string ApiKeyScopeInvalid = "api_key.scope_invalid";
    public const string ApiKeyExpiresAtInPast = "api_key.expires_at_in_past";
    public const string ApiKeyExpiresAtWithNeverExpires = "api_key.expires_at_with_never_expires";
    public const string ApiKeyNotFound = "api_key.not_found";
    public const string ApiKeyScopeMissing = "api_key.scope_missing";
    public const string ApiKeyNotAllowed = "api_key.not_allowed";
    public const string ApiKeyNotASession = "api_key.not_a_session";

    public const string AuthEmailUnchanged = "auth.email_unchanged";
    public const string AuthEmailAlreadyInUse = "auth.email_already_in_use";
    public const string AuthPvqFieldsRequired = "auth.pvq_fields_required";
    public const string AuthPvqNotConfigured = "auth.pvq_not_configured";
    public const string AuthInvalidResetToken = "auth.invalid_reset_token";
    public const string AuthNoAccountForSession = "auth.no_account_for_session";

    // ── Instance settings ────────────────────────────────────────────────────
    public const string BrandAppNameTooLong = "brand.app_name_too_long";
    public const string BrandPrimaryColorInvalid = "brand.primary_color_invalid";
    public const string BrandAccentColorInvalid = "brand.accent_color_invalid";
    public const string BrandFaviconInvalid = "brand.favicon_invalid";
    public const string BrandCopyrightTooLong = "brand.copyright_too_long";
    public const string BrandSubtitleTooLong = "brand.subtitle_too_long";
    public const string BrandHighlightsHeadingTooLong = "brand.highlights_heading_too_long";
    public const string BrandHighlightsSubheadingTooLong = "brand.highlights_subheading_too_long";
    public const string BrandHeaderImageFitInvalid = "brand.header_image_fit_invalid";
    public const string BrandWebsiteUrlInvalid = "brand.website_url_invalid";
    public const string BrandPrivacyPolicyUrlInvalid = "brand.privacy_policy_url_invalid";
    public const string BrandMinimumAppVersionInvalid = "brand.minimum_app_version_invalid";

    public const string HighlightTitleRequired = "highlight.title_required";
    public const string HighlightTitleTooLong = "highlight.title_too_long";
    public const string HighlightBodyTooLong = "highlight.body_too_long";
    public const string HighlightLinkInvalid = "highlight.link_invalid";

    public const string SocialLinkLabelRequired = "social_link.label_required";
    public const string SocialLinkLabelTooLong = "social_link.label_too_long";
    public const string SocialLinkUrlRequired = "social_link.url_required";
    public const string SocialLinkUrlInvalid = "social_link.url_invalid";

    /// <summary>Shared by highlights and social links: both validate an icon key against the same allow-list.</summary>
    public const string IconInvalid = "icon.invalid";

    public const string ContactPhoneTooLong = "contact.phone_too_long";
    public const string ContactEmailTooLong = "contact.email_too_long";
    public const string ContactEmailInvalid = "contact.email_invalid";

    // ── Media ────────────────────────────────────────────────────────────────
    public const string MediaUnsupportedImageType = "media.unsupported_image_type";
    public const string MediaHeroTooLarge = "media.hero_too_large";
    public const string MediaLocationImageTooLarge = "media.location_image_too_large";
    public const string MediaUnsupportedGuideType = "media.unsupported_guide_type";
    public const string MediaGuideTooLarge = "media.guide_too_large";

    // ── Infrastructure ───────────────────────────────────────────────────────
    public const string EmailNotConfigured = "email.not_configured";
    public const string EmailConnectionFailed = "email.connection_failed";
    public const string AdminPasswordNotConfigured = "admin.password_not_configured";
    public const string NotificationPushEndpointInvalid = "notification.push_endpoint_invalid";
}
