using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using ResourceFlowApi.Controllers;
using ResourceFlowApi.Core.Application.DTOs;
using ResourceFlowApi.Core.Application.Exceptions;
using ResourceFlowApi.Core.Application.Interfaces;
using ResourceFlowApi.Core.Domain;
using ResourceFlowApi.Infrastructure.Persistence;
using ResourceFlowApi.Infrastructure.Persistence.Repositories;

namespace ResourceFlowApi.Tests.Controllers
{
    public class AdminControllerUpdateTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly AppDbContext _dbContext;
        private readonly AdminController _adminController;

        public AdminControllerUpdateTests()
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
            var venue1 = new Venue { Name = "Test Venue 1", Address = "123 Test St" };
            var venue2 = new Venue { Name = "Test Venue 2", Address = "456 Test Ave" };
            _dbContext.Venues.AddRange(venue1, venue2);
            _dbContext.SaveChanges();

            var section1 = new Section { Name = "Section 1", VenueId = venue1.Id };
            var section2 = new Section { Name = "Section 2", VenueId = venue2.Id };
            _dbContext.Sections.AddRange(section1, section2);
            _dbContext.SaveChanges();

            var resource1 = new Resource { Name = "Resource 1", Capacity = 4, SectionId = section1.Id };
            var resource2 = new Resource { Name = "Resource 2", Capacity = 2, SectionId = section1.Id };
            var resource3 = new Resource { Name = "Resource 3", Capacity = 6, SectionId = section2.Id };
            _dbContext.Resources.AddRange(resource1, resource2, resource3);
            _dbContext.SaveChanges();

