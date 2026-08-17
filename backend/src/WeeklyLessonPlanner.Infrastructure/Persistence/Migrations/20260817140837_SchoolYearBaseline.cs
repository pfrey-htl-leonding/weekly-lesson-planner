using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace WeeklyLessonPlanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SchoolYearBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_config",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    VisibleWeekdaysMask = table.Column<int>(type: "integer", nullable: false),
                    HolidayColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EventColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ExamColor = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_config", x => x.Id);
                    table.CheckConstraint("ck_app_config_singleton", "\"Id\" = 1");
                    table.CheckConstraint("ck_app_config_weekdays", "\"VisibleWeekdaysMask\" BETWEEN 1 AND 127");
                });

            migrationBuilder.CreateTable(
                name: "database_metadata",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    RecordedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_database_metadata", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "school_years",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PlanningStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PlanningEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_school_years", x => x.Id);
                    table.CheckConstraint("ck_school_year_range", "\"PlanningStart\" <= \"PlanningEnd\"");
                });

            migrationBuilder.CreateTable(
                name: "courses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_courses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_courses_school_years_SchoolYearId",
                        column: x => x.SchoolYearId,
                        principalTable: "school_years",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "global_day_markers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Label = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_global_day_markers", x => x.Id);
                    table.CheckConstraint("ck_global_marker_type", "\"Type\" IN (1, 2)");
                    table.ForeignKey(
                        name: "FK_global_day_markers_school_years_SchoolYearId",
                        column: x => x.SchoolYearId,
                        principalTable: "school_years",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "course_exams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_exams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_course_exams_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "course_weekdays",
                columns: table => new
                {
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Weekday = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_course_weekdays", x => new { x.CourseId, x.Weekday });
                    table.CheckConstraint("ck_course_weekday_value", "\"Weekday\" BETWEEN 1 AND 7");
                    table.ForeignKey(
                        name: "FK_course_weekdays_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "topics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Heading = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topics", x => x.Id);
                    table.UniqueConstraint("AK_topics_Id_CourseId", x => new { x.Id, x.CourseId });
                    table.ForeignKey(
                        name: "FK_topics_courses_CourseId",
                        column: x => x.CourseId,
                        principalTable: "courses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "topic_instances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topic_instances", x => x.Id);
                    table.UniqueConstraint("AK_topic_instances_Id_CourseId", x => new { x.Id, x.CourseId });
                    table.ForeignKey(
                        name: "FK_topic_instances_topics_TopicId_CourseId",
                        columns: x => new { x.TopicId, x.CourseId },
                        principalTable: "topics",
                        principalColumns: new[] { "Id", "CourseId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "topic_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicInstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    CourseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topic_assignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_topic_assignments_topic_instances_TopicInstanceId_CourseId",
                        columns: x => new { x.TopicInstanceId, x.CourseId },
                        principalTable: "topic_instances",
                        principalColumns: new[] { "Id", "CourseId" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "app_config",
                columns: new[] { "Id", "EventColor", "ExamColor", "HolidayColor", "VisibleWeekdaysMask" },
                values: new object[] { 1, "#1565c0", "#ed6c02", "#2e7d32", 31 });

            migrationBuilder.InsertData(
                table: "school_years",
                columns: new[] { "Id", "Name", "PlanningEnd", "PlanningStart" },
                values: new object[] { new Guid("6f708a97-c4e2-4a72-9652-aaf16f983d3f"), "2026/27", new DateOnly(2027, 6, 30), new DateOnly(2026, 9, 1) });

            migrationBuilder.CreateIndex(
                name: "IX_course_exams_CourseId_Date",
                table: "course_exams",
                columns: new[] { "CourseId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_course_exams_Date",
                table: "course_exams",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_courses_SchoolYearId_Name",
                table: "courses",
                columns: new[] { "SchoolYearId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_database_metadata_Key",
                table: "database_metadata",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_global_day_markers_SchoolYearId_Date",
                table: "global_day_markers",
                columns: new[] { "SchoolYearId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_school_years_Name",
                table: "school_years",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_topic_assignments_CourseId_Date",
                table: "topic_assignments",
                columns: new[] { "CourseId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_topic_assignments_TopicInstanceId",
                table: "topic_assignments",
                column: "TopicInstanceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_topic_assignments_TopicInstanceId_CourseId",
                table: "topic_assignments",
                columns: new[] { "TopicInstanceId", "CourseId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_topic_instances_TopicId_CourseId",
                table: "topic_instances",
                columns: new[] { "TopicId", "CourseId" });

            migrationBuilder.CreateIndex(
                name: "IX_topics_CourseId_Heading",
                table: "topics",
                columns: new[] { "CourseId", "Heading" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_config");

            migrationBuilder.DropTable(
                name: "course_exams");

            migrationBuilder.DropTable(
                name: "course_weekdays");

            migrationBuilder.DropTable(
                name: "database_metadata");

            migrationBuilder.DropTable(
                name: "global_day_markers");

            migrationBuilder.DropTable(
                name: "topic_assignments");

            migrationBuilder.DropTable(
                name: "topic_instances");

            migrationBuilder.DropTable(
                name: "topics");

            migrationBuilder.DropTable(
                name: "courses");

            migrationBuilder.DropTable(
                name: "school_years");
        }
    }
}
