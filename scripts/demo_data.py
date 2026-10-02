#!/usr/bin/env python3
"""
demo_data.py — the single source of truth for ResourceFlow demo/seed data.

Emits SQL on stdout. Consumers pipe it straight into sqlite3:

    python3 scripts/demo_data.py config            # brand, locations, sections, resources, groups
    python3 scripts/demo_data.py bookings          # bookings + admin notifications
    python3 scripts/demo_data.py all               # both, in FK-safe order
    python3 scripts/demo_data.py accounts          # the Owner + demo Manager (see below)

The `accounts` section is not part of `all`: it needs an admin password, which it
takes from ADMIN_EMAIL/ADMIN_PASSWORD or from --settings-file, never from argv.

This file replaces three copies of the same dataset that used to drift apart
(seed-local.sh's bash heredocs, purge-bookings.sh's inline Python, and a
hand-maintained config-snapshot.sql). Edit the dataset here and every consumer
follows; scripts/config-snapshot.sql is now a generated artifact.

The dataset is deliberately built to exercise every feature the product has,
because a demo site that only shows the happy path undersells it. Each location
below is annotated with the feature it exists to surface.

Conventions this script upholds:
  * every DateTime is emitted as UTC, converted from venue-local time via
    the location's IANA timezone;
  * OpenDays / WalkInDays are comma-separated ISO day numbers (1=Mon … 7=Sun);
  * a booking reserves a resource XOR a resource group, never both;
  * booking references follow the location's BookingRefFormat.
"""

import argparse
import base64
import hashlib
import json
import os
import pathlib
import random
import sys
from datetime import date, datetime, timedelta, timezone
from zoneinfo import ZoneInfo

# ─── Admin accounts ──────────────────────────────────────────────────────────
# The instance is multi-user, so a single-account demo never shows the Users
# card doing anything. These two exist to surface that:
#
#   Owner    — the bootstrap account, email/password from configuration. No
#              DisplayName, so the UI's "display name falls back to email" path
#              is exercised alongside the Manager's, which has one.
#   Manager  — the role that sees everything except user management, so a
#              visitor can sign in as one and watch the Users card disappear.
#
# The Manager shares the Owner's password on purpose: on the public demo the
# admin password is published anyway, so "same password, different email" is
# one less thing to explain. Do not reuse this generator on an instance where
# that is not already true.

DEMO_MANAGER_EMAIL = "manager@resourceflow.example"
DEMO_MANAGER_DISPLAY_NAME = "Demo Manager"

# ─── Brand ───────────────────────────────────────────────────────────────────
# Every nullable brand field is populated: AccentColor, HeaderImageFit and
# CopyrightText were previously always NULL, so the settings UI that renders
# them had nothing to show on a seeded database.
#
# HeaderImageUrl is deliberately NOT set here — see the `media` section. Image
# URLs are derived from the files that actually exist rather than hardcoded, so
# a hardcoded path can never point at a missing file.

BRAND = {'AppName': 'ResourceFlow',
 'PrimaryColor': '#059669',
 'AccentColor': '#f59e0b',
 'FaviconIcon': 'star',
 'HeaderImageUrl': None,
 'HeaderImageFit': 'Cover',
 'WebsiteUrl': 'http://localhost:5062',
 'PhoneNumber': '+1 215 555 0100',
 'EmailAddress': 'bookings@resourceflow.example',
 'CopyrightText': 'ResourceFlow demonstration instance',
 'Subtitle': 'Book spaces and facilities across locations.',
 'HighlightsHeading': 'Spaces and facilities',
 'HighlightsSubheading': 'Choose a space for your next session',
 'PrivacyPolicyUrl': None,
 'MinimumAppVersion': None}

# ─── Locations ───────────────────────────────────────────────────────────────
# Sections/resources/groups are nested so ids are assigned by build_dataset()
# rather than hand-numbered. `group.resources` refers to member resources by name.
#
# Between them these seven locations cover: per-day opening hours (including a
# past-midnight wrap), both booking-reference formats, all three contact
# fallback states, every slot interval (15/30/60), walk-in-only both globally
# and per-day, a live booking pause, an archived location, the resource-oversize
# cap, duration rules by party size, a walk-in-only resource, a guest cap, and
# combinable resource groups both named and unnamed.

