using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SlackTube.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelMappingsAndJobAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GoogleAccountId",
                table: "upload_jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "channel_mappings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SlackWorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlackChannelId = table.Column<string>(type: "text", nullable: false),
                    SlackChannelName = table.Column<string>(type: "text", nullable: false),
                    GoogleAccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_channel_mappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_channel_mappings_google_accounts_GoogleAccountId",
                        column: x => x.GoogleAccountId,
                        principalTable: "google_accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_channel_mappings_slack_workspaces_SlackWorkspaceId",
                        column: x => x.SlackWorkspaceId,
                        principalTable: "slack_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_upload_jobs_GoogleAccountId",
                table: "upload_jobs",
                column: "GoogleAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_channel_mappings_GoogleAccountId",
                table: "channel_mappings",
                column: "GoogleAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_channel_mappings_SlackChannelId",
                table: "channel_mappings",
                column: "SlackChannelId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_channel_mappings_SlackWorkspaceId",
                table: "channel_mappings",
                column: "SlackWorkspaceId");

            migrationBuilder.AddForeignKey(
                name: "FK_upload_jobs_google_accounts_GoogleAccountId",
                table: "upload_jobs",
                column: "GoogleAccountId",
                principalTable: "google_accounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // Data migration: turn the old single listening channel + the (migrated) first Google
            // account into one mapping, resolving the workspace + name from synced slack_channels.
            // No-op unless a listening channel, its workspace channel row, and an account all exist.
            migrationBuilder.Sql(@"
                INSERT INTO channel_mappings (""Id"", ""SlackWorkspaceId"", ""SlackChannelId"", ""SlackChannelName"", ""GoogleAccountId"", ""CreatedAt"")
                SELECT gen_random_uuid(), sc.""WorkspaceId"", s.""ListeningChannelId"", sc.""Name"",
                       (SELECT ""Id"" FROM google_accounts ORDER BY ""CreatedAt"" LIMIT 1), now()
                FROM app_settings s
                JOIN slack_channels sc ON sc.""SlackChannelId"" = s.""ListeningChannelId""
                WHERE s.""ListeningChannelId"" IS NOT NULL
                  AND EXISTS (SELECT 1 FROM google_accounts)
                ON CONFLICT (""SlackChannelId"") DO NOTHING;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_upload_jobs_google_accounts_GoogleAccountId",
                table: "upload_jobs");

            migrationBuilder.DropTable(
                name: "channel_mappings");

            migrationBuilder.DropIndex(
                name: "IX_upload_jobs_GoogleAccountId",
                table: "upload_jobs");

            migrationBuilder.DropColumn(
                name: "GoogleAccountId",
                table: "upload_jobs");
        }
    }
}
