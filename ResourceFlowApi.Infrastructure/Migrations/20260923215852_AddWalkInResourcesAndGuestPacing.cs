using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceFlowApi.Migrations
{
    /// <inheritdoc />
    public partial class AddWalkInResourcesAndGuestPacing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WalkInOnly",
                table: "Resources",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "MaxGuestsPerSlot",
                table: "Venues",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WalkInOnly",
                table: "Resources");

            migrationBuilder.DropColumn(
                name: "MaxGuestsPerSlot",
                table: "Venues");
        }
    }
}
