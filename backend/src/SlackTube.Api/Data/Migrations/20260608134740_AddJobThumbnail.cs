using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SlackTube.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJobThumbnail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThumbnailMimeType",
                table: "upload_jobs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "upload_jobs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbnailMimeType",
                table: "upload_jobs");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "upload_jobs");
        }
    }
}
