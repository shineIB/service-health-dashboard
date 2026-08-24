using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrdersService.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxFailedState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_PublishedAtUtc",
                table: "OutboxMessages");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FailedAtUtc",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedAtUtc_FailedAtUtc",
                table: "OutboxMessages",
                columns: new[] { "PublishedAtUtc", "FailedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_PublishedAtUtc_FailedAtUtc",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "FailedAtUtc",
                table: "OutboxMessages");

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_PublishedAtUtc",
                table: "OutboxMessages",
                column: "PublishedAtUtc");
        }
    }
}
