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
    public class AdminControllerRestoreTests : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly AppDbContext _dbContext;
        private readonly AdminController _adminController;

        public AdminControllerRestoreTests()
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
            var venue = new Venue { Name = "Test Venue", Address = "123 Test St" };
            _dbContext.Venues.Add(venue);

            var section = new Section { Name = "Main Section", VenueId = venue.Id };
            _dbContext.Sections.Add(section);

            var resource = new Resource { Name = "Resource 1", Capacity = 4, SectionId = section.Id };
            _dbContext.Resources.Add(resource);

            // Active booking
            var activeBooking = new Booking
            {
                VenueId = venue.Id,
                SectionId = section.Id,
                ResourceId = resource.Id,
                Date = DateTime.UtcNow.AddHours(2),
                CustomerEmail = "active@test.com",
                PartySize = 2,
                IsCancelled = false,
                BookingRef = "ACTIVE001"
            };
            _dbContext.Bookings.Add(activeBooking);

            // Cancelled booking
            var cancelledBooking = new Booking
            {
                VenueId = venue.Id,
                SectionId = section.Id,
                ResourceId = resource.Id,
                Date = DateTime.UtcNow.AddHours(4),
                CustomerEmail = "cancelled@test.com",
                PartySize = 3,
                IsCancelled = true,
                CancelledAt = DateTime.UtcNow.AddHours(-1),
                BookingRef = "CANCELLED001"
            };
            _dbContext.Bookings.Add(cancelledBooking);

            _dbContext.SaveChanges();
        }

        [Fact]
        public async Task RestoreBooking_WithValidCancelledBooking_ReturnsSuccess()
        {
            // Arrange
            Booking cancelledBooking = await _dbContext.Bookings
                .FirstAsync(b => b.BookingRef == "CANCELLED001");

            // Act
            IActionResult result = await _adminController.RestoreBooking(cancelledBooking.Id);

            // Assert
            OkObjectResult okResult = Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(okResult.Value);

            // Verify booking is restored in database
            Booking? restoredBooking = await _dbContext.Bookings.FindAsync(cancelledBooking.Id);
            Assert.NotNull(restoredBooking);
            Assert.False(restoredBooking.IsCancelled);
            Assert.Null(restoredBooking.CancelledAt);
        }

        [Fact]
        public async Task RestoreBooking_WithNonExistentBooking_ReturnsNotFound()
        {
            // Act
            IActionResult result = await _adminController.RestoreBooking(99999);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task RestoreBooking_WithActiveBooking_ThrowsBusinessRuleException()
        {
            // Post-Bundle-6 the controller propagates the typed exception; the 400 status
            // is applied by GlobalExceptionHandler (covered by GlobalExceptionHandlerTests).
            Booking activeBooking = await _dbContext.Bookings
                .FirstAsync(b => b.BookingRef == "ACTIVE001");

            BusinessRuleException ex = await Assert.ThrowsAsync<BusinessRuleException>(
                () => _adminController.RestoreBooking(activeBooking.Id));
            Assert.Equal("Booking is already active.", ex.Message);
        }

        [Fact]
        public async Task RestoreBooking_MultipleRestores_OnlyWorksOnce()
        {
            // Arrange
            Booking cancelledBooking = await _dbContext.Bookings
                .FirstAsync(b => b.BookingRef == "CANCELLED001");

            // Act - First restore
            IActionResult firstResult = await _adminController.RestoreBooking(cancelledBooking.Id);

            // Act - Second restore attempt now throws BusinessRuleException (booking already active)
            BusinessRuleException ex = await Assert.ThrowsAsync<BusinessRuleException>(
                () => _adminController.RestoreBooking(cancelledBooking.Id));

            // Assert
            Assert.IsType<OkObjectResult>(firstResult);
            Assert.Equal("Booking is already active.", ex.Message);

            // Verify booking is still active
            Booking? booking = await _dbContext.Bookings.FindAsync(cancelledBooking.Id);
            Assert.NotNull(booking);
            Assert.False(booking.IsCancelled);
            Assert.Null(booking.CancelledAt);
        }

        [Fact]
        public async Task RestoreBooking_VerifyResponseMessage()
        {
            // Arrange
            Booking cancelledBooking = await _dbContext.Bookings
                .FirstAsync(b => b.BookingRef == "CANCELLED001");

            // Act
            IActionResult result = await _adminController.RestoreBooking(cancelledBooking.Id);

            // Assert
            OkObjectResult okResult = Assert.IsType<OkObjectResult>(result);
            MessageResponse response = Assert.IsType<MessageResponse>(okResult.Value);
            Assert.Equal("Booking restored successfully.", response.Message);
        }

        public void Dispose()
        {
            _dbContext.Dispose();
            _serviceProvider.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
