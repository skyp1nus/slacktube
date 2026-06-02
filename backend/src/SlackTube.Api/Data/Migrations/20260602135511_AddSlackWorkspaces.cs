using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SlackTube.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSlackWorkspaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "slack_workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SlackTeamId = table.Column<string>(type: "text", nullable: false),
                    TeamName = table.Column<string>(type: "text", nullable: false),
                    BotTokenEncrypted = table.Column<string>(type: "text", nullable: false),
                    BotUserId = table.Column<string>(type: "text", nullable: true),
                    Scope = table.Column<string>(type: "text", nullable: true),
                    AuthedUserId = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    InstalledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slack_workspaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "slack_channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SlackChannelId = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsPrivate = table.Column<bool>(type: "boolean", nullable: false),
                    IsMember = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_slack_channels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_slack_channels_slack_workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "slack_workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_slack_channels_SlackChannelId",
                table: "slack_channels",
                column: "SlackChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_slack_channels_WorkspaceId_SlackChannelId",
                table: "slack_channels",
                columns: new[] { "WorkspaceId", "SlackChannelId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_slack_workspaces_SlackTeamId",
                table: "slack_workspaces",
                column: "SlackTeamId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "slack_channels");

            migrationBuilder.DropTable(
                name: "slack_workspaces");
        }
    }
}