LOCATIONS = [{'name': 'Central Workspace',
  'address': '346 W Girard Ave, Philadelphia, PA',
  'timezone': 'America/New_York',
  'open_days': '1,2,3,4,5,6',
  'open_time': '09:00',
  'close_time': '23:45',
  'duration': 90,
  'duration_rules': [(1, 60), (3, 90), (5, 120)],
  'slot_interval': 30,
  'ref_format': 0,
  'tags': 'meeting rooms,projector',
  'description': 'Book spaces and facilities at Central Workspace.',
  'guide_url': None,
  'phone': '+1 215 555 0123',
  'email': 'location1@resourceflow.example',
  'sections': [{'name': 'Meeting Rooms', 'resources': [('T1', 4), ('T2', 2), ('T3', 6), ('T4', 2)]},
               {'name': 'Studios', 'resources': [('P1', 4), ('P2', 4)]},
               {'name': 'Workspaces', 'resources': [('S1', 2), ('S2', 2)]}],
  'walk_in_resources': ['S1'],
  'max_guests': 8,
  'groups': [{'name': 'Meeting Room Pair', 'combined_capacity': 5, 'resources': ['T1', 'T2']},
             {'name': 'Studio Pair', 'combined_capacity': 7, 'resources': ['P1', 'P2']}]},
 {'name': 'North Workspace',
  'address': 'The Alley Behind the Alley, Toronto, ON',
  'timezone': 'America/Toronto',
  'open_days': '3,4,5,6',
  'open_time': '11:00',
  'close_time': '22:00',
  'open_hours': {3: ('16:00', '22:00'), 5: ('11:00', '01:30'), 6: ('11:00', '01:30')},
  'duration': 60,
  'slot_interval': 30,
  'ref_format': 1,
  'tags': 'workspaces,accessible',
  'description': 'Book spaces and facilities at North Workspace.',
  'guide_url': None,
  'phone': None,
  'email': None,
  'sections': [{'name': 'Meeting Rooms', 'resources': [('B1', 2), ('B2', 2), ('B3', 1)]},
               {'name': 'Studios', 'resources': [('Resource 1', 4), ('Resource 2', 4)]}],
  'groups': [{'name': None, 'combined_capacity': 4, 'resources': ['B1', 'B2']}],
  'walk_in_resources': []},
 {'name': 'Harbour Studio',
  'address': "Multiple Areas, please don't ask",
  'timezone': 'America/Los_Angeles',
  'open_days': '1,2,3,4,5,6,7',
  'open_time': '00:00',
  'close_time': '00:00',
  'duration': 60,
  'slot_interval': 60,
  'ref_format': 0,
  'tags': 'creative studio,24-hour access',
  'description': 'Book spaces and facilities at Harbour Studio.',
  'guide_url': None,
  'phone': '+1 604 555 0177',
  'email': None,
  'max_oversize': 2,
  'sections': [{'name': 'Meeting Rooms', 'resources': [('Workspace A', 2), ('Workspace B', 4)]},
               {'name': 'Studios', 'resources': [('BR1', 6), ('BR2', 2)]}],
  'groups': [],
  'walk_in_resources': []},
 {'name': 'Express Workspace',
  'address': 'Terminal B, Philadelphia International',
  'timezone': 'America/New_York',
  'open_days': '1,2,3,4,5,6,7',
  'open_time': '06:00',
  'close_time': '21:00',
  'walk_in_days': '6,7',
  'duration': 45,
  'slot_interval': 15,
  'ref_format': 1,
  'tags': 'shared resources',
  'description': 'Book spaces and facilities at Express Workspace.',
  'guide_url': None,
  'phone': '+1 215 555 0188',
  'email': 'location4@resourceflow.example',
  'sections': [{'name': 'Meeting Rooms', 'resources': [('C1', 2), ('C2', 2), ('C3', 4)]}],
  'groups': [],
  'walk_in_resources': []},
 {'name': 'Coastal Studio',
  'address': 'Boardwalk, Ocean City, NJ',
  'timezone': 'America/New_York',
  'open_days': '1,2,3,4,5,6,7',
  'open_time': '00:00',
  'close_time': '00:00',
  'walk_in_only': True,
  'duration': 60,
  'slot_interval': 30,
  'ref_format': 0,
  'tags': 'studio,daylight',
  'description': 'Book spaces and facilities at Coastal Studio.',
  'guide_url': None,
  'phone': None,
  'email': 'location5@resourceflow.example',
  'sections': [{'name': 'Meeting Rooms', 'resources': [('D1', 4), ('D2', 4), ('D3', 6)]}],
  'groups': [{'name': 'Combined Spaces', 'combined_capacity': 8, 'resources': ['D1', 'D2']}],
  'walk_in_resources': []},
 {'name': 'City Meeting Centre',
  'address': '1801 Walnut St, Philadelphia, PA',
  'timezone': 'America/New_York',
  'open_days': '2,3,4,5,6',
  'open_time': '17:00',
  'close_time': '23:00',
  'paused_days': 5,
  'duration': 120,
  'slot_interval': 60,
  'ref_format': 0,
  'tags': 'meeting rooms,quiet',
  'description': 'Book spaces and facilities at City Meeting Centre.',
  'guide_url': None,
  'phone': '+1 215 555 0144',
  'email': 'location6@resourceflow.example',
  'sections': [{'name': 'Meeting Rooms', 'resources': [('R1', 2), ('R2', 4), ('R3', 4)]}],
  'groups': [],
  'walk_in_resources': []},
 {'name': 'Archived Workspace',
  'address': '2nd & Market, Philadelphia, PA',
  'timezone': 'America/New_York',
  'open_days': '1,2,3,4,5',
  'open_time': '11:00',
  'close_time': '22:00',
  'archived': True,
  'duration': 60,
  'slot_interval': 30,
  'ref_format': 0,
  'tags': 'archived',
  'description': 'Book spaces and facilities at Archived Workspace.',
  'guide_url': None,
  'phone': None,
  'email': None,
  'sections': [{'name': 'Meeting Rooms', 'resources': [('M1', 4), ('M2', 2)]}],
  'groups': [],
  'walk_in_resources': []}]

# ─── Home page content ───────────────────────────────────────────────────────

HIGHLIGHTS = [('Meeting rooms', 'Book a space for team collaboration.', 'business-outline', None),
 ('Creative studios',
  'Reserve a space for workshops and creative sessions.',
  'color-palette-outline',
  None),
 ('Capacity-aware booking', 'Find resources that accommodate your group.', 'people-outline', None),
 ('Multiple locations',
  'Compare local hours and availability across sites.',
  'location-outline',
  None),
 ('Flexible scheduling',
  'Choose dates and times that suit your session.',
  'calendar-outline',
  None)]

SOCIAL_LINKS = [('Contact', 'https://example.com/contact', 'link-outline')]

# (name, email, special_requests) — a deliberate mix: guests with no email at
# all (phone bookings), guests with no requests, and one long request that
# stress-tests the admin grid's text wrapping.
GUESTS = [('Alex Chen', 'participant1@example.com', None),
 ('Sam Taylor', 'participant2@example.com', 'Projector requested'),
 ('Jordan Lee', 'participant3@example.com', None),
 ('Casey Morgan', 'participant4@example.com', 'Whiteboard requested'),
 ('Jamie Patel', 'participant5@example.com', None),
 ('Riley Kim', 'participant6@example.com', None),
 ('Robin Li', 'participant7@example.com', 'Accessible entrance required'),
 ('Avery Brown', 'participant8@example.com', None),
 ('Drew Smith', 'participant9@example.com', None),
 ('Cameron Jones', 'participant10@example.com', 'Quiet workspace preferred'),
 ('Quinn Wang', 'participant11@example.com', None),
 ('Skyler Wilson', 'participant12@example.com', 'Flexible layout requested'),
 ('Reese Clark', None, None),
 ('Sage Moore', None, None),
 ('Emery Davis', 'participant15@example.com', None),
 ('Rowan Martin', 'participant16@example.com', None),
 ('Blake Hall', None, None),
 ('Harper Green', 'participant18@example.com', None),
 ('Parker Young',
  'participant19@example.com',
  'Accessible entrance and space for mobility aids required. Please provide a projector and a '
  'quiet area for a team workshop.'),
 ('Finley Adams', None, None)]

REF_ADJECTIVES = [
    "amber", "bold", "brave", "bright", "brisk", "calm", "clear", "cobalt", "cosmic", "crisp",
    "dusty", "eager", "fair", "gentle", "glad", "golden", "grand", "hazel", "humble", "ivory",
    "jolly", "keen", "lively", "lucky", "mellow", "misty",
]
REF_NOUNS = [
    "anchor", "arch", "aspen", "atlas", "beacon", "birch", "bluff", "breeze", "brook", "canyon",
    "cedar", "cliff", "cloud", "comet", "cove", "crane", "creek", "delta", "dune", "ember",
    "falcon", "fern", "fjord", "forest", "glacier", "grove", "harbor", "hawk", "heron", "island",
    "lagoon", "lake", "lantern",
]

# Busy bands, as local wall-clock minutes-from-midnight. Slots are drawn from
# the intersection of these bands with the location's resolved hours for the
# day, so a location that opens at 16:00 simply gets no midday bookings.
BUSY_BANDS = [
    ("midday", 11 * 60 + 30, 14 * 60 + 30),
    ("evening", 17 * 60 + 30, 21 * 60 + 30),
    ("late", 21 * 60 + 30, 26 * 60),  # past midnight, for the late-close days
]


# ─── SQL helpers ─────────────────────────────────────────────────────────────


