using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceFlowApi.Migrations
{
    /// <inheritdoc />
    public partial class PushSubscriptionCompositeUniqueKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminPushSubscriptions_Endpoint",
                table: "AdminPushSubscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_AdminPushSubscriptions_Endpoint_VenueId",
                table: "AdminPushSubscriptions",
                columns: new[] { "Endpoint", "VenueId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminPushSubscriptions_Endpoint_VenueId",
                table: "AdminPushSubscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_AdminPushSubscriptions_Endpoint",
                table: "AdminPushSubscriptions",
                column: "Endpoint",
                unique: true);
        }
    }
}
