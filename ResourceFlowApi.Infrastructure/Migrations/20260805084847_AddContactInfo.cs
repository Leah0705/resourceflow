using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceFlowApi.Migrations
{
    /// <inheritdoc />
    public partial class AddContactInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailAddress",
                table: "Venues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Venues",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmailAddress",
                table: "BrandSettings",
                type: "TEXT",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "BrandSettings",
                type: "TEXT",
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailAddress",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Venues");

            migrationBuilder.DropColumn(
                name: "EmailAddress",
                table: "BrandSettings");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "BrandSettings");
        }
    }
}
