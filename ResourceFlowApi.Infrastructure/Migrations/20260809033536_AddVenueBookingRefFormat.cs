using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceFlowApi.Migrations
{
    /// <inheritdoc />
    public partial class AddVenueBookingRefFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BookingRefFormat",
                table: "Venues",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BookingRefFormat",
                table: "Venues");
        }
    }
}