def q(value):
    """Render a Python value as a SQL literal, escaping embedded quotes."""
    if value is None:
        return "NULL"
    if isinstance(value, bool):
        return "1" if value else "0"
    if isinstance(value, int):
        return str(value)
    return "'" + str(value).replace("'", "''") + "'"


def insert(resource, row):
    cols = ",".join(row.keys())
    vals = ",".join(q(v) for v in row.values())
    return f"INSERT INTO {resource}({cols}) VALUES({vals});"


def utc_str(dt):
    return dt.astimezone(timezone.utc).strftime("%Y-%m-%d %H:%M:%S")


# ─── Dataset assembly ────────────────────────────────────────────────────────


def build_dataset(now_utc):
    """Assign ids to the declarative location tree and resolve relative dates."""
    venues, sections, resources, groups, memberships = [], [], [], [], []
    rid = sid = tid = gid = 0

    for spec in LOCATIONS:
        rid += 1
        paused_until = None
        if spec.get("paused_days"):
            paused_until = utc_str(now_utc + timedelta(days=spec["paused_days"]))

        duration_rules_json = None
        if spec.get("duration_rules"):
            duration_rules_json = json.dumps(
                [{"minPartySize": party_size, "minutes": minutes} for party_size, minutes in spec["duration_rules"]],
                separators=(",", ":"),
            )

        open_hours_json = None
        if spec.get("open_hours"):
            open_hours_json = json.dumps(
                {str(day): {"open": o, "close": c} for day, (o, c) in sorted(spec["open_hours"].items())},
                separators=(",", ":"),
            )

        venues.append(
            {
                "id": rid,
                "spec": spec,
                "row": {
                    "Id": rid,
                    "Name": spec["name"],
                    "Address": spec["address"],
                    "OpenTime": spec["open_time"],
                    "CloseTime": spec["close_time"],
                    "OpenDays": spec["open_days"],
                    "Timezone": spec["timezone"],
                    "BookingsPausedUntil": paused_until,
                    "Tags": spec.get("tags"),
                    "ImageUrl": spec.get("image"),
                    "IsArchived": bool(spec.get("archived")),
                    "DefaultBookingDurationMinutes": spec["duration"],
                    "DurationRulesJson": duration_rules_json,
                    "BookingSlotIntervalMinutes": spec["slot_interval"],
                    "OpenHoursJson": open_hours_json,
                    "WalkInOnly": bool(spec.get("walk_in_only")),
                    "WalkInDays": spec.get("walk_in_days"),
                    "Description": spec.get("description"),
                    "GuideUrl": spec.get("guide_url"),
                    "PhoneNumber": spec.get("phone"),
                    "EmailAddress": spec.get("email"),
                    "MaxSpareCapacity": spec.get("max_oversize"),
                    "MaxGuestsPerSlot": spec.get("max_guests"),
                    "BookingRefFormat": spec["ref_format"],
                },
            }
        )

        by_name = {}
        for order, sec in enumerate(spec["sections"]):
            sid += 1
            sections.append({"Id": sid, "Name": sec["name"], "VenueId": rid, "SortOrder": order})
            for tname, capacity in sec["resources"]:
                tid += 1
                resources.append(
                    {
                        "Id": tid,
                        "Name": tname,
                        "Capacity": capacity,
                        "SectionId": sid,
                        "WalkInOnly": tname in spec.get("walk_in_resources", ()),
                    }
                )
                by_name[tname] = {"id": tid, "capacity": capacity, "section_id": sid}

        resolved_groups = []
        for grp in spec.get("groups", []):
            gid += 1
            member_ids = [by_name[n]["id"] for n in grp["resources"]]
            groups.append(
                {
                    "Id": gid,
                    "Name": grp["name"],
                    "VenueId": rid,
                    "CombinedCapacity": grp["combined_capacity"],
                }
            )
            memberships += [{"ResourceGroupId": gid, "ResourceId": mid} for mid in member_ids]
            resolved_groups.append(
                {
                    "id": gid,
                    "combined_capacity": grp["combined_capacity"],
                    "member_ids": member_ids,
                    "section_id": by_name[grp["resources"][0]]["section_id"],
                }
            )

        # Booking generation needs the resolved inventory, keyed by resource name.
        venues[-1]["resources_by_name"] = by_name
        venues[-1]["groups"] = resolved_groups

    return {
        "venues": venues,
        "sections": sections,
        "resources": resources,
        "groups": groups,
        "memberships": memberships,
    }


# ─── Config SQL ──────────────────────────────────────────────────────────────

CONFIG_TABLES = [
    # The activity log is wiped on every reset, and for a stronger reason than the rest of
    # this list: the demo's admin password is public, so its entries are visitors' actions
    # and visitors' IP addresses, shown to every other visitor. The seeded entries that
    # replace them are emitted with the bookings, which is where the refs they point at
    # are generated.
    "AdminAuditEntries",
    "Highlights",
    "SocialLinks",
    "ResourceGroupMemberships",
    "ResourceGroups",
    "Resources",
    "Sections",
    "Venues",
    "BrandSettings",
    "EmailSettings",
]


def emit_config(ds):
    out = ["-- ── Config: brand, locations, sections, resources, groups, home page ──"]
    for t in CONFIG_TABLES:
        out.append(f"DELETE FROM {t};")
    seqs = ",".join(q(t) for t in CONFIG_TABLES)
    out.append(f"DELETE FROM sqlite_sequence WHERE name IN ({seqs});")

    out.append("")
    out.append("-- EmailSettings is left empty on purpose: no SMTP credentials in source control.")
    out.append(insert("BrandSettings", {"Id": 1, **BRAND}))

    out.append("")
    out.append("-- Locations")
    for r in ds["venues"]:
        out.append(insert("Venues", r["row"]))

    out.append("")
    out.append("-- Sections (SortOrder is explicit display order, not insertion order)")
    for s in ds["sections"]:
        out.append(insert("Sections", s))

    out.append("")
    out.append("-- Resources")
    for t in ds["resources"]:
        out.append(insert("Resources", t))

    if ds["groups"]:
        out.append("")
        out.append("-- Combinable resource groups. Members stay individually bookable; grouping")
        out.append("-- only deprioritizes them in auto-assign.")
        for g in ds["groups"]:
            out.append(insert("ResourceGroups", g))
        for m in ds["memberships"]:
            out.append(insert("ResourceGroupMemberships", m))

    out.append("")
    out.append("-- Highlights (Link=NULL renders a static card; a Link makes the card clickable)")
    for i, (title, body, icon, link) in enumerate(HIGHLIGHTS):
        out.append(
            insert(
                "Highlights",
                {"Id": i + 1, "Title": title, "Body": body, "IconKey": icon, "SortOrder": i, "Link": link},
            )
        )

    out.append("")
    out.append("-- Social links (footer)")
    for i, (label, url, icon) in enumerate(SOCIAL_LINKS):
        out.append(
            insert("SocialLinks", {"Id": i + 1, "Label": label, "Url": url, "IconKey": icon, "SortOrder": i})
        )

    return out


