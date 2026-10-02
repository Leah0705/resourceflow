using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceFlowApi.Migrations
{
    /// <inheritdoc />
    public partial class AddResourceGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ResourceGroupId",
                table: "Bookings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ResourceGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    VenueId = table.Column<int>(type: "INTEGER", nullable: false),
                    CombinedCapacity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ResourceGroups_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ResourceGroupMemberships",
                columns: table => new
                {
                    ResourceGroupId = table.Column<int>(type: "INTEGER", nullable: false),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceGroupMemberships", x => new { x.ResourceGroupId, x.ResourceId });
                    table.ForeignKey(
                        name: "FK_ResourceGroupMemberships_ResourceGroups_ResourceGroupId",
                        column: x => x.ResourceGroupId,
                        principalTable: "ResourceGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ResourceGroupMemberships_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ResourceGroupId",
                table: "Bookings",
                column: "ResourceGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceGroupMemberships_ResourceId",
                table: "ResourceGroupMemberships",
                column: "ResourceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ResourceGroups_VenueId",
                table: "ResourceGroups",
                column: "VenueId");

            migrationBuilder.AddForeignKey(
                name: "FK_Bookings_ResourceGroups_ResourceGroupId",
                table: "Bookings",
                column: "ResourceGroupId",
                principalTable: "ResourceGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Bookings_ResourceGroups_ResourceGroupId",
                table: "Bookings");

            migrationBuilder.DropTable(
                name: "ResourceGroupMemberships");

            migrationBuilder.DropTable(
                name: "ResourceGroups");

            migrationBuilder.DropIndex(
                name: "IX_Bookings_ResourceGroupId",
                table: "Bookings");

            migrationBuilder.DropColumn(
                name: "ResourceGroupId",
                table: "Bookings");
        }
    }
}
