# Documentación archivada (histórica)

Estos documentos describen fases anteriores del proyecto (SQLite local, SqlTest, PostgreSQL auxiliar, dual-run SQLite/HANA) y **no representan la arquitectura operativa actual**.

Arquitectura actual (HANA-first):

- `Database:Provider=Hana`
- `Source:Mode=SapHana`
- Cola e ingesta sobre tablas SAP HANA (`printer_source_print_job`, `printer_print_job`, etc.)
- DDL controlado externamente; `Database:ApplyMigrations=false` por defecto

Consulte `README.md` y `docs/RESUMEN-PROYECTO.md` para la configuración vigente.