# ─── Media ───────────────────────────────────────────────────────────────────
# MediaService writes uploads into deterministic slots — hero.<ext>,
# location-<id>.<ext>, guide-<id>.pdf — so the seed can discover what artwork
# actually exists instead of hardcoding paths. This is what lets an uploaded
# image survive the demo reset: the config step leaves ImageUrl NULL, and this
# step points it back at whatever file is on disk, whatever extension it has.
#
# It only ever SETS a URL, never nulls one out, so it composes safely on top of
# the config step (which has already cleared the columns) and can also be run
# on its own. A served guide file wins over an external guide link, because
# uploading a PDF for a location is the more deliberate act.

IMAGE_EXTENSIONS = ("jpg", "jpeg", "png", "webp")


def _media_url(path):
    """/media/<name>?v=<mtime-ms>, matching MediaService's cache-buster format."""
    stamp = int(path.stat().st_mtime * 1000)
    return f"/media/{path.name}?v={stamp}"


def emit_media(ds, media_dir):
    from pathlib import Path

    root = Path(media_dir)
    out = [f"-- ── Media: URLs derived from the files in {media_dir} ──"]
    if not root.is_dir():
        out.append(f"-- directory not found, nothing to link")
        return out, 0

    linked = 0

    hero = next(
        (p for ext in IMAGE_EXTENSIONS for p in sorted(root.glob(f"hero.{ext}"))),
        None,
    )
    if hero:
        out.append(f"UPDATE BrandSettings SET HeaderImageUrl={q(_media_url(hero))};")
        linked += 1

    for r in ds["venues"]:
        rid = r["id"]
        image = next(
            (p for ext in IMAGE_EXTENSIONS for p in sorted(root.glob(f"location-{rid}.{ext}"))),
            None,
        )
        if image:
            out.append(f"UPDATE Venues SET ImageUrl={q(_media_url(image))} WHERE Id={rid};")
            linked += 1

        guide = root / f"guide-{rid}.pdf"
        if guide.is_file():
            out.append(f"UPDATE Venues SET GuideUrl={q(_media_url(guide))} WHERE Id={rid};")
            linked += 1

    if linked == 0:
        out.append("-- no matching media files found")
    return out, linked


# ─── Admin accounts ──────────────────────────────────────────────────────────


def resolve_admin_credentials(settings_file):
    """
    The Owner's email and password, from an ASP.NET appsettings JSON when one is
    given, else the ADMIN_EMAIL/ADMIN_PASSWORD environment variables the API's own
    bootstrap falls back to. Never taken from argv: a password there is readable by
    any local user through `ps`.
    """
    email = password = None

    if settings_file:
        path = pathlib.Path(settings_file)
        if not path.is_file():
            raise SystemExit(f"settings file not found: {settings_file}")
        admin = json.loads(path.read_text(encoding="utf-8")).get("Admin") or {}
        email = admin.get("Email") or None
        password = admin.get("Password") or None

    email = email or os.environ.get("ADMIN_EMAIL") or "admin@resourceflow.example"
    password = password or os.environ.get("ADMIN_PASSWORD")
    if not password:
        raise SystemExit(
            "no admin password available — set ADMIN_PASSWORD or pass --settings-file"
        )
    return email.strip().lower(), password


def _derive_credential(secret):
    """PBKDF2-SHA256, 100k iterations, 16-byte salt, 32-byte key — matches PasswordService."""
    salt = os.urandom(16)
    key = hashlib.pbkdf2_hmac("sha256", secret.encode(), salt, 100_000, dklen=32)
    return base64.b64encode(key).decode(), base64.b64encode(salt).decode()


def build_demo_accounts(settings_file):
    """
    The curated account rows, with credentials already derived. Reading the configured
    password and hashing it both happen here, so the plaintext never enters a structure
    that reaches the emitted SQL — which carries only the hash and the salt, exactly like
    a row the API would have written. Each row gets its own salt even though the two
    share a password.
    """
    owner_email, secret = resolve_admin_credentials(settings_file)

    # DisplayName None on purpose: between the two rows, both branches of the UI's
    # "display name, falling back to email" render.
    identities = [
        (owner_email, None, "Owner"),
        (DEMO_MANAGER_EMAIL, DEMO_MANAGER_DISPLAY_NAME, "Manager"),
    ]

    accounts = []
    for email, display_name, role in identities:
        password_hash, password_salt = _derive_credential(secret)
        accounts.append(
            {
                "Email": email.strip().lower(),
                "PasswordHash": password_hash,
                "PasswordSalt": password_salt,
                "DisplayName": display_name,
                "Role": role,
                "IsActive": True,
            }
        )
    return accounts


def emit_accounts(accounts, now_utc):
    """
    Replaces every admin account with the curated pair. Wiping first is the point on
    the demo: its admin password is public, so an account a visitor invited themselves
    must not survive the reset, for the same reason their uploads don't.

    API keys are deleted explicitly rather than left to the AdminApiKeys -> AdminCredentials
    cascade, because this script runs under PRAGMA foreign_keys=OFF and the cascade therefore
    never fires. Without the explicit delete a visitor's key outlived every reset: the wipe
    reseeds AdminCredentials from Id 1, so the orphaned row's UserId lands back on the new
    Owner and the key keeps authenticating, for the whole year until it expires.
    """
    out = [
        "-- ── Admin accounts: the bootstrap Owner + a demo Manager ──",
        "DELETE FROM AdminApiKeys;",
        "DELETE FROM sqlite_sequence WHERE name = 'AdminApiKeys';",
        "DELETE FROM AdminCredentials;",
        "DELETE FROM sqlite_sequence WHERE name = 'AdminCredentials';",
    ]

    created = utc_str(now_utc)
    for account in accounts:
        out.append(insert("AdminCredentials", {**account, "CreatedAt": created}))

    return out, len(accounts)


# ─── Booking generation ──────────────────────────────────────────────────────


def parse_hhmm(text):
    h, m = text.split(":")
    return int(h) * 60 + int(m)


def hours_for_day(spec, iso_day):
    override = (spec.get("open_hours") or {}).get(iso_day)
    if override:
        return override
    return spec["open_time"], spec["close_time"]


def duration_for(spec, party_size):
    """The slot length a party of `party_size` gets, the way BookingDuration.For resolves it."""
    rules = [minutes for min_party_size, minutes in sorted(spec.get("duration_rules") or []) if min_party_size <= party_size]
    return rules[-1] if rules else spec["duration"]


def longest_duration(spec):
    return max([spec["duration"]] + [minutes for _, minutes in spec.get("duration_rules") or []])


def day_slot_grid(spec, iso_day):
    """Local minutes-from-midnight for every bookable start on this ISO day.

    Mirrors AvailabilityService: a close time at or before the open time rolls
    into the next day (23:00→01:30 wraps; 00:00→00:00 is a full 24 hours), and
    a start is only offered if the whole booking fits before close.
    """
    open_txt, close_txt = hours_for_day(spec, iso_day)
    start = parse_hhmm(open_txt)
    end = parse_hhmm(close_txt)
    if end <= start:
        end += 24 * 60

    step = spec["slot_interval"]
    return list(range(start, end - longest_duration(spec) + 1, step))


