-- Migracion: token del bot de Telegram puesto desde la pantalla (Alertas por Telegram). Si esta,
-- manda sobre Telegram__BotToken del servicio; null = el del servicio. La API nunca lo devuelve.
-- Antes de desplegar la version que lo usa; la anterior no lee la columna:
--   node scripts/sql/migraciones/aplicar.mjs scripts/sql/migraciones/2026-10-27-telegram-bot-token.sql
-- Marcha atras: ALTER TABLE printer_telegram_config DROP COLUMN bot_token;
ALTER TABLE printer_telegram_config ADD COLUMN IF NOT EXISTS bot_token varchar(100);
