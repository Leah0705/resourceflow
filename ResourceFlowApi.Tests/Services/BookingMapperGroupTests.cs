using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Mappings;
using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Tests.Services;

/// <summary>
/// Display-enrichment for combinable-group bookings. The Mapperly-generated <c>ToDto</c> maps
/// ResourceName/ResourceCapacity from <c>Booking.Resource</c>, which is null on a group booking — without the
/// substitution in <c>ToDtoWithGroup</c> the guest confirmation, lookup, admin grid, and calendar
/// would all render a blank resource.
/// </summary>
public class BookingMapperGroupTests
{
    private static readonly BookingMapper Mapper = new();

    private static Booking GroupBooking(ResourceGroup group) => new()
    {
        Id = 1,
        VenueId = 1,
        SectionId = 2,
        ResourceId = null,
        ResourceGroupId = group.Id,
        ResourceGroup = group,
        PartySize = 6,
        BookingRef = "GRP1",
        Date = DateTime.UtcNow.AddDays(1)
    };

    private static ResourceGroupMembership Member(int resourceId, string? resourceName) => new()
    {
        ResourceGroupId = 1,
        ResourceId = resourceId,
        Resource = resourceName == null ? null : new Resource { Id = resourceId, Name = resourceName, Capacity = 4 }
    };

    [Fact]
    public void GroupLabel_PrefersTheAdminAssignedName()
    {
        var group = new ResourceGroup { Id = 1, Name = "Window desks", CombinedCapacity = 8 };
        group.Members.Add(Member(3, "T3"));
        group.Members.Add(Member(2, "T2"));

        Assert.Equal("Window desks", BookingMapper.GroupLabel(group));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void GroupLabel_FallsBackToMemberNames_WhenTheNameIsBlank(string name)
    {
        var group = new ResourceGroup { Id = 1, Name = name, CombinedCapacity = 8 };
        group.Members.Add(Member(3, "T3"));
        group.Members.Add(Member(2, "T2"));

        // Members are ordered by resource id, not insertion order, so the label is stable.
        Assert.Equal("Resources T2 + T3", BookingMapper.GroupLabel(group));
    }

    [Fact]
    public void GroupLabel_UsesResourceIdPlaceholder_WhenAMemberResourceIsNotLoaded()
    {
        var group = new ResourceGroup { Id = 1, CombinedCapacity = 8 };
        group.Members.Add(Member(2, "T2"));
        group.Members.Add(Member(9, null));

        Assert.Equal("Resources T2 + Resource 9", BookingMapper.GroupLabel(group));
    }

    [Fact]
    public void GroupLabel_FallsBackToAGenericLabel_WhenTheGroupHasNoMembers()
    {
        var group = new ResourceGroup { Id = 1, CombinedCapacity = 8 };

        Assert.Equal("Combined resources", BookingMapper.GroupLabel(group));
    }

    [Fact]
    public void ToDtoWithGroup_FillsGroupIdLabelAndCombinedCapacity()
    {
        var group = new ResourceGroup { Id = 5, Name = "Window desks", CombinedCapacity = 8 };
        group.Members.Add(Member(2, "T2"));
        group.Members.Add(Member(3, "T3"));

        BookingDto dto = Mapper.ToDtoWithGroup(GroupBooking(group));

        Assert.Equal(5, dto.ResourceGroupId);
        Assert.Equal("Window desks", dto.ResourceName);
        Assert.Equal(8, dto.ResourceCapacity);
    }

    [Fact]
    public void ToDtoWithGroup_KeepsTheRealResourceName_WhenTheBookingAlsoHasAResource()
    {
        // Defensive: the substitution is null-coalescing, so a row that somehow carries both must
        // keep the concrete resource's own name and capacity rather than being relabelled.
        var group = new ResourceGroup { Id = 5, Name = "Window desks", CombinedCapacity = 8 };
        group.Members.Add(Member(2, "T2"));
        group.Members.Add(Member(3, "T3"));

        Booking booking = GroupBooking(group);
        booking.ResourceId = 2;
        booking.Resource = new Resource { Id = 2, Name = "T2", Capacity = 4 };

        BookingDto dto = Mapper.ToDtoWithGroup(booking);

        Assert.Equal("T2", dto.ResourceName);
        Assert.Equal(4, dto.ResourceCapacity);
        Assert.Equal(5, dto.ResourceGroupId);
    }

    [Fact]
    public void ToDtoWithGroup_LeavesASingleResourceBookingUntouched()
    {
        var booking = new Booking
        {
            Id = 2,
            VenueId = 1,
            SectionId = 1,
            ResourceId = 7,
            Resource = new Resource { Id = 7, Name = "T7", Capacity = 4 },
            Section = new Section { Id = 1, Name = "Main" },
            PartySize = 2,
            BookingRef = "SNG1",
            Date = DateTime.UtcNow.AddDays(1)
        };

        BookingDto dto = Mapper.ToDtoWithGroup(booking);

        Assert.Null(dto.ResourceGroupId);
        Assert.Equal("T7", dto.ResourceName);
        Assert.Equal("Main", dto.SectionName);
    }

    [Fact]
    public void ToDtoWithGroupList_EnrichesEveryRow()
    {
        var group = new ResourceGroup { Id = 5, Name = "Window desks", CombinedCapacity = 8 };
        group.Members.Add(Member(2, "T2"));
        group.Members.Add(Member(3, "T3"));

        List<BookingDto> dtos = Mapper.ToDtoWithGroupList([GroupBooking(group), GroupBooking(group)]).ToList();

        Assert.Equal(2, dtos.Count);
        Assert.All(dtos, d =>
        {
            Assert.Equal(5, d.ResourceGroupId);
            Assert.Equal("Window desks", d.ResourceName);
            Assert.Equal(8, d.ResourceCapacity);
        });
    }
}