            var booking = new Booking
            {
                VenueId = venue1.Id,
                SectionId = section1.Id,
                ResourceId = resource1.Id,
                Date = DateTime.UtcNow.AddDays(1),
                CustomerEmail = "original@test.com",
                PartySize = 2,
                BookingRef = "UPDATE001",
                SpecialRequests = "None"
            };
            _dbContext.Bookings.Add(booking);
            _dbContext.SaveChanges();
        }

        [Fact]
        public async Task AdminUpdateBooking_WithValidData_UpdatesBooking()
        {
            // Arrange
            Booking booking = await _dbContext.Bookings.FirstAsync(b => b.BookingRef == "UPDATE001");
            Resource resource2 = await _dbContext.Resources.FirstAsync(t => t.Name == "Resource 2");
            DateTime newDate = DateTime.UtcNow.AddDays(2);
            var req = new AdminUpdateBookingRequest
            {
                ResourceId = resource2.Id,
                Date = newDate,
                PartySize = 2,
                CustomerEmail = "updated@test.com",
                SpecialRequests = "Lots of requests"
            };

            // Act
            IActionResult result = await _adminController.AdminUpdateBooking(booking.Id, req);

            // Assert
            OkObjectResult okResult = Assert.IsType<OkObjectResult>(result);
            BookingDetailDto updatedDto = Assert.IsType<BookingDetailDto>(okResult.Value);

            Assert.Equal(resource2.Id, updatedDto.ResourceId);
            Assert.Equal(newDate, updatedDto.Date);
            Assert.Equal(2, updatedDto.PartySize);
            Assert.Equal("updated@test.com", updatedDto.CustomerEmail);
            Assert.Equal("Lots of requests", updatedDto.SpecialRequests);

            // Verify database
            Booking? dbBooking = await _dbContext.Bookings.FindAsync(booking.Id);
            Assert.NotNull(dbBooking);
            Assert.Equal(resource2.Id, dbBooking.ResourceId);
            Assert.Equal(newDate, dbBooking.Date);
            Assert.Equal(2, dbBooking.PartySize);
            Assert.Equal("updated@test.com", dbBooking.CustomerEmail);
            Assert.Equal("Lots of requests", dbBooking.SpecialRequests);
        }

        [Fact]
        public async Task AdminUpdateBooking_ChangeVenueAndSection_UpdatesBooking()
        {
            // Arrange
            Booking booking = await _dbContext.Bookings.FirstAsync(b => b.BookingRef == "UPDATE001");
            Venue venue2 = await _dbContext.Venues.FirstAsync(r => r.Name == "Test Venue 2");
            Section section2 = await _dbContext.Sections.FirstAsync(s => s.Name == "Section 2");
            Resource resource3 = await _dbContext.Resources.FirstAsync(t => t.Name == "Resource 3");

            var req = new AdminUpdateBookingRequest
            {
                VenueId = venue2.Id,
                SectionId = section2.Id,
                ResourceId = resource3.Id
            };

            // Act
            IActionResult result = await _adminController.AdminUpdateBooking(booking.Id, req);

            // Assert
            OkObjectResult okResult = Assert.IsType<OkObjectResult>(result);
            BookingDetailDto updatedDto = Assert.IsType<BookingDetailDto>(okResult.Value);

            Assert.Equal(venue2.Id, updatedDto.VenueId);
            Assert.Equal(section2.Id, updatedDto.SectionId);
            Assert.Equal(resource3.Id, updatedDto.ResourceId);

            // Verify database
            Booking? dbBooking = await _dbContext.Bookings.FindAsync(booking.Id);
            Assert.NotNull(dbBooking);
            Assert.Equal(venue2.Id, dbBooking.VenueId);
            Assert.Equal(section2.Id, dbBooking.SectionId);
            Assert.Equal(resource3.Id, dbBooking.ResourceId);
        }

        [Fact]
        public async Task AdminUpdateBooking_WithInvalidResource_ThrowsValidationException()
        {
            // Post-Bundle-6 the controller propagates the typed exception; the 400 status
            // is applied by GlobalExceptionHandler (covered by GlobalExceptionHandlerTests).
            Booking booking = await _dbContext.Bookings.FirstAsync(b => b.BookingRef == "UPDATE001");
            Resource resourceInOtherVenue = await _dbContext.Resources.FirstAsync(t => t.Name == "Resource 3");
            var req = new AdminUpdateBookingRequest
            {
                ResourceId = resourceInOtherVenue.Id
            };

            await Assert.ThrowsAsync<ValidationException>(
                () => _adminController.AdminUpdateBooking(booking.Id, req));
        }

        [Fact]
        public async Task AdminUpdateBooking_NonExistentBooking_ReturnsNotFound()
        {
            // Arrange
            var req = new AdminUpdateBookingRequest { PartySize = 5 };

            // Act
            IActionResult result = await _adminController.AdminUpdateBooking(99999, req);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task AdminUpdateBooking_ReturnsBadRequest_WhenPartySizeExceedsResourceCapacity()
        {
            // Arrange
            var venue = new Venue { Name = "Test Venue", Address = "123 Test St" };
            _dbContext.Venues.Add(venue);
            _dbContext.SaveChanges();

            var section = new Section { Name = "Main", VenueId = venue.Id };
            _dbContext.Sections.Add(section);
            _dbContext.SaveChanges();

            var resource = new Resource { Name = "Resource 1", Capacity = 2, SectionId = section.Id };
            _dbContext.Resources.Add(resource);
            _dbContext.SaveChanges();

            var booking = new Booking
            {
                VenueId = venue.Id,
                SectionId = section.Id,
                ResourceId = resource.Id,
                Date = DateTime.UtcNow.AddDays(1),
                CustomerEmail = "guest@test.com",
                PartySize = 1,
                BookingRef = "TEST001"
            };
            _dbContext.Bookings.Add(booking);
            _dbContext.SaveChanges();

            var req = new AdminUpdateBookingRequest { PartySize = 5 };

            // Act — capacity-exceeded throws BusinessRuleException (admin-edit path → 400
            // via GlobalExceptionHandler), NOT ConflictException (which is the create path → 409).
            BusinessRuleException ex = await Assert.ThrowsAsync<BusinessRuleException>(
                () => _adminController.AdminUpdateBooking(booking.Id, req));

            // Assert
            Assert.Contains("has capacity 2", ex.Message);
        }

        [Fact]
        public async Task AdminUpdateBooking_Succeeds_WhenPartySizeEqualsResourceCapacity()
        {
            // Arrange
            var venue = new Venue { Name = "Test Venue", Address = "123 Test St" };
            _dbContext.Venues.Add(venue);
            _dbContext.SaveChanges();

            var section = new Section { Name = "Main", VenueId = venue.Id };
            _dbContext.Sections.Add(section);
            _dbContext.SaveChanges();

            var resource = new Resource { Name = "Resource 1", Capacity = 4, SectionId = section.Id };
            _dbContext.Resources.Add(resource);
            _dbContext.SaveChanges();

            var booking = new Booking
            {
                VenueId = venue.Id,
                SectionId = section.Id,
                ResourceId = resource.Id,
                Date = DateTime.UtcNow.AddDays(1),
                CustomerEmail = "guest@test.com",
                PartySize = 2,
                BookingRef = "TEST002"
            };
            _dbContext.Bookings.Add(booking);
            _dbContext.SaveChanges();

            var req = new AdminUpdateBookingRequest { PartySize = 4 };

            // Act
            IActionResult result = await _adminController.AdminUpdateBooking(booking.Id, req);

            // Assert
            Assert.IsType<OkObjectResult>(result);
            var okResult = (OkObjectResult)result;
            BookingDetailDto returnedDto = Assert.IsType<BookingDetailDto>(okResult.Value);
            Assert.Equal(4, returnedDto.PartySize);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
            _serviceProvider.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