class UnitLedger:
    """Tracks occupancy so seeded bookings never conflict with each other.

    Booking a resource also consumes its group (the resources can't be pushed
    together while one is taken) and booking a group consumes every member —
    the same mutual exclusion IBookingRepository.IsUnitBookedOnDateAsync
    enforces at runtime. The previous seeder deduplicated on
    (resource, date, time) only, which let a 90-minute booking at 18:00 sit on
    top of another at 18:30 on the same resource.
    """

    def __init__(self):
        self._busy = {}  # key -> list of (start, end) UTC datetimes

    @staticmethod
    def _overlaps(spans, start, end):
        return any(s < end and start < e for s, e in spans)

    def is_free(self, keys, start, end):
        return not any(self._overlaps(self._busy.get(k, []), start, end) for k in keys)

    def reserve(self, keys, start, end):
        for k in keys:
            self._busy.setdefault(k, []).append((start, end))


def make_ref_pool(rng):
    """Unique-reference minter, per format, matching the server's generators."""
    used = set()

    def mint(ref_format):
        while True:
            if ref_format == 1:  # Numeric — 8 digits, never a leading zero
                ref = str(rng.randint(10_000_000, 99_999_999))
            else:
                adj = rng.choice(REF_ADJECTIVES)
                a, b = rng.sample(REF_NOUNS, 2)
                ref = f"{adj}-{a}-{b}"
            if ref not in used:
                used.add(ref)
                return ref

    return mint


def bookable_units(r):
    """Every unit a booking can reserve, as (kind, id, capacity, section_id, keys).

    `keys` is the ledger's mutual-exclusion set: a resource locks itself and any
    group it belongs to; a group locks itself and all of its members.
    """
    units = []
    member_to_groups = {}
    for g in r["groups"]:
        for mid in g["member_ids"]:
            member_to_groups.setdefault(mid, []).append(g["id"])

    for name, t in r["resources_by_name"].items():
        keys = [("t", t["id"])] + [("g", gid) for gid in member_to_groups.get(t["id"], [])]
        units.append(("resource", t["id"], t["capacity"], t["section_id"], keys))

    for g in r["groups"]:
        keys = [("g", g["id"])] + [("t", mid) for mid in g["member_ids"]]
        units.append(("group", g["id"], g["combined_capacity"], g["section_id"], keys))

    return units


def party_size_for(rng, capacity, max_oversize):
    """A plausible party for a unit, honouring the location's oversize cap."""
    low = 1 if max_oversize is None else max(1, capacity - max_oversize)
    choices = list(range(low, capacity + 1))
    # Weight towards a full-ish resource; solo guests are the exception.
    weights = [1 if s == 1 else 3 for s in choices]
    return rng.choices(choices, weights=weights, k=1)[0]


NO_SHOW_RATE = 0.08
NO_SHOW_MARKED_AFTER = timedelta(minutes=15)


def assign_statuses(bookings, now_utc, rng):
    """What happened at each slot, the way staff would have recorded it.

    Past slots mostly finished and a few never showed; a no-show was marked a
    quarter of an hour in, which is when its resource came free. Slots under way
    now are in use. Cancelled and upcoming bookings stay Booked, and a status the
    row already carries (the waitlist's assigned parties) is kept.
    """
    for row, _, start_utc in bookings:
        if "Status" in row:
            continue
        end_utc = datetime.strptime(row["EndTime"], "%Y-%m-%d %H:%M:%S").replace(tzinfo=timezone.utc)
        if row["IsCancelled"] or start_utc > now_utc:
            row["Status"] = "Booked"
        elif end_utc > now_utc:
            row["Status"] = "InUse"
        elif rng.random() < NO_SHOW_RATE:
            row["Status"] = "NoShow"
            row["EndTime"] = utc_str(start_utc + NO_SHOW_MARKED_AFTER)
        else:
            row["Status"] = "Finished"


