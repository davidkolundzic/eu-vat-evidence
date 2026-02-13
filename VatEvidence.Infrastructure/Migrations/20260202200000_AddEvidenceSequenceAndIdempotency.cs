using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VatEvidence.Infrastructure.Migrations
{
    public partial class AddEvidenceSequenceAndIdempotency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "sequence",
                table: "evidence_records",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Backfill sequence for existing rows (deterministic by captured_utc, id)
            migrationBuilder.Sql(@"
WITH ordered AS (
  SELECT
    id,
    transaction_id,
    ROW_NUMBER() OVER (PARTITION BY transaction_id ORDER BY captured_utc, id) AS seq
  FROM evidence_records
)
UPDATE evidence_records er
SET sequence = o.seq
FROM ordered o
WHERE er.id = o.id;
");

            migrationBuilder.CreateIndex(
                name: "ux_evidence_records_tx_sequence",
                table: "evidence_records",
                columns: new[] { "transaction_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_evidence_records_tx_type_source",
                table: "evidence_records",
                columns: new[] { "transaction_id", "evidence_type", "source_ref" },
                unique: true);

            // Enforce append-only at DB level (no UPDATE/DELETE)
            migrationBuilder.Sql(@"
CREATE OR REPLACE FUNCTION evidence_records_append_only()
RETURNS trigger
LANGUAGE plpgsql
AS $$
BEGIN
  RAISE EXCEPTION 'evidence_records is append-only';
END;
$$;

DROP TRIGGER IF EXISTS tr_evidence_records_append_only ON evidence_records;
CREATE TRIGGER tr_evidence_records_append_only
BEFORE UPDATE OR DELETE ON evidence_records
FOR EACH ROW
EXECUTE FUNCTION evidence_records_append_only();
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DROP TRIGGER IF EXISTS tr_evidence_records_append_only ON evidence_records;
DROP FUNCTION IF EXISTS evidence_records_append_only();
");
            migrationBuilder.DropIndex(
                name: "ux_evidence_records_tx_sequence",
                table: "evidence_records");

            migrationBuilder.DropIndex(
                name: "ux_evidence_records_tx_type_source",
                table: "evidence_records");

            migrationBuilder.DropColumn(
                name: "sequence",
                table: "evidence_records");
        }
    }
}
