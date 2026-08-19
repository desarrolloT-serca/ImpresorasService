-- Invalidacion inmediata de acceso (Fase 5 de docs/roadmapimpresoras.md).
--
-- Hasta ahora, borrar o desactivar un usuario no le quitaba el acceso: su JWT seguia siendo valido
-- hasta 8 horas. Estas dos columnas se comprueban en CADA peticion autenticada.
--
-- is_active     FALSE deja de servir en la siguiente peticion.
-- token_version se incrementa al cambiar la contrasena; el token lleva el valor con el que se
--               emitio y deja de validar en cuanto no coincide.
--
-- Ambas con DEFAULT para que las filas existentes queden activas y en version 0, que es lo que
-- emitiran los tokens nuevos: no invalida las sesiones abiertas al aplicar el DDL.
-- DEFAULT TRUE, no DEFAULT 1: HANA rechaza el 1 para una columna BOOLEAN con
-- "[336] invalid default value". El catalogo devuelve luego default=[1] porque asi lo almacena,
-- lo que despista al leerlo; a la hora de declararlo va el literal TRUE.
-- Ejecutar UNA SENTENCIA POR VEZ: los clientes JDBC dan [257] si se pegan las dos juntas.
-- Sustituir <SCHEMA> por el esquema HANA del entorno antes de ejecutar.
ALTER TABLE "<SCHEMA>"."printer_user" ADD ("is_active" BOOLEAN DEFAULT TRUE NOT NULL);
ALTER TABLE "<SCHEMA>"."printer_user" ADD ("token_version" INTEGER DEFAULT 0 NOT NULL);
