using ResourceFlowApi.Core.Domain;

namespace ResourceFlowApi.Infrastructure.Persistence;

public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (!db.Venues.Any())
        {
            var r1 = new Venue
            {
                Name = "Central Workspace",
                Address = "123 Main St",
                Sections = new List<Section>
                {
                    new Section
                    {
                        Name = "Meeting Rooms",
                        Resources = new List<Resource>
                        {
                            new Resource { Name = "T1", Capacity = 4 },
                            new Resource { Name = "T2", Capacity = 2 }
                        }
                    },
                    new Section
                    {
                        Name = "Studios",
                        Resources = new List<Resource>
                        {
                            new Resource { Name = "P1", Capacity = 4 }
                        }
                    }
                }
            };

            var r2 = new Venue
            {
                Name = "Harbour Studio",
                Address = "456 Elm St",
                Sections = new List<Section>
                {
                    new Section
                    {
                        Name = "Workspaces",
                        Resources = new List<Resource>
                        {
                            new Resource { Name = "B1", Capacity = 2 },
                            new Resource { Name = "B2", Capacity = 2 }
                        }
                    }
                }
            };

            db.Venues.AddRange(r1, r2);
            db.SaveChanges();
        }

        SeedHighlights(db);
    }

    private static void SeedHighlights(AppDbContext db)
    {
        if (db.Highlights.Any())
        {
            return;
        }

        db.Highlights.AddRange(
            new VenueHighlight
            {
                Title = "Meeting rooms",
                Body = "Reserve a room for focused work or team collaboration.",
                IconKey = "business-outline",
                SortOrder = 0,
            },
            new VenueHighlight
            {
                Title = "Creative studios",
                Body = "Book a dedicated space for creative sessions and workshops.",
                IconKey = "color-palette-outline",
                SortOrder = 1,
            },
            new VenueHighlight
            {
                Title = "Capacity-aware booking",
                Body = "Choose a resource that accommodates your group.",
                IconKey = "people-outline",
                SortOrder = 2,
            },
            new VenueHighlight
            {
                Title = "Multiple locations",
                Body = "Compare local opening hours and availability across sites.",
                IconKey = "heart-outline",
                SortOrder = 3,
            }
        );
        db.SaveChanges();
    }
}