def emit_bookings(ds, now_utc, days_back, days_forward, occupancy, rng):
    out = [
        "-- ── Bookings + admin notifications ──",
        "DELETE FROM AdminNotifications;",
        "DELETE FROM EmailFailures;",
        # Explicit for the same reason the accounts wipe is: foreign_keys=OFF means the
        # Bookings cascade never reaches a visitor's push address.
        "DELETE FROM GuestPushSubscriptions;",
        # Visitors join the demo's waitlist with a name and often an email, and the
        # venues it points at are rebuilt with the config.
        "DELETE FROM WaitlistEntries;",
        "DELETE FROM Bookings;",
        "DELETE FROM sqlite_sequence WHERE name IN ('Bookings','AdminNotifications','EmailFailures','GuestPushSubscriptions','WaitlistEntries');",
        "",
    ]

    mint = make_ref_pool(rng)
    ledger = UnitLedger()
    guest_idx = 0
    bookings = []  # (row, venue_name) — ids assigned after sorting
    today = now_utc.date()

    for r in ds["venues"]:
        spec = r["spec"]
        tz = ZoneInfo(spec["timezone"])
        open_days = {int(d) for d in spec["open_days"].split(",") if d.strip()}
        walk_in_days = {int(d) for d in (spec.get("walk_in_days") or "").split(",") if d.strip()}
        units = bookable_units(r)

        for offset in range(-days_back, days_forward + 1):
            local_day = today + timedelta(days=offset)
            iso_day = local_day.isoweekday()
            if iso_day not in open_days:
                continue

            # Walk-in days take no online bookings, but AdminService.CreateBookingAsync
            # is intentionally exempt so staff can log walk-ins — seed a couple so the
            # admin grid shows what that looks like.
            walk_in = spec.get("walk_in_only") or iso_day in walk_in_days

            grid = day_slot_grid(spec, iso_day)
            if not grid:
                continue

            candidates = []
            for minutes in grid:
                band = next((b for b, lo, hi in BUSY_BANDS if lo <= minutes < hi), None)
                if band:
                    candidates.append(minutes)
            if not candidates:
                continue

            target = max(1, int(len(units) * occupancy))
            if walk_in:
                target = min(target, 2)

            attempts = 0
            placed = 0
            guests_by_slot = {}
            while placed < target and attempts < target * 6:
                attempts += 1
                unit_kind, unit_id, capacity, section_id, keys = rng.choice(units)
                minutes = rng.choice(candidates)

                party_size = party_size_for(rng, capacity, spec.get("max_oversize"))
                # Grid minutes are slot starts, so this is the cap BookingService enforces.
                if spec.get("max_guests") and guests_by_slot.get(minutes, 0) + party_size > spec["max_guests"]:
                    continue
                local_start = datetime.combine(local_day, datetime.min.time()) + timedelta(minutes=minutes)
                start_utc = local_start.replace(tzinfo=tz).astimezone(timezone.utc)
                end_utc = start_utc + timedelta(minutes=duration_for(spec, party_size))

                if not ledger.is_free(keys, start_utc, end_utc):
                    continue
                ledger.reserve(keys, start_utc, end_utc)
                guests_by_slot[minutes] = guests_by_slot.get(minutes, 0) + party_size
                placed += 1

                name, email, special = GUESTS[guest_idx % len(GUESTS)]
                guest_idx += 1
                if walk_in:
                    special = "Walk-in, recorded at the front desk"

                # Past bookings cancel more often than upcoming ones.
                cancel_rate = 0.15 if offset < 0 else 0.05
                cancelled = rng.random() < cancel_rate
                cancelled_at = None
                if cancelled:
                    # Cancelled some time before the booking, and never in the future.
                    lead = timedelta(hours=rng.randint(1, 72))
                    cancelled_at = min(start_utc - lead, now_utc)

                bookings.append(
                    (
                        {
                            "BookingRef": mint(spec["ref_format"]),
                            "CustomerName": name,
                            "CustomerEmail": email,
                            "Date": utc_str(start_utc),
                            "EndTime": utc_str(end_utc),
                            "PartySize": party_size,
                            "SectionId": section_id,
                            "ResourceId": unit_id if unit_kind == "resource" else None,
                            "ResourceGroupId": unit_id if unit_kind == "group" else None,
                            "VenueId": r["id"],
                            "IsCancelled": cancelled,
                            "CancelledAt": utc_str(cancelled_at) if cancelled_at else None,
                            "SpecialRequests": special,
                        },
                        r["row"]["Name"],
                        start_utc,
                    )
                )

    # Deleting a resource or section nulls these FKs rather than cascading the
    # booking away (DeleteResourceAsync / DeleteSectionAsync). Seed a couple of
    # survivors so the admin grid's "Resource"/"Section" fallback labels render.
    first = ds["venues"][0]
    for days_ago in (3, 9):
        start_utc = (now_utc - timedelta(days=days_ago)).replace(minute=0, second=0, microsecond=0)
        bookings.append(
            (
                {
                    "BookingRef": mint(first["spec"]["ref_format"]),
                    "CustomerName": "Rex Porter",
                    "CustomerEmail": "rex@example.com",
                    "Date": utc_str(start_utc),
                    "EndTime": utc_str(start_utc + timedelta(minutes=duration_for(first["spec"], 2))),
                    "PartySize": 2,
                    "SectionId": None,
                    "ResourceId": None,
                    "ResourceGroupId": None,
                    "VenueId": first["id"],
                    "IsCancelled": False,
                    "CancelledAt": None,
                    "SpecialRequests": "Resource was removed in a refit — booking kept",
                },
                first["row"]["Name"],
                start_utc,
            )
        )

    # Bookings the location's *current* schedule would no longer accept. Everything
    # above is generated against the rules the server enforces, so the schedule-conflict
    # panel and its dashboard count would have nothing to report on a fresh demo and the
    # feature would be invisible. These are the case it exists for: a schedule narrowed
    # after the bookings were taken. One lands on a day the location no longer opens,
    # one an hour and a half before it now opens.
    #
    # Deliberately the only seeded rows that break the never-conflict rule. They are
    # upcoming and non-cancelled because the read excludes anything else.
    strand_target = ds["venues"][0]
    strand_spec = strand_target["spec"]
    strand_tz = ZoneInfo(strand_spec["timezone"])
    strand_open_days = {int(d) for d in strand_spec["open_days"].split(",") if d.strip()}
    strand_units = [u for u in bookable_units(strand_target) if u[0] == "resource"]

    def first_upcoming(wanted_open):
        for offset in range(2, max(days_forward, 2) + 1):
            day = today + timedelta(days=offset)
            if (day.isoweekday() in strand_open_days) == wanted_open:
                return day
        return None

    closed_day = first_upcoming(wanted_open=False)
    open_day = first_upcoming(wanted_open=True)
    strand_open_minutes = parse_hhmm(hours_for_day(strand_spec, (open_day or today).isoweekday())[0])

    stranded = []
    if closed_day and strand_units:
        stranded.append((closed_day, 19 * 60, "Booked before Sundays were dropped"))
    if open_day and strand_units and strand_open_minutes >= 90:
        stranded.append((open_day, strand_open_minutes - 90, "Booked before the opening time moved later"))

    for i, (local_day, minutes, note) in enumerate(stranded):
        _, unit_id, capacity, section_id, keys = strand_units[i % len(strand_units)]
        local_start = datetime.combine(local_day, datetime.min.time()) + timedelta(minutes=minutes)
        start_utc = local_start.replace(tzinfo=strand_tz).astimezone(timezone.utc)
        end_utc = start_utc + timedelta(minutes=duration_for(strand_spec, min(2, capacity)))
        ledger.reserve(keys, start_utc, end_utc)

        name, email, _ = GUESTS[guest_idx % len(GUESTS)]
        guest_idx += 1
        bookings.append(
            (
                {
                    "BookingRef": mint(strand_spec["ref_format"]),
                    "CustomerName": name,
                    "CustomerEmail": email,
                    "Date": utc_str(start_utc),
                    "EndTime": utc_str(end_utc),
                    "PartySize": min(2, capacity),
                    "SectionId": section_id,
                    "ResourceId": unit_id,
                    "ResourceGroupId": None,
                    "VenueId": strand_target["id"],
                    "IsCancelled": False,
                    "CancelledAt": None,
                    "SpecialRequests": note,
                },
                strand_target["row"]["Name"],
                start_utc,
            )
        )

    walk_in_venue = next(r for r in ds["venues"] if r["spec"].get("walk_in_only"))
    walk_in_bookings, queue = build_walk_in_queue(walk_in_venue, now_utc, ledger, mint, rng)
    bookings.extend(walk_in_bookings)

    # Insert in chronological order so Bookings.Id correlates with time, the
    # way it would on a live system.
    bookings.sort(key=lambda b: b[2])
    assign_statuses(bookings, now_utc, rng)

    out.append(f"-- {len(bookings)} bookings across {len(ds['venues'])} locations")
    notifications = []
    for i, (row, venue_name, start_utc) in enumerate(bookings, start=1):
        out.append(insert("Bookings", {"Id": i, **row}))

        # Notifications only exist for recent activity — the bell should have
        # something in it on a fresh demo, but not 400 unread items.
        age = now_utc - start_utc
        if timedelta(days=-2) <= age <= timedelta(days=4):
            notifications.append((i, row, venue_name))

    rng.shuffle(notifications)
    notifications = notifications[:24]
    notifications.sort(key=lambda n: n[0])

    if notifications:
        out.append("")
        out.append("-- Admin notifications: most read, the newest few unread so the bell has a badge.")
        nid = 0
        for idx, (booking_id, row, venue_name) in enumerate(notifications):
            nid += 1
            cancelled = row["IsCancelled"]
            created = row["CancelledAt"] if cancelled else row["Date"]
            unread = idx >= len(notifications) - 5
            out.append(
                insert(
                    "AdminNotifications",
                    {
                        "Id": nid,
                        "VenueId": row["VenueId"],
                        "BookingId": booking_id,
                        "BookingRef": row["BookingRef"],
                        "Type": "BookingCancelled" if cancelled else "BookingCreated",
                        "CustomerName": row["CustomerName"] or "Guest",
                        "BookingDate": row["Date"],
                        "PartySize": row["PartySize"],
                        "VenueName": venue_name,
                        "IsRead": not unread,
                        "CreatedAt": created,
                    },
                )
            )

        # The capacity alert carries no booking of its own (BookingId NULL,
        # empty ref/customer) — see BookingNotificationService.
        nid += 1
        out.append(
            insert(
                "AdminNotifications",
                {
                    "Id": nid,
                    "VenueId": ds["venues"][0]["id"],
                    "BookingId": None,
                    "BookingRef": "",
                    "Type": "VenueNearlyFull",
                    "CustomerName": "",
                    "BookingDate": utc_str(now_utc),
                    "PartySize": 0,
                    "VenueName": ds["venues"][0]["row"]["Name"],
                    "IsRead": False,
                    "CreatedAt": utc_str(now_utc - timedelta(hours=2)),
                },
            )
        )

    booking_ids = {id(row): i for i, (row, _, _) in enumerate(bookings, start=1)}
    out.append("")
    out.append(f"-- Walk-in waitlist at {walk_in_venue['row']['Name']}: parties already in session, one called, the rest waiting")
    for entry_id, entry in enumerate(queue, start=1):
        assigned_row = entry.pop("_booking", None)
        entry["BookingId"] = booking_ids[id(assigned_row)] if assigned_row else None
        entry["Id"] = entry_id
        out.append(insert("WaitlistEntries", entry))

    out.extend(emit_audit_entries(ds, bookings, queue, now_utc))

    return out, len(bookings)


