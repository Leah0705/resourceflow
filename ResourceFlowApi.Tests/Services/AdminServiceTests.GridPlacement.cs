using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Services;
using ResourceFlowApi.Core.Application.Utilities;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Services;

/// <summary>
/// What the admin timetable needs off a booking to draw it on the floor: which unit it reserves
/// (a resource or a combinable group) and when the slot actually ends.
/// </summary>
public partial class AdminServiceTests
{
    private void SeedGroup(int venueId, int groupId, string? name = null)
    {
        _db.Resources.Add(new Resource { Id = 80 + groupId, Name = "T8", Capacity = 4, SectionId = venueId });
        _db.Resources.Add(new Resource { Id = 90 + groupId, Name = "T9", Capacity = 4, SectionId = venueId });
        _db.ResourceGroups.Add(new ResourceGroup
        {
            Id = groupId,
            Name = name,
            VenueId = venueId,
            CombinedCapacity = 7,
            Members =
            [
                new ResourceGroupMembership { ResourceGroupId = groupId, ResourceId = 80 + groupId },
                new ResourceGroupMembership { ResourceGroupId = groupId, ResourceId = 90 + groupId },
            ],
        });
    }

    [Fact]
    public async Task GetBookingsAsync_ExposesResourceGroupId_SoAGroupBookingCanBePlaced()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        SeedGroup(venueId: 1, groupId: 5, name: "Window desks");
        _db.Bookings.Add(new Booking
        {
            Id = 1,
            VenueId = 1,
            SectionId = 1,
            ResourceId = null,
            ResourceGroupId = 5,
            PartySize = 7,
            Date = DateTime.UtcNow.Date.AddHours(19),
            BookingRef = "GROUP",
        });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> bookings = await svc.GetBookingsAsync(1, null, "all");

        BookingDetailDto dto = Assert.Single(bookings);
        Assert.Null(dto.ResourceId);
        Assert.Equal(5, dto.ResourceGroupId);
        Assert.Equal("Window desks", dto.ResourceName);
    }

    [Fact]
    public async Task GetBookingsAsync_LabelsAnUnnamedGroupFromItsMemberResources()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        SeedGroup(venueId: 1, groupId: 6);
        _db.Bookings.Add(new Booking
        {
            Id = 1,
            VenueId = 1,
            SectionId = 1,
            ResourceGroupId = 6,
            PartySize = 7,
            Date = DateTime.UtcNow.Date.AddHours(19),
            BookingRef = "GROUP",
        });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> bookings = await svc.GetBookingsAsync(1, null, "all");

        Assert.Equal("Resources T8 + T9", Assert.Single(bookings).ResourceName);
    }

    [Fact]
    public async Task GetBookingsAsync_KeepsResourceIdAndLeavesGroupNull_ForASingleResourceBooking()
    {
        AdminService svc = CreateService();
        SeedBase(1);
        _db.Bookings.Add(new Booking
        {
            Id = 1,
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            PartySize = 2,
            Date = DateTime.UtcNow.Date.AddHours(19),
            BookingRef = "SINGLE",
        });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> bookings = await svc.GetBookingsAsync(1, null, "all");

        BookingDetailDto dto = Assert.Single(bookings);
        Assert.Equal(1, dto.ResourceId);
        Assert.Null(dto.ResourceGroupId);
    }

    [Fact]
    public async Task GetBookingsAsync_ResolvesAStoredlessEndTimeToTheLocationsDefaultDuration()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "Test",
            Timezone = "UTC",
            DefaultBookingDurationMinutes = 105,
        });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        DateTime start = DateTime.UtcNow.Date.AddHours(19);
        _db.Bookings.Add(new Booking
        {
            Id = 1,
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            PartySize = 2,
            Date = start,
            EndTime = null,
            BookingRef = "LEGACY",
        });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> bookings = await svc.GetBookingsAsync(1, null, "all");

        Assert.Equal(start.AddMinutes(105), Assert.Single(bookings).EndTime);
    }

    [Fact]
    public async Task GetBookingsAsync_KeepsAStoredEndTimeRatherThanTheDefaultDuration()
    {
        AdminService svc = CreateService();
        _db.Venues.Add(new Venue
        {
            Id = 1,
            Name = "Test",
            Timezone = "UTC",
            DefaultBookingDurationMinutes = 105,
        });
        _db.Sections.Add(new Section { Id = 1, Name = "Main", VenueId = 1 });
        _db.Resources.Add(new Resource { Id = 1, Name = "T1", Capacity = 4, SectionId = 1 });
        DateTime start = DateTime.UtcNow.Date.AddHours(19);
        _db.Bookings.Add(new Booking
        {
            Id = 1,
            VenueId = 1,
            SectionId = 1,
            ResourceId = 1,
            PartySize = 2,
            Date = start,
            EndTime = start.AddMinutes(180),
            BookingRef = "EXTENDED",
        });
        await _db.SaveChangesAsync();

        List<BookingDetailDto> bookings = await svc.GetBookingsAsync(1, null, "all");

        Assert.Equal(start.AddMinutes(180), Assert.Single(bookings).EndTime);
    }
}
