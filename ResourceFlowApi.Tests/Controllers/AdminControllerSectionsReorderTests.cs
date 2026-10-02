using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ResourceFlowApi.Controllers;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;
using ResourceFlowApi.Infrastructure.Persistence.Repositories;

namespace ResourceFlowApi.Tests.Controllers
{
    public class AdminControllerSectionsReorderTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly AppDbContext _dbContext;
        private readonly AdminController _adminController;
        private Venue _venue = null!;
        private Section _first = null!;
        private Section _second = null!;

        public AdminControllerSectionsReorderTests()
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

            _serviceProvider = services.BuildServiceProvider();
            _dbContext = _serviceProvider.GetRequiredService<AppDbContext>();

            SeedTestData();

            var holdService = new Mock<IHoldService>().Object;
            var emailService = new MockEmailService();
            var adminService = new ResourceFlowApi.Core.Application.Services.AdminService(
                new BookingRepository(_dbContext),
                new BookingFilterRepository(_dbContext),
                new VenueRepository(_dbContext),
                new SectionRepository(_dbContext),
                new ResourceRepository(_dbContext),
                holdService,
                emailService);
            _adminController = new AdminController(adminService);
        }

        private void SeedTestData()
        {
            _venue = new Venue { Name = "Test Venue" };
            _dbContext.Venues.Add(_venue);
            _dbContext.SaveChanges();

            _first = new Section { Name = "First", VenueId = _venue.Id, SortOrder = 0 };
            _second = new Section { Name = "Second", VenueId = _venue.Id, SortOrder = 1 };
            _dbContext.Sections.AddRange(_first, _second);
            _dbContext.SaveChanges();
        }

        [Fact]
        public async Task ReorderSections_ReturnsNoContent_AndPersistsNewOrder()
        {
            var req = new ReorderSectionsRequest { SectionIds = [_second.Id, _first.Id] };

            IActionResult result = await _adminController.ReorderSections(_venue.Id, req);

            Assert.IsType<NoContentResult>(result);
            Section? first = await _dbContext.Sections.FindAsync(_first.Id);
            Section? second = await _dbContext.Sections.FindAsync(_second.Id);
            Assert.Equal(1, first!.SortOrder);
            Assert.Equal(0, second!.SortOrder);
        }

        [Fact]
        public async Task ReorderSections_ReadBack_ReflectsNewOrder()
        {
            var req = new ReorderSectionsRequest { SectionIds = [_second.Id, _first.Id] };
            await _adminController.ReorderSections(_venue.Id, req);

            IActionResult sectionsResult = await _adminController.GetSections(_venue.Id);

            OkObjectResult ok = Assert.IsType<OkObjectResult>(sectionsResult);
            List<LookupDto> sections = Assert.IsType<List<LookupDto>>(ok.Value);
            Assert.Equal(["Second", "First"], sections.Select(s => s.Name));
        }

        [Fact]
        public async Task ReorderSections_ReturnsNotFound_WhenVenueMissing()
        {
            var req = new ReorderSectionsRequest { SectionIds = [_first.Id, _second.Id] };

            IActionResult result = await _adminController.ReorderSections(9999, req);

            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task ReorderSections_ReturnsBadRequest_WhenSectionIdsInvalid()
        {
            var req = new ReorderSectionsRequest { SectionIds = [_first.Id] };

            IActionResult result = await _adminController.ReorderSections(_venue.Id, req);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ReorderSections_ReturnsNoContent_WhenVenueHasNoSections_AndEmptyListSent()
        {
            var emptyVenue = new Venue { Name = "No Sections" };
            _dbContext.Venues.Add(emptyVenue);
            _dbContext.SaveChanges();

            var req = new ReorderSectionsRequest { SectionIds = [] };

            IActionResult result = await _adminController.ReorderSections(emptyVenue.Id, req);

            Assert.IsType<NoContentResult>(result);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
            _serviceProvider.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
