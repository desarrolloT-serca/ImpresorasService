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
-- Sustituir <SCHEMA> por el esquema HANA del entorno antes de ejecutar.
ALTER TABLE "<SCHEMA>"."printer_user" ADD ("is_active" BOOLEAN DEFAULT TRUE NOT NULL);
ALTER TABLE "<SCHEMA>"."printer_user" ADD ("token_version" INTEGER DEFAULT 0 NOT NULL);
