-- Migracion: las tiendas y los usuarios salen del ERP (ERP_SERCA docs/72, 11/11/2026).
-- printer_store: deja de ser una copia a mano de los almacenes y pasa a ser una vista sobre
--   gesserca.fdalmacen (mismos codigos; activa = sin fecha de baja). La tabla vieja se queda como
--   printer_store_old hasta comprobar que todo va bien.
-- printer_user: sin uso. Se entra solo con el pase del ERP. Se queda como printer_user_old.
-- Orden: DESPUES de desplegar la Api y el Worker que ya no escriben en printer_store ni leen printer_user
-- (rama feat/datos-del-erp). Con la version anterior, el login propio y la pantalla Tiendas fallarian.
--   node scripts/sql/migraciones/aplicar.mjs scripts/sql/migraciones/2026-11-11-tiendas-y-usuarios-del-erp.sql
-- No se puede repetir: falla en el primer RENAME si ya se aplico (sin cambiar nada, va en una transaccion).
-- Quien la aplica: el DUENO de las tablas printer_* (en la copia, pb8_xp57), que puede leer
--   gesserca.fdalmacen. La vista se consulta con los permisos de su dueno, asi que el usuario de la Api y
--   el Worker (en la copia, impresoras_service) solo necesita SELECT sobre la vista, no sobre gesserca.
--   Si el servicio usa otro usuario, cambiar el GRANT.
-- Comprobacion: SELECT count(*), count(*) FILTER (WHERE is_active) FROM printer_store;
--   debe dar los almacenes de fdalmacen (31 y 29 en la copia el 11/11/2026) y las 29 activas deben ser
--   las mismas que las de printer_store_old.
-- Marcha atras (en una transaccion):
--   DROP VIEW printer_store;
--   ALTER TABLE printer_store_old RENAME TO printer_store;
--   ALTER INDEX IF EXISTS ix_printer_store_old_name RENAME TO ix_printer_store_name;
--   ALTER TABLE printer_user_old RENAME TO printer_user;
--   y volver a la version anterior de la Api y el Worker.
-- Borrado definitivo, pasada una semana sin incidencias:
--   DROP TABLE printer_store_old; DROP TABLE printer_user_old;
-- EXCEPCION a "sin nombre de esquema": fdalmacen es del ERP y vive siempre en gesserca.

ALTER TABLE printer_store RENAME TO printer_store_old;
ALTER INDEX IF EXISTS ix_printer_store_name RENAME TO ix_printer_store_old_name;

CREATE VIEW printer_store AS
SELECT cod_alm::integer AS store_id,
       rtrim(denominacion)::varchar(120) AS name,
       fec_baja IS NULL AS is_active
FROM gesserca.fdalmacen;
GRANT SELECT ON printer_store TO impresoras_service;

ALTER TABLE printer_user RENAME TO printer_user_old;
