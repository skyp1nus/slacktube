using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SlackTube.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadStartedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UploadStartedAt",
                table: "upload_jobs",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UploadStartedAt",
                table: "upload_jobs");
        }
    }
}
