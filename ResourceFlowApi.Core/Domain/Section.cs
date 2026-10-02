namespace ResourceFlowApi.Core.Domain;

public class Section
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public int SortOrder { get; set; }

    // Relation to Venue
    public int VenueId { get; set; }
    public Venue? Venue { get; set; }

    public ICollection<Resource> Resources { get; set; } = new List<Resource>();
}
