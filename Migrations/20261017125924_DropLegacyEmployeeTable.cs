using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BIS.ERP.Migrations
{
    /// <inheritdoc />
    public partial class DropLegacyEmployeeTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Идемпотентно: в базах, где таблицы Postings/Employee ещё не было
            // (например, мастер-база), миграция не должна падать.
            migrationBuilder.Sql(@"ALTER TABLE IF EXISTS ""Postings"" DROP CONSTRAINT IF EXISTS ""FK_Postings_Employee_EmployeeId"";");
            migrationBuilder.Sql(@"DROP INDEX IF EXISTS ""IX_Postings_EmployeeId"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""Employee"";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Employee",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Address = table.Column<string>(type: "text", nullable: false),
                    BirthDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Department = table.Column<string>(type: "text", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Email = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "text", nullable: false),
                    Gender = table.Column<string>(type: "text", nullable: false),
                    HireDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MaritalStatus = table.Column<string>(type: "text", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: false),
                    PassportIssueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PassportIssuedBy = table.Column<string>(type: "text", nullable: false),
                    PassportNumber = table.Column<string>(type: "text", nullable: false),
                    PersonnelNumber = table.Column<string>(type: "text", nullable: false),
                    Phone = table.Column<string>(type: "text", nullable: false),
                    Position = table.Column<string>(type: "text", nullable: false),
                    PositionDisplay = table.Column<string>(type: "text", nullable: false),
                    PositionId = table.Column<Guid>(type: "uuid", nullable: true),
                    PositionText = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    TaxId = table.Column<string>(type: "text", nullable: false),
                    TerminationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employee", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Postings_EmployeeId",
                table: "Postings",
                column: "EmployeeId");

            migrationBuilder.AddForeignKey(
                name: "FK_Postings_Employee_EmployeeId",
                table: "Postings",
                column: "EmployeeId",
                principalTable: "Employee",
                principalColumn: "Id");
        }
    }
}
