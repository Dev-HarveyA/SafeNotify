using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SafeNotify.Server.Data.Migrations
{
    public partial class AddAlertSpeech : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "SpeakMessage",
                table: "Alerts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SpeakMessage",
                table: "Alerts");
        }
    }
}
