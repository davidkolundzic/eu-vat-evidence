using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VatEvidence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRestrictDeleteOnEvidenceRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_evidence_records_transactions_transaction_id",
                table: "evidence_records");

            migrationBuilder.AddForeignKey(
                name: "fk_evidence_records_transactions_transaction_id",
                table: "evidence_records",
                column: "transaction_id",
                principalTable: "transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_evidence_records_transactions_transaction_id",
                table: "evidence_records");

            migrationBuilder.AddForeignKey(
                name: "fk_evidence_records_transactions_transaction_id",
                table: "evidence_records",
                column: "transaction_id",
                principalTable: "transactions",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