# ─── Walk-in waitlist ────────────────────────────────────────────────────────
# The admin board and the guest's quoted wait are driven entirely by state: who is
# queued, and which resources are mid-slot. Without both, the board is empty and every
# quote reads "a resource is free now", so the seed puts two parties at resources and four
# in line. Everything is relative to now; the reset reruns every two hours and the
# app expires a party six hours after it joins.

WAITLIST_REF_ALPHABET = "abcdefghijkmnpqrstuvwxyz23456789"

# (minutes since joining, party size, status, minutes since called)
WALK_IN_QUEUE = [
    (32, 2, "Notified", 3),
    (26, 4, "Waiting", None),
    (14, 2, "Waiting", None),
    (6, 6, "Waiting", None),
]

# (minutes since assigned, party size), for the parties the waitlist already assigned.
WALK_INS_IN_USE = [(40, 6), (15, 4)]


def build_walk_in_queue(r, now_utc, ledger, mint, rng):
    """Assigned walk-ins (as bookings, each linked from its closed entry) plus the live queue.

    Assigned parties take the smallest free resource that fits for a whole slot starting when
    they were assigned, the way WaitlistService.AssignAsync picks one, so they stay inside the
    never-conflict rule the rest of the bookings keep.
    """
    spec = r["spec"]
    resources = sorted((u for u in bookable_units(r) if u[0] == "resource"), key=lambda u: u[2])
    guests = iter(GUESTS[-8:])

    def ref():
        return "".join(rng.choice(WAITLIST_REF_ALPHABET) for _ in range(20))

    def entry(minutes_ago, party_size, status, **fields):
        name, email, _ = next(guests)
        return {
            "VenueId": r["id"],
            "Ref": ref(),
            "Name": name,
            "PartySize": party_size,
            "Email": email if rng.random() < 0.5 else None,
            "Locale": "en",
            "Status": status,
            "CreatedAt": utc_str(now_utc - timedelta(minutes=minutes_ago)),
            "NotifiedAt": None,
            "ClosedAt": None,
            **fields,
        }

    bookings, entries = [], []
    for started_ago, party_size in WALK_INS_IN_USE:
        start_utc = (now_utc - timedelta(minutes=started_ago)).replace(second=0, microsecond=0)
        end_utc = start_utc + timedelta(minutes=duration_for(spec, party_size))
        unit = next((u for u in resources if u[2] >= party_size and ledger.is_free(u[4], start_utc, end_utc)), None)
        if unit is None:
            continue
        _, resource_id, _, section_id, keys = unit
        ledger.reserve(keys, start_utc, end_utc)

        in_use = entry(started_ago + 20, party_size, "InUse", ClosedAt=utc_str(start_utc))
        row = {
            "BookingRef": mint(spec["ref_format"]),
            "CustomerName": in_use["Name"],
            "CustomerEmail": in_use["Email"],
            "Date": utc_str(start_utc),
            "EndTime": utc_str(end_utc),
            "PartySize": party_size,
            "SectionId": section_id,
            "ResourceId": resource_id,
            "ResourceGroupId": None,
            "VenueId": r["id"],
            "IsCancelled": False,
            "CancelledAt": None,
            "SpecialRequests": "Assigned from the waitlist",
            "Status": "InUse",
        }
        in_use["_booking"] = row
        bookings.append((row, r["row"]["Name"], start_utc))
        entries.append(in_use)

    for joined_ago, party_size, status, called_ago in WALK_IN_QUEUE:
        notified = utc_str(now_utc - timedelta(minutes=called_ago)) if called_ago is not None else None
        entries.append(entry(joined_ago, party_size, status, NotifiedAt=notified))

    # Ticket numbers count up in joining order, as CountCreatedSinceAsync + 1 would have issued them.
    entries.sort(key=lambda e: e["CreatedAt"])
    for number, e in enumerate(entries, start=1):
        e["Number"] = number
    return bookings, entries


# ─── Activity log ────────────────────────────────────────────────────────────
# Only actions an admin actually takes belong here: a guest booking a resource is
# not an admin action and produces no entry, which is why these are cancels,
# pauses and settings edits rather than one row per seeded booking.
#
# ActorUserId is NULL throughout. The accounts section runs after this one and
# wipes AdminCredentials, which would SET NULL any id written here anyway — and
# an entry that still reads correctly with no account behind it is exactly what
# the denormalized actor columns exist for.


