using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SlackTube.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleOAuthClients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OAuthClientId",
                table: "google_accounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "google_oauth_clients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "text", nullable: false),
                    ClientId = table.Column<string>(type: "text", nullable: false),
                    EncryptedClientSecret = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_google_oauth_clients", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_google_accounts_OAuthClientId",
                table: "google_accounts",
                column: "OAuthClientId");

            migrationBuilder.CreateIndex(
                name: "IX_google_accounts_YouTubeChannelId",
                table: "google_accounts",
                column: "YouTubeChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_google_oauth_clients_ClientId",
                table: "google_oauth_clients",
                column: "ClientId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_google_accounts_google_oauth_clients_OAuthClientId",
                table: "google_accounts",
                column: "OAuthClientId",
                principalTable: "google_oauth_clients",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_google_accounts_google_oauth_clients_OAuthClientId",
                table: "google_accounts");

            migrationBuilder.DropTable(
                name: "google_oauth_clients");

            migrationBuilder.DropIndex(
                name: "IX_google_accounts_OAuthClientId",
                table: "google_accounts");

            migrationBuilder.DropIndex(
                name: "IX_google_accounts_YouTubeChannelId",
                table: "google_accounts");

            migrationBuilder.DropColumn(
                name: "OAuthClientId",
                table: "google_accounts");
        }
    }
}
