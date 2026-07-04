using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EsheChatService.Migrations
{
    /// <inheritdoc />
    public partial class AddProviderToAiUsageTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Provider",
                table: "AiUsages",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Provider",
                table: "AiUsages");
        }
    }
}
