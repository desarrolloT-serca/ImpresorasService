// Aplica migraciones de esta carpeta con la conexión del .env de la raíz (ConnectionStrings__PrintQueue
// y PGPASSWORD), cada una en su transacción y con el search_path de la cadena. Para equipos sin psql.
// Uso, desde ImpresorasServiceV1:
//   node scripts/sql/migraciones/aplicar.mjs <migracion.sql> [<migracion.sql> ...]
// Necesita el paquete pg: se toma del ERP de este equipo si no está instalado aquí.
import { readFileSync } from "node:fs"
import { createRequire } from "node:module"

const require = createRequire(import.meta.url)
const { Client } = (() => {
  try { return require("pg") }
  catch { return require("C:/Users/TANO CANOVAS/Documents/Dev/ERP_SERCA/erp/node_modules/pg") }
})()

const env = Object.fromEntries(readFileSync(".env", "utf8").split(/\r?\n/)
  .filter((l) => /^\w+=/.test(l))
  .map((l) => [l.slice(0, l.indexOf("=")), l.slice(l.indexOf("=") + 1).trim()]))
const cs = Object.fromEntries(env.ConnectionStrings__PrintQueue.split(";").filter(Boolean)
  .map((p) => { const i = p.indexOf("="); return [p.slice(0, i).trim().toLowerCase(), p.slice(i + 1).trim()] }))

const c = new Client({ host: cs.host, port: +cs.port, database: cs.database, user: cs.username, password: env.PGPASSWORD, connectionTimeoutMillis: 8000 })
await c.connect()
for (const fichero of process.argv.slice(2)) {
  try {
    await c.query("begin")
    await c.query(`set local search_path to ${cs["search path"]}`)
    await c.query(readFileSync(fichero, "utf8"))
    await c.query("commit")
    console.log("Aplicada:", fichero)
  } catch (e) {
    await c.query("rollback")
    console.error("ERROR (deshecha):", fichero, e.message)
    process.exitCode = 1
    break
  }
}
const r = await c.query(`select table_name, column_name, is_nullable from information_schema.columns
  where table_schema = $1 and ((table_name = 'printer_printer' and column_name = 'spool_queue')
    or (table_name = 'printer_print_job' and column_name = 'ipp_job_id'))`, [cs["search path"]])
console.table(r.rows)
await c.end()
