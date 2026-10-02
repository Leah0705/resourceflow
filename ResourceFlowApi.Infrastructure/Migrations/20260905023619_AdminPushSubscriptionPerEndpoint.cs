using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceFlowApi.Migrations
{
    /// <inheritdoc />
    public partial class AdminPushSubscriptionPerEndpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing installs hold one row per (endpoint, venue) — the settings card
            // registered the browser against every location — so collapsing the scope leaves
            // duplicate endpoints that the new unique index would reject. Keep the oldest row
            // per endpoint; the surviving keys are identical, every row for an endpoint having
            // been written by the same subscribe call.
            migrationBuilder.Sql(
                """
                DELETE FROM AdminPushSubscriptions
                WHERE Id NOT IN (SELECT MIN(Id) FROM AdminPushSubscriptions GROUP BY Endpoint);
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_AdminPushSubscriptions_Venues_VenueId",
                table: "AdminPushSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_AdminPushSubscriptions_Endpoint_VenueId",
                table: "AdminPushSubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_AdminPushSubscriptions_VenueId",
                table: "AdminPushSubscriptions");

            migrationBuilder.DropColumn(
                name: "VenueId",
                table: "AdminPushSubscriptions");

            migrationBuilder.CreateIndex(
                name: "IX_AdminPushSubscriptions_Endpoint",
                table: "AdminPushSubscriptions",
                column: "Endpoint",
                unique: true);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Lossy by nature: there is no venue to restore a subscription to, and a row
        /// reinstated at <c>VenueId = 0</c> would be an orphan under the FK this puts
        /// back. The table is emptied instead, so a downgraded install asks its admins to
        /// re-subscribe rather than carrying rows the fan-out can never match.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminPushSubscriptions_Endpoint",
                table: "AdminPushSubscriptions");

            migrationBuilder.Sql("DELETE FROM AdminPushSubscriptions;");

            migrationBuilder.AddColumn<int>(
                name: "VenueId",
                table: "AdminPushSubscriptions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_AdminPushSubscriptions_Endpoint_VenueId",
                table: "AdminPushSubscriptions",
                columns: new[] { "Endpoint", "VenueId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdminPushSubscriptions_VenueId",
                table: "AdminPushSubscriptions",
                column: "VenueId");

            migrationBuilder.AddForeignKey(
                name: "FK_AdminPushSubscriptions_Venues_VenueId",
                table: "AdminPushSubscriptions",
                column: "VenueId",
                principalTable: "Venues",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
