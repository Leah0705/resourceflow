using Moq;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Tests.TestInfrastructure;

namespace ResourceFlowApi.Tests.Services;

public class WaitlistServiceTests
{
    /// <summary>A Saturday evening, inside the 11:00–23:00 UTC hours every test venue keeps.</summary>
    private static readonly DateTime Now = new(2026, 9, 26, 19, 0, 0, DateTimeKind.Utc);

    private readonly FakeWaitlistRepository _waitlist = new();
    private readonly Mock<IVenueRepository> _venues = new();
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IHoldService> _holds = new();
    private readonly Mock<ISystemClock> _clock = new();
    private readonly Mock<IWaitlistReadyNotifier> _notifier = new();
    private readonly Mock<INotificationQueue> _queue = new();
    private readonly Mock<IAuditScope> _audit = new();
    private readonly List<Booking> _inProgress = new();
    private readonly HashSet<int> _bookedResources = new();
    private Venue _venue = WalkInVenue();

    public WaitlistServiceTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(() => Now);
        _venues.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(() => _venue);
        _bookings.Setup(b => b.GetInProgressForVenueAsync(1, It.IsAny<DateTime>(), It.IsAny<int>()))
            .ReturnsAsync(() => _inProgress);
        _bookings.Setup(b => b.IsUnitBookedOnDateAsync(It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<int?>()))
            .ReturnsAsync((int? resourceId, int? _, DateTime _, int _, int? _) => resourceId is { } id && _bookedResources.Contains(id));
        _bookings.Setup(b => b.AddAsync(It.IsAny<Booking>()))
            .ReturnsAsync((Booking b) => { b.Id = 500; return b; });
    }

    /// <summary>A walk-in-only location with a 2-place resource (id 1) and a 4-place resource (id 2).</summary>
    private static Venue WalkInVenue(bool walkInOnly = true, string open = "11:00", string close = "23:00")
    {
        var venue = new Venue
        {
            Id = 1,
            Name = "Test Venue",
            WalkInOnly = walkInOnly,
            OpenTime = open,
            CloseTime = close,
            DefaultBookingDurationMinutes = 60,
        };
        venue.Sections.Add(new Section
        {
            Id = 7,
            Name = "Main",
            VenueId = 1,
            Resources = new List<Resource>
            {
                new() { Id = 1, Capacity = 2, SectionId = 7 },
                new() { Id = 2, Capacity = 4, SectionId = 7 },
            },
        });
        return venue;
    }

    private WaitlistService CreateService(ICurrentUserService? currentUser = null) => new(
        _waitlist,
        _venues.Object,
        _bookings.Object,
        new ResourceAutoAssigner(_bookings.Object, _holds.Object),
        _clock.Object,
        _notifier.Object,
        _queue.Object,
        currentUser,
        _audit.Object);

    private static JoinWaitlistRequest Party(int partySize = 2, string name = "Ada", string? email = null, string? locale = null)
        => new() { Name = name, PartySize = partySize, Email = email, Locale = locale };

    private WaitlistEntry Seed(int partySize, WaitlistStatus status = WaitlistStatus.Waiting, int minutesAgo = 10)
    {
        var entry = new WaitlistEntry
        {
            VenueId = 1,
            Venue = _venue,
            Ref = $"ref{_waitlist.Entries.Count + 1}",
            Number = _waitlist.Entries.Count + 1,
            Name = $"Guest {_waitlist.Entries.Count + 1}",
            PartySize = partySize,
            Status = status,
            CreatedAt = Now.AddMinutes(-minutesAgo),
        };
        _waitlist.Entries.Add(entry);
        entry.Id = _waitlist.Entries.Count;
        return entry;
    }

    // ── Joining ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinAsync_Accepts_DuringAWalkInSlot()
    {
        WaitlistStatusDto status = await CreateService().JoinAsync(1, Party());

        Assert.Equal("waiting", status.Status);
        Assert.Equal(1, status.Number);
        Assert.Equal(0, status.PartiesAhead);
        Assert.Equal(0, status.EstimatedWaitMinutes);
        Assert.Equal(WaitlistFields.RefLength, status.Ref.Length);
        Assert.Equal("Test Venue", status.VenueName);
    }

    [Fact]
    public async Task JoinAsync_Rejects_OnADayThatTakesBookings()
    {
        _venue = WalkInVenue(walkInOnly: false);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => CreateService().JoinAsync(1, Party()));
        Assert.Equal(ErrorCodes.WaitlistNotWalkInNow, ex.Code);
    }

    [Fact]
    public async Task JoinAsync_Rejects_OutsideOpeningHours()
    {
        _venue = WalkInVenue(open: "11:00", close: "15:00");

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => CreateService().JoinAsync(1, Party()));
        Assert.Equal(ErrorCodes.WaitlistClosedNow, ex.Code);
    }

    [Fact]
    public async Task JoinAsync_Rejects_APartyNoResourceCanFit()
    {
        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => CreateService().JoinAsync(1, Party(partySize: 5)));
        Assert.Equal(ErrorCodes.WaitlistPartyTooLarge, ex.Code);
    }

    [Fact]
    public async Task JoinAsync_Rejects_AnUnknownLocation()
    {
        NotFoundException ex = await Assert.ThrowsAsync<NotFoundException>(() => CreateService().JoinAsync(99, Party()));
        Assert.Equal(ErrorCodes.VenueNotFound, ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task JoinAsync_Rejects_ABlankName(string name)
    {
        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(() => CreateService().JoinAsync(1, Party(name: name)));
        Assert.Equal(ErrorCodes.WaitlistNameRequired, ex.Code);
    }

    [Fact]
    public async Task JoinAsync_Rejects_AMalformedEmail()
    {
        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(() => CreateService().JoinAsync(1, Party(email: "not-an-address")));
        Assert.Equal(ErrorCodes.WaitlistEmailInvalid, ex.Code);
    }

    [Fact]
    public async Task JoinAsync_TrimsAndLowercasesTheEmail_AndKeepsASupportedLocale()
    {
        await CreateService().JoinAsync(1, Party(name: "  Ada  ", email: " Ada@Example.COM ", locale: "fr"));

        WaitlistEntry stored = Assert.Single(_waitlist.Entries);
        Assert.Equal("Ada", stored.Name);
        Assert.Equal("ada@example.com", stored.Email);
        Assert.Equal("fr", stored.Locale);
    }

    [Fact]
    public async Task JoinAsync_StoresNoEmail_WhenLeftBlank_AndFallsBackToEnglish()
    {
        await CreateService().JoinAsync(1, Party(email: "  ", locale: "xx"));

        WaitlistEntry stored = Assert.Single(_waitlist.Entries);
        Assert.Null(stored.Email);
        Assert.Equal("en", stored.Locale);
    }

    [Fact]
    public async Task JoinAsync_NumbersTickets_FromOneEachLocalDay()
    {
        Seed(2, WaitlistStatus.InUse, minutesAgo: 60 * 20);
        Seed(2, WaitlistStatus.InUse, minutesAgo: 30);

        WaitlistStatusDto status = await CreateService().JoinAsync(1, Party());

        Assert.Equal(2, status.Number);
    }

    // ── Status ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStatusAsync_QuotesTheWaitBehindTheQueueAhead()
    {
        _inProgress.Add(new Booking { ResourceId = 1, Date = Now.AddMinutes(-40), EndTime = Now.AddMinutes(20) });
        _inProgress.Add(new Booking { ResourceId = 2, Date = Now.AddMinutes(-15), EndTime = Now.AddMinutes(45) });
        Seed(2, minutesAgo: 20);
        WaitlistEntry mine = Seed(2, minutesAgo: 5);

        WaitlistStatusDto? status = await CreateService().GetStatusAsync(mine.Ref);

        Assert.NotNull(status);
        Assert.Equal(1, status.PartiesAhead);
        Assert.Equal(45, status.EstimatedWaitMinutes);
    }

    [Fact]
    public async Task GetQuoteAsync_QuotesANewPartyBehindTheQueue()
    {
        _inProgress.Add(new Booking { ResourceId = 1, Date = Now.AddMinutes(-40), EndTime = Now.AddMinutes(20) });
        Seed(2, minutesAgo: 5);

        WaitlistQuoteDto quote = await CreateService().GetQuoteAsync(1, 2);

        Assert.True(quote.AcceptingGuests);
        Assert.Equal(1, quote.PartiesWaiting);
        Assert.Equal(20, quote.EstimatedWaitMinutes);
    }

    [Fact]
    public async Task GetQuoteAsync_HasNoEstimate_ForAPartyNoResourceCanFit()
    {
        WaitlistQuoteDto quote = await CreateService().GetQuoteAsync(1, 9);

        Assert.Null(quote.EstimatedWaitMinutes);
    }

    [Fact]
    public async Task GetStatusAsync_IsNull_ForAnUnknownRef()
    {
        Assert.Null(await CreateService().GetStatusAsync("nope"));
    }

    [Fact]
    public async Task GetStatusAsync_IsNull_OnceTheLocationIsGone()
    {
        WaitlistEntry mine = Seed(2);
        _venues.Setup(r => r.GetByIdAsync(1)).ReturnsAsync((Venue?)null);

        Assert.Null(await CreateService().GetStatusAsync(mine.Ref));
    }

    [Fact]
    public async Task GetStatusAsync_CarriesNoPlaceInLine_OnceTheEntryHasLeftTheQueue()
    {
        WaitlistEntry mine = Seed(2, WaitlistStatus.InUse);

        WaitlistStatusDto? status = await CreateService().GetStatusAsync(mine.Ref);

        Assert.Equal("inUse", status!.Status);
        Assert.Null(status.PartiesAhead);
        Assert.Null(status.EstimatedWaitMinutes);
    }

    // ── Leaving ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LeaveAsync_ClosesTheEntry()
    {
        WaitlistEntry mine = Seed(2);
        mine.PushChannel = GuestPushChannels.Expo;
        mine.PushEndpoint = ExpoToken;

        Assert.True(await CreateService().LeaveAsync(mine.Ref));

        Assert.Equal(WaitlistStatus.Left, mine.Status);
        Assert.Equal(Now, mine.ClosedAt);
        Assert.Null(mine.PushAddress());
    }

    [Fact]
    public async Task LeaveAsync_IsIdempotent_AfterTheEntryHasClosed()
    {
        WaitlistEntry mine = Seed(2, WaitlistStatus.InUse);

        Assert.True(await CreateService().LeaveAsync(mine.Ref));

        Assert.Equal(WaitlistStatus.InUse, mine.Status);
    }

    [Fact]
    public async Task LeaveAsync_IsFalse_ForAnUnknownRef()
    {
        Assert.False(await CreateService().LeaveAsync("nope"));
    }

    // ── Push ────────────────────────────────────────────────────────────────

    private const string ExpoToken = "ExponentPushToken[abc]";

    private static WaitlistPushRequest Device(string channel = GuestPushChannels.Expo, string endpoint = ExpoToken, string? p256dh = null, string? auth = null)
        => new() { Channel = channel, Endpoint = endpoint, P256dh = p256dh, Auth = auth };

    [Fact]
    public async Task SetPushAsync_StoresTheAddress_AndReplacesAnEarlierDevice()
    {
        WaitlistEntry mine = Seed(2);
        WaitlistService service = CreateService();

        Assert.True(await service.SetPushAsync(mine.Ref, Device()));
        Assert.True(await service.SetPushAsync(mine.Ref, Device(GuestPushChannels.WebPush, "https://push.example.com/sub", "key", "secret")));

        Assert.Equal(new GuestPushAddress(GuestPushChannels.WebPush, "https://push.example.com/sub", "key", "secret"), mine.PushAddress());
        Assert.True((await service.GetStatusAsync(mine.Ref))!.PushEnabled);
    }

    [Theory]
    [InlineData("sms", "+15550100", "key", "secret")]
    [InlineData(GuestPushChannels.Expo, "not-a-token", null, null)]
    [InlineData(GuestPushChannels.WebPush, "https://push.example.com/sub", null, null)]
    [InlineData(GuestPushChannels.WebPush, "http://127.0.0.1/sub", "key", "secret")]
    public async Task SetPushAsync_RejectsAnInvalidAddress(string channel, string endpoint, string? p256dh, string? auth)
    {
        WaitlistEntry mine = Seed(2);

        ValidationException ex = await Assert.ThrowsAsync<ValidationException>(
            () => CreateService().SetPushAsync(mine.Ref, Device(channel, endpoint, p256dh, auth)));

        Assert.Equal(ErrorCodes.WaitlistPushInvalid, ex.Code);
        Assert.Null(mine.PushAddress());
    }

    [Fact]
    public async Task SetPushAsync_RejectsAnEntryThatHasLeft()
    {
        WaitlistEntry mine = Seed(2, WaitlistStatus.Left);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => CreateService().SetPushAsync(mine.Ref, Device()));

        Assert.Equal(ErrorCodes.WaitlistNotActive, ex.Code);
        Assert.False(await CreateService().SetPushAsync("nope", Device()));
    }

    // ── Board ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBoardAsync_PutsCalledPartiesAheadOfTheQueue()
    {
        WaitlistEntry first = Seed(2, minutesAgo: 30);
        WaitlistEntry called = Seed(4, WaitlistStatus.Notified, minutesAgo: 10);

        WaitlistBoardDto board = await CreateService().GetBoardAsync(1);

        Assert.Equal([called.Id, first.Id], board.Entries.Select(e => e.Id));
        Assert.Equal([0, 1], board.Entries.Select(e => e.PartiesAhead));
        Assert.Equal("notified", board.Entries[0].Status);
    }

    [Fact]
    public async Task GetBoardAsync_FlagsWhoCanBeAssignedNow()
    {
        _bookedResources.Add(2);
        Seed(2);
        Seed(4);

        WaitlistBoardDto board = await CreateService().GetBoardAsync(1);

        Assert.True(board.Entries[0].CanAssignNow);
        Assert.False(board.Entries[1].CanAssignNow);
    }

    [Fact]
    public async Task GetBoardAsync_NamesThePartyAnAssignableRowSkips()
    {
        _bookedResources.Add(1);
        _inProgress.Add(new Booking { ResourceId = 1, Date = Now.AddMinutes(-30), EndTime = Now.AddMinutes(30) });
        WaitlistEntry ahead = Seed(2, minutesAgo: 20);
        Seed(4);

        WaitlistBoardDto board = await CreateService().GetBoardAsync(1);

        Assert.True(board.Entries[1].CanAssignNow);
        Assert.Equal(60, board.Entries[1].EstimatedWaitMinutes);
        Assert.Equal(ahead.Number, board.Entries[1].SkipsNumber);
        Assert.Null(board.Entries[0].SkipsNumber);
    }

    [Fact]
    public async Task GetBoardAsync_NamesNoOneSkipped_WhenTheRowCannotBeAssignedNow()
    {
        _bookedResources.Add(2);
        Seed(4, minutesAgo: 20);
        Seed(4);

        WaitlistBoardDto board = await CreateService().GetBoardAsync(1);

        Assert.False(board.Entries[1].CanAssignNow);
        Assert.Null(board.Entries[1].SkipsNumber);
    }

    [Fact]
    public async Task GetBoardAsync_ReportsWhetherGuestsCanJoin()
    {
        Assert.True((await CreateService().GetBoardAsync(1)).AcceptingGuests);

        _venue = WalkInVenue(walkInOnly: false);
        Assert.False((await CreateService().GetBoardAsync(1)).AcceptingGuests);
    }

    [Fact]
    public async Task GetBoardAsync_HidesGuestDetails_FromAKeyWithoutGuestsRead()
    {
        WaitlistEntry entry = Seed(2);
        entry.Email = "ada@example.com";

        WaitlistBoardDto board = await CreateService(FakeCurrentUser.ApiKey((ApiKeyScopes.Bookings, ApiKeyScopes.Read))).GetBoardAsync(1);

        Assert.Null(board.Entries[0].Name);
        Assert.Null(board.Entries[0].Email);
    }

    [Fact]
    public async Task AddByStaffAsync_AddsAParty_EvenWhenTheLocationTakesBookings()
    {
        _venue = WalkInVenue(walkInOnly: false);

        WaitlistEntryDto entry = await CreateService().AddByStaffAsync(1, Party(partySize: 4));

        Assert.Equal(4, entry.PartySize);
        Assert.Equal("waiting", entry.Status);
        _audit.Verify(a => a.Describe(AuditActions.WaitlistAdd, AuditTargets.WaitlistEntry, It.IsAny<string>(), "#1", 1, It.IsAny<string>()));
    }

    // ── Calling, assigning, removing ────────────────────────────────────────

    [Fact]
    public async Task NotifyAsync_MarksThePartyCalled_AndTellsThem()
    {
        WaitlistEntry entry = Seed(2);

        await CreateService().NotifyAsync(entry.Id);

        Assert.Equal(WaitlistStatus.Notified, entry.Status);
        Assert.Equal(Now, entry.NotifiedAt);
        _notifier.Verify(n => n.NotifyAsync(entry, entry.Venue), Times.Once);
        _audit.Verify(a => a.Describe(AuditActions.WaitlistNotify, AuditTargets.WaitlistEntry, It.IsAny<string>(), It.IsAny<string>(), 1, It.IsAny<string>()));
    }

    [Fact]
    public async Task NotifyAsync_Rejects_APartyThatHasLeft()
    {
        WaitlistEntry entry = Seed(2, WaitlistStatus.Left);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(() => CreateService().NotifyAsync(entry.Id));
        Assert.Equal(ErrorCodes.WaitlistNotActive, ex.Code);
    }

    [Fact]
    public async Task NotifyAsync_Rejects_AnUnknownEntry()
    {
        NotFoundException ex = await Assert.ThrowsAsync<NotFoundException>(() => CreateService().NotifyAsync(42));
        Assert.Equal(ErrorCodes.WaitlistNotFound, ex.Code);
    }

    [Fact]
    public async Task AssignAsync_CreatesABookingOnTheSmallestFreeResource()
    {
        WaitlistEntry entry = Seed(2);
        entry.Email = "ada@example.com";

        AssignWaitlistEntryResponse result = await CreateService().AssignAsync(entry.Id, new AssignWaitlistEntryRequest());

        _bookings.Verify(b => b.AddAsync(It.Is<Booking>(bk =>
            bk.ResourceId == 1 && bk.SectionId == 7 && bk.Date == Now && bk.EndTime == Now.AddMinutes(60)
            && bk.PartySize == 2 && bk.CustomerName == entry.Name && bk.CustomerEmail == "ada@example.com")));
        Assert.Equal(500, result.BookingId);
        Assert.Equal(WaitlistStatus.InUse, entry.Status);
        Assert.Equal(500, entry.BookingId);
        Assert.Equal("inUse", result.Entry.Status);
        _queue.Verify(q => q.EnqueueBookingCreated(It.IsAny<Booking>(), "Test Venue"), Times.Once);
    }

    [Fact]
    public async Task AssignAsync_UsesAWalkInOnlyResource()
    {
        _venue.Sections.Single().Resources.First(t => t.Id == 1).WalkInOnly = true;
        WaitlistEntry entry = Seed(2);

        await CreateService().AssignAsync(entry.Id, new AssignWaitlistEntryRequest());

        _bookings.Verify(b => b.AddAsync(It.Is<Booking>(bk => bk.ResourceId == 1)));
    }

    [Fact]
    public async Task AssignAsync_RecordsTheBookingAsInUse()
    {
        WaitlistEntry entry = Seed(2);

        await CreateService().AssignAsync(entry.Id, new AssignWaitlistEntryRequest());

        _bookings.Verify(b => b.AddAsync(It.Is<Booking>(bk => bk.Status == BookingStatus.InUse)));
    }

    [Fact]
    public async Task AssignAsync_UsesTheChosenResource_WhenItIsFree()
    {
        WaitlistEntry entry = Seed(2);

        await CreateService().AssignAsync(entry.Id, new AssignWaitlistEntryRequest { ResourceId = 2 });

        _bookings.Verify(b => b.AddAsync(It.Is<Booking>(bk => bk.ResourceId == 2)));
    }

    [Fact]
    public async Task AssignAsync_Rejects_AResourceThatIsNotFree()
    {
        _bookedResources.Add(2);
        WaitlistEntry entry = Seed(2);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => CreateService().AssignAsync(entry.Id, new AssignWaitlistEntryRequest { ResourceId = 2 }));
        Assert.Equal(ErrorCodes.WaitlistNoResourceFree, ex.Code);
    }

    [Fact]
    public async Task AssignAsync_Rejects_WhenNoResourceIsFree()
    {
        _bookedResources.UnionWith([1, 2]);
        WaitlistEntry entry = Seed(2);

        ConflictException ex = await Assert.ThrowsAsync<ConflictException>(
            () => CreateService().AssignAsync(entry.Id, new AssignWaitlistEntryRequest()));
        Assert.Equal(ErrorCodes.WaitlistNoResourceFree, ex.Code);
        Assert.Equal(WaitlistStatus.Waiting, entry.Status);
    }

    [Fact]
    public async Task AssignAsync_BooksAGroup_WhenOneIsChosen()
    {
        _venue.Groups.Add(new ResourceGroup
        {
            Id = 9,
            VenueId = 1,
            CombinedCapacity = 6,
            Members = new List<ResourceGroupMembership>
            {
                new() { ResourceGroupId = 9, ResourceId = 1, Resource = _venue.Sections.First().Resources.First() },
                new() { ResourceGroupId = 9, ResourceId = 2, Resource = _venue.Sections.First().Resources.Last() },
            },
        });
        WaitlistEntry entry = Seed(6);

        await CreateService().AssignAsync(entry.Id, new AssignWaitlistEntryRequest { ResourceGroupId = 9 });

        _bookings.Verify(b => b.AddAsync(It.Is<Booking>(bk => bk.ResourceGroupId == 9 && bk.ResourceId == null)));
    }

    [Fact]
    public async Task RemoveAsync_ClosesTheEntryAsLeft()
    {
        WaitlistEntry entry = Seed(2, WaitlistStatus.Notified);

        await CreateService().RemoveAsync(entry.Id);

        Assert.Equal(WaitlistStatus.Left, entry.Status);
        _audit.Verify(a => a.Describe(AuditActions.WaitlistRemove, AuditTargets.WaitlistEntry, It.IsAny<string>(), It.IsAny<string>(), 1, It.IsAny<string>()));
    }

    [Fact]
    public async Task SweepAsync_ExpiresEntriesPastStaleAfter_AndDeletesPastRetainFor()
    {
        await CreateService().SweepAsync();

        Assert.Equal(Now.AddHours(-6), _waitlist.ExpiredBefore);
        Assert.Equal(Now.AddDays(-7), _waitlist.DeletedBefore);
    }

    /// <summary>A list-backed repository, so the service's reads see its own writes.</summary>
    private sealed class FakeWaitlistRepository : IWaitlistRepository
    {
        public List<WaitlistEntry> Entries { get; } = new();
        public DateTime? ExpiredBefore { get; private set; }
        public DateTime? DeletedBefore { get; private set; }

        public Task<WaitlistEntry?> GetByIdAsync(int id) => Task.FromResult(Entries.FirstOrDefault(e => e.Id == id));

        public Task<WaitlistEntry?> GetByRefAsync(string entryRef) => Task.FromResult(Entries.FirstOrDefault(e => e.Ref == entryRef));

        public Task<List<WaitlistEntry>> GetActiveForVenueAsync(int venueId)
            => Task.FromResult(Entries.Where(e => e.VenueId == venueId && e.IsActive).OrderBy(e => e.CreatedAt).ToList());

        public Task<int> CountCreatedSinceAsync(int venueId, DateTime sinceUtc)
            => Task.FromResult(Entries.Count(e => e.VenueId == venueId && e.CreatedAt >= sinceUtc));

        public Task<WaitlistEntry> AddAsync(WaitlistEntry entry)
        {
            Entries.Add(entry);
            entry.Id = Entries.Count;
            return Task.FromResult(entry);
        }

        public Task SaveChangesAsync() => Task.CompletedTask;

        public Task<int> ExpireActiveCreatedBeforeAsync(DateTime cutoffUtc, DateTime nowUtc)
        {
            ExpiredBefore = cutoffUtc;
            return Task.FromResult(0);
        }

        public Task<int> DeleteCreatedBeforeAsync(DateTime cutoffUtc)
        {
            DeletedBefore = cutoffUtc;
            return Task.FromResult(0);
        }
    }
}
