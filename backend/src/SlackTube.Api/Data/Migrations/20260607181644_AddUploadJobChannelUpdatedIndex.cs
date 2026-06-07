using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SlackTube.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadJobChannelUpdatedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_upload_jobs_SlackChannelId_UpdatedAt",
                table: "upload_jobs",
                columns: new[] { "SlackChannelId", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_upload_jobs_SlackChannelId_UpdatedAt",
                table: "upload_jobs");
        }
    }
}
