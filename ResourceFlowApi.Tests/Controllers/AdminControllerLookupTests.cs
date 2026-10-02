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
    public class AdminControllerLookupTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly AppDbContext _dbContext;
        private readonly AdminController _adminController;

        public AdminControllerLookupTests()
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options =>
                options.UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString()));

            _serviceProvider = services.BuildServiceProvider();
            _dbContext = _serviceProvider.GetRequiredService<AppDbContext>();

            // Seed test data
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
            var r1 = new Venue { Name = "B Venue", Address = "123 St" };
            var r2 = new Venue { Name = "A Venue", Address = "456 Ave" };
            _dbContext.Venues.AddRange(r1, r2);
            _dbContext.SaveChanges();

            var s1 = new Section { Name = "Main Section", VenueId = r1.Id };
            var s2 = new Section { Name = "Outdoor", VenueId = r1.Id };
            _dbContext.Sections.AddRange(s1, s2);
            _dbContext.SaveChanges();
        }

        [Fact]
        public async Task GetVenues_ReturnsSortedVenues()
        {
            // Act
            IActionResult result = await _adminController.GetVenues();

            // Assert
            OkObjectResult okResult = Assert.IsType<OkObjectResult>(result);
            List<LookupDto> venues = Assert.IsType<List<LookupDto>>(okResult.Value);
            Assert.Equal(2, venues.Count);

            // Check sorting (A should be first)
            Assert.Equal("A Venue", venues[0].Name);
            Assert.Equal("B Venue", venues[1].Name);
        }

        [Fact]
        public async Task GetSections_ReturnsSectionsForVenue()
        {
            // Arrange
            Venue venue = await _dbContext.Venues.FirstAsync(r => r.Name == "B Venue");

            // Act
            IActionResult result = await _adminController.GetSections(venue.Id);

            // Assert
            OkObjectResult okResult = Assert.IsType<OkObjectResult>(result);
            List<LookupDto> sections = Assert.IsType<List<LookupDto>>(okResult.Value);
            Assert.Equal(2, sections.Count);
        }

        [Fact]
        public async Task GetSections_WithInvalidId_ReturnsEmptyList()
        {
            // Act
            IActionResult result = await _adminController.GetSections(99999);

            // Assert
            OkObjectResult okResult = Assert.IsType<OkObjectResult>(result);
            List<LookupDto> sections = Assert.IsType<List<LookupDto>>(okResult.Value);
            Assert.Empty(sections);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
            _serviceProvider.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