def emit_audit_entries(ds, bookings, queue, now_utc):
    def entry(minutes_ago, action, **fields):
        row = {
            "OccurredAt": utc_str(now_utc - timedelta(minutes=minutes_ago)),
            "ActorUserId": None,
            "ActorEmail": DEMO_MANAGER_EMAIL,
            "ActorDisplayName": DEMO_MANAGER_DISPLAY_NAME,
            "ActorRole": "Manager",
            "Action": action,
            "TargetType": None,
            "TargetId": None,
            "TargetLabel": None,
            "VenueId": None,
            "Summary": None,
            "ChangesJson": None,
            "HttpMethod": "POST",
            "Path": "/api/admin",
            "StatusCode": 204,
            "IpAddress": "203.0.113.42",
            "UserAgent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36",
        }
        row.update(fields)
        return row

    first = ds["venues"][0]
    cancelled = [(i, row) for i, (row, _, _) in enumerate(bookings, start=1) if row["IsCancelled"]][-3:]
    in_use = [e for e in queue if e["Status"] == "InUse"][-1:]
    booking_refs = {i: row["BookingRef"] for i, (row, _, _) in enumerate(bookings, start=1)}

    rows = [
        entry(
            2,
            "auth.login",
            Path="/api/admin/auth/login",
            StatusCode=200,
            Summary=f"{DEMO_MANAGER_DISPLAY_NAME} signed in",
        ),
        # A rejected attempt is exactly what a trail gets consulted about, so 4xx rows are kept.
        entry(
            9,
            "auth.login_failed",
            ActorEmail="someone@example.com",
            ActorDisplayName=None,
            ActorRole="",
            Path="/api/admin/auth/login",
            StatusCode=401,
            Summary="Failed sign-in attempt",
            IpAddress="198.51.100.7",
        ),
        entry(
            34,
            "venue.pause",
            TargetType="Venue",
            TargetId=str(first["id"]),
            TargetLabel=first["row"]["Name"],
            VenueId=first["id"],
            Path=f"/api/admin/venues/{first['id']}/pause",
            StatusCode=200,
            Summary="Paused new bookings for 60 minutes",
        ),
        entry(
            96,
            "brand.update",
            TargetType="Brand",
            HttpMethod="PATCH",
            Path="/api/brand",
            StatusCode=200,
            Summary="Updated the homepage tagline",
            ChangesJson='{"tagline":{"before":"Book a resource","after":"Reserve your resource"}}',
        ),
        entry(
            210,
            "resource.update",
            TargetType="Resource",
            TargetId="4",
            TargetLabel="Resource 4",
            VenueId=first["id"],
            HttpMethod="PUT",
            Path=f"/api/venues/{first['id']}/sections/1/resources/4",
            StatusCode=200,
            Summary="Changed Resource 4 capacity from 2 to 4",
            ChangesJson='{"capacity":{"before":"2","after":"4"}}',
        ),
        # The customer's name is not recorded, here or anywhere: booking history is
        # deliberately GDPR-purgeable, and an entry holding it would put it back.
        *[
            entry(
                360 + index * 47,
                "booking.cancel",
                TargetType="Booking",
                TargetId=str(booking_id),
                TargetLabel=row["BookingRef"],
                VenueId=row["VenueId"],
                Path=f"/api/admin/bookings/{booking_id}/cancel",
                Summary=f"Cancelled booking {row['BookingRef']}",
            )
            for index, (booking_id, row) in enumerate(cancelled)
        ],
        # Named by ticket number only, as WaitlistService.Describe does: the entry's name
        # and email are deleted after seven days, and the trail outlives them.
        *[
            entry(
                15,
                "waitlist.assign",
                TargetType="WaitlistEntry",
                TargetId=str(e["Id"]),
                TargetLabel=f"#{e['Number']}",
                VenueId=e["VenueId"],
                Path=f"/api/admin/waitlist/{e['Id']}/assign",
                StatusCode=200,
                Summary=f"Assigned ticket #{e['Number']} to booking {booking_refs[e['BookingId']]}",
            )
            for e in in_use
        ],
    ]

    out = [
        "",
        f"-- Activity log: {len(rows)} admin actions (see AdminAuditEntry).",
        # Repeated from the config section so this one stays self-contained: applying
        # `bookings` on its own must not collide with the explicit ids below.
        "DELETE FROM AdminAuditEntries;",
        "DELETE FROM sqlite_sequence WHERE name = 'AdminAuditEntries';",
    ]
    out.extend(insert("AdminAuditEntries", {"Id": i, **row}) for i, row in enumerate(rows, start=1))
    return out


# ─── CLI ─────────────────────────────────────────────────────────────────────


def main():
    p = argparse.ArgumentParser(description="Emit ResourceFlow demo-data SQL on stdout.")
    p.add_argument(
        "section",
        choices=["config", "bookings", "media", "accounts", "all"],
        nargs="?",
        default="all",
    )
    p.add_argument(
        "--media-dir",
        default=None,
        help="directory to scan for hero/location/guide files; required by the 'media' section",
    )
    p.add_argument(
        "--settings-file",
        default=None,
        help="appsettings JSON to read Admin:Email/Admin:Password from ('accounts' section)",
    )
    p.add_argument("--days-back", type=int, default=14, help="days of history to generate (default 14)")
    p.add_argument("--days-forward", type=int, default=14, help="days of upcoming bookings (default 14)")
    p.add_argument(
        "--occupancy",
        type=float,
        default=0.55,
        help="fraction of each location's bookable units to fill per service day (default 0.55)",
    )
    p.add_argument("--seed", type=int, default=None, help="RNG seed, for reproducible output")
    args = p.parse_args()

    if not 0 < args.occupancy <= 1:
        p.error("--occupancy must be greater than 0 and at most 1")
    if args.section == "media" and not args.media_dir:
        p.error("the 'media' section requires --media-dir")

    rng = random.Random(args.seed)
    now_utc = datetime.now(timezone.utc)
    ds = build_dataset(now_utc)

    lines = [
        "-- GENERATED by scripts/demo_data.py — do not edit by hand.",
        f"-- Generated at {utc_str(now_utc)} UTC.",
        "PRAGMA foreign_keys=OFF;",
        "PRAGMA busy_timeout=5000;",
        "BEGIN;",
        "",
    ]

    booking_count = 0
    if args.section in ("config", "all"):
        lines += emit_config(ds)
        lines.append("")
    if args.section in ("bookings", "all"):
        booking_lines, booking_count = emit_bookings(
            ds, now_utc, args.days_back, args.days_forward, args.occupancy, rng
        )
        lines += booking_lines
        lines.append("")

    # Accounts stay out of 'all': they need a password the generator cannot invent,
    # so every caller asks for them explicitly and supplies one.
    account_count = 0
    if args.section == "accounts":
        account_lines, account_count = emit_accounts(
            build_demo_accounts(args.settings_file), now_utc
        )
        lines += account_lines
        lines.append("")

    # Media runs last so it overwrites the NULLs the config step wrote.
    media_count = 0
    if args.section == "media" or (args.section == "all" and args.media_dir):
        media_lines, media_count = emit_media(ds, args.media_dir)
        lines += media_lines
        lines.append("")

    lines += ["COMMIT;", "PRAGMA foreign_keys=ON;"]

    print("\n".join(lines))
    if args.section == "accounts":
        print(f"[demo_data] {account_count} admin accounts", file=sys.stderr)
        return

    summary = f"[demo_data] {len(ds['venues'])} locations, {len(ds['resources'])} resources"
    if args.section in ("bookings", "all"):
        summary += f", {booking_count} bookings"
    if args.media_dir or args.section == "media":
        summary += f", {media_count} media files linked"
    print(summary, file=sys.stderr)


if __name__ == "__main__":
    main()
