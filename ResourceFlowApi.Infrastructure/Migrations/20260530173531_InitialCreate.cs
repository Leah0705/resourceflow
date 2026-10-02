using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ResourceFlowApi.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminCredentials",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordSalt = table.Column<string>(type: "TEXT", nullable: false),
                    PvqQuestion = table.Column<string>(type: "TEXT", nullable: true),
                    PvqAnswerHash = table.Column<string>(type: "TEXT", nullable: true),
                    PvqAnswerSalt = table.Column<string>(type: "TEXT", nullable: true),
                    ResetToken = table.Column<string>(type: "TEXT", nullable: true),
                    ResetTokenExpiry = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminCredentials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BrandSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppName = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PrimaryColor = table.Column<string>(type: "TEXT", nullable: false),
                    AccentColor = table.Column<string>(type: "TEXT", nullable: true),
                    HeaderImageUrl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BrandSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailFailures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BookingRef = table.Column<string>(type: "TEXT", nullable: true),
                    RecipientEmail = table.Column<string>(type: "TEXT", nullable: false),
                    ErrorMessage = table.Column<string>(type: "TEXT", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailFailures", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    EncryptedPassword = table.Column<string>(type: "TEXT", nullable: false),
                    EnableSsl = table.Column<bool>(type: "INTEGER", nullable: false),
                    FromName = table.Column<string>(type: "TEXT", nullable: true),
                    FromEmail = table.Column<string>(type: "TEXT", nullable: true),
                    SendBookingConfirmations = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Highlights",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    IconKey = table.Column<string>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Highlights", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Venues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    OpenTime = table.Column<string>(type: "TEXT", nullable: false),
                    CloseTime = table.Column<string>(type: "TEXT", nullable: false),
                    OpenDays = table.Column<string>(type: "TEXT", nullable: false),
                    Timezone = table.Column<string>(type: "TEXT", nullable: false),
                    BookingsPausedUntil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Tags = table.Column<string>(type: "TEXT", nullable: true),
                    ImageUrl = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Venues", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    VenueId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sections_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Resources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false),
                    SectionId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Resources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Resources_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Bookings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ResourceId = table.Column<int>(type: "INTEGER", nullable: false),
                    SectionId = table.Column<int>(type: "INTEGER", nullable: false),
                    VenueId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CustomerEmail = table.Column<string>(type: "TEXT", nullable: true),
                    CustomerName = table.Column<string>(type: "TEXT", nullable: true),
                    PartySize = table.Column<int>(type: "INTEGER", nullable: false),
                    SpecialRequests = table.Column<string>(type: "TEXT", nullable: true),
                    BookingRef = table.Column<string>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsCancelled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bookings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Bookings_Venues_VenueId",
                        column: x => x.VenueId,
                        principalTable: "Venues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bookings_Sections_SectionId",
                        column: x => x.SectionId,
                        principalTable: "Sections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Bookings_Resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "Resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_VenueId",
                table: "Bookings",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_SectionId",
                table: "Bookings",
                column: "SectionId");

            migrationBuilder.CreateIndex(
                name: "IX_Bookings_ResourceId",
                table: "Bookings",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_Sections_VenueId",
                table: "Sections",
                column: "VenueId");

            migrationBuilder.CreateIndex(
                name: "IX_Resources_SectionId",
                table: "Resources",
                column: "SectionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminCredentials");

            migrationBuilder.DropTable(
                name: "Bookings");

            migrationBuilder.DropTable(
                name: "BrandSettings");

            migrationBuilder.DropTable(
                name: "EmailFailures");

            migrationBuilder.DropTable(
                name: "EmailSettings");

            migrationBuilder.DropTable(
                name: "Highlights");

            migrationBuilder.DropTable(
                name: "Resources");

            migrationBuilder.DropTable(
                name: "Sections");

            migrationBuilder.DropTable(
                name: "Venues");
        }
    }
}
