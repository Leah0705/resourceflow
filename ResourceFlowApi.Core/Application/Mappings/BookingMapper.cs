using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Domain;
using Riok.Mapperly.Abstractions;

namespace ResourceFlowApi.Core.Application.Mappings;

[Mapper]
public partial class BookingMapper
{
    [MapperIgnoreTarget(nameof(BookingDto.isHeld))]
    [MapperIgnoreTarget(nameof(BookingDto.HoldId))]
    [MapperIgnoreTarget(nameof(BookingDto.MemberResourceIds))]
    [MapperIgnoreSource(nameof(Booking.Venue))]
    [MapperIgnoreSource(nameof(Booking.ResourceGroup))]
    [MapProperty("Resource.Name", nameof(BookingDto.ResourceName))]
    [MapProperty("Resource.Capacity", nameof(BookingDto.ResourceCapacity))]
    [MapProperty("Section.Name", nameof(BookingDto.SectionName))]
    public partial BookingDto ToDto(Booking booking);

    /// <summary>
    /// Maps a booking then fills the display fields for a combinable-resource group booking. The base
    /// <see cref="ToDto"/> maps ResourceName/ResourceCapacity/SectionName from the single Resource/Section, which
    /// are null for a group booking (the booking reserves a <see cref="ResourceGroup"/>). For those rows
    /// we substitute a readable group label, the group's CombinedCapacity, and the first member's section
    /// so the guest confirmation, lookup, admin grid, and calendar all show something meaningful
    /// instead of silently dropping the Resource/Section rows.
    /// </summary>
    public BookingDto ToDtoWithGroup(Booking booking)
    {
        BookingDto dto = ToDto(booking);
        if (booking.ResourceGroup is { } group)
        {
            dto.ResourceGroupId = group.Id;
            // For a group booking Resource is null, so the base map left these empty. Substitute a
            // readable group label and the group's combined capacity so display surfaces (guest
            // confirmation, admin grid, email, calendar) show something meaningful. SectionName is
            // already mapped from the persisted SectionId (set to a member's section at create time).
            dto.ResourceName ??= GroupLabel(group);
            dto.ResourceCapacity ??= group.CombinedCapacity;
        }

        return dto;
    }

    /// <summary>A human-readable label for a group: its name, else the member resource names joined by " + ".</summary>
    public static string GroupLabel(ResourceGroup group)
    {
        if (!string.IsNullOrWhiteSpace(group.Name))
        {
            return group.Name;
        }

        var names = group.Members
            .OrderBy(m => m.ResourceId)
            .Select(m => m.Resource?.Name ?? $"Resource {m.ResourceId}")
            .ToList();
        return names.Count > 0 ? $"Resources {string.Join(" + ", names)}" : "Combined resources";
    }

    [MapperIgnoreTarget(nameof(Booking.Resource))]
    [MapperIgnoreTarget(nameof(Booking.Section))]
    [MapperIgnoreTarget(nameof(Booking.Venue))]
    [MapperIgnoreTarget(nameof(Booking.BookingRef))]
    [MapperIgnoreTarget(nameof(Booking.EndTime))]
    [MapperIgnoreTarget(nameof(Booking.ResourceGroup))]
    [MapperIgnoreSource(nameof(BookingDto.isHeld))]
    [MapperIgnoreSource(nameof(BookingDto.HoldId))]
    [MapperIgnoreSource(nameof(BookingDto.MemberResourceIds))]
    [MapperIgnoreSource(nameof(BookingDto.BookingRef))]
    [MapperIgnoreSource(nameof(BookingDto.EndTime))]
    [MapperIgnoreSource(nameof(BookingDto.ResourceName))]
    [MapperIgnoreSource(nameof(BookingDto.SectionName))]
    [MapperIgnoreSource(nameof(BookingDto.ResourceCapacity))]
    public partial Booking ToEntity(BookingDto dto);

    public partial IEnumerable<BookingDto> ToDtoList(IEnumerable<Booking> bookings);

    /// <summary>List variant of <see cref="ToDtoWithGroup"/> for the venue bookings feed.</summary>
    public IEnumerable<BookingDto> ToDtoWithGroupList(IEnumerable<Booking> bookings)
        => bookings.Select(ToDtoWithGroup).ToList();
}
