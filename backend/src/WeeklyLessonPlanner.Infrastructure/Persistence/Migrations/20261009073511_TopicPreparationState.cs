using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WeeklyLessonPlanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TopicPreparationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPrepared",
                table: "topic_assignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPrepared",
                table: "topic_assignments");
        }
    }
}
