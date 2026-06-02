using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SlackTube.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    SlackBotTokenEncrypted = table.Column<string>(type: "text", nullable: true),
                    SlackSigningSecretEncrypted = table.Column<string>(type: "text", nullable: true),
                    ListeningChannelId = table.Column<string>(type: "text", nullable: true),
                    StatusMessageTs = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_settings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "google_tokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EncryptedRefreshToken = table.Column<string>(type: "text", nullable: false),
                    Scopes = table.Column<string>(type: "text", nullable: false),
                    AccountEmail = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_google_tokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "upload_jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SlackEventId = table.Column<string>(type: "text", nullable: false),
                    SlackChannelId = table.Column<string>(type: "text", nullable: false),
                    SlackUserId = table.Column<string>(type: "text", nullable: false),
                    SlackMessageTs = table.Column<string>(type: "text", nullable: false),
                    DriveFileId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Tags = table.Column<string>(type: "jsonb", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    RequiresConfirmation = table.Column<bool>(type: "boolean", nullable: false),
                    Confirmed = table.Column<bool>(type: "boolean", nullable: true),
                    BytesTotal = table.Column<long>(type: "bigint", nullable: false),
                    BytesTransferred = table.Column<long>(type: "bigint", nullable: false),
                    OriginalFileName = table.Column<string>(type: "text", nullable: true),
                    YouTubeVideoId = table.Column<string>(type: "text", nullable: true),
                    YouTubeUrl = table.Column<string>(type: "text", nullable: true),
                    QuotaUnitsCharged = table.Column<int>(type: "integer", nullable: false),
                    HangfireJobId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upload_jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "job_state_history",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    FromState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    ToState = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Note = table.Column<string>(type: "text", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_state_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_job_state_history_upload_jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "upload_jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_job_state_history_JobId",
                table: "job_state_history",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_upload_jobs_CreatedAt",
                table: "upload_jobs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_upload_jobs_SlackEventId",
                table: "upload_jobs",
                column: "SlackEventId");

            migrationBuilder.CreateIndex(
                name: "IX_upload_jobs_State",
                table: "upload_jobs",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "google_tokens");

            migrationBuilder.DropTable(
                name: "job_state_history");

            migrationBuilder.DropTable(
                name: "upload_jobs");
        }
    }
}
