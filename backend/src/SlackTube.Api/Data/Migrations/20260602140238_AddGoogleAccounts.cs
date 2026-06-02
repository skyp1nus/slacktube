using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SlackTube.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "google_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    YouTubeChannelId = table.Column<string>(type: "text", nullable: true),
                    YouTubeChannelTitle = table.Column<string>(type: "text", nullable: true),
                    AccountEmail = table.Column<string>(type: "text", nullable: true),
                    EncryptedRefreshToken = table.Column<string>(type: "text", nullable: false),
                    Scopes = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_google_accounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_google_accounts_CreatedAt",
                table: "google_accounts",
                column: "CreatedAt");

            // Data migration: carry the existing single google_tokens row over to one google_account
            // so current uploads keep working. No-op on a fresh install (no rows to copy).
            migrationBuilder.Sql(@"
                INSERT INTO google_accounts (""Id"", ""Label"", ""EncryptedRefreshToken"", ""Scopes"", ""Status"", ""CreatedAt"", ""UpdatedAt"")
                SELECT gen_random_uuid(), 'Migrated account', ""EncryptedRefreshToken"", ""Scopes"", 'Active', ""CreatedAt"", ""UpdatedAt""
                FROM google_tokens
                WHERE ""EncryptedRefreshToken"" IS NOT NULL AND ""EncryptedRefreshToken"" <> '';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "google_accounts");
        }
    }
}
