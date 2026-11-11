#!/usr/bin/env bash
# Instala o actualiza ImpresorasService en Linux (systemd), solo por IP. Equivale a
# install-windows-services.ps1: compila desde este checkout, copia a /opt/impresoras y (re)arranca
# impresoras-api e impresoras-worker. No toca la base de datos.
#
#   sudo ./scripts/install-linux.sh
#
# La primera vez crea /etc/impresoras/impresoras.env desde deploy/linux/impresoras.env.example, con
# Erp__Secret generado, y se para: hay que rellenar la conexión y volver a lanzarlo.
# Después lo respeta (no regenera claves ni pisa lo que se haya cambiado a mano).
# Necesita el SDK de .NET 8 para compilar; los servicios no necesitan el runtime (self-contained).
# ponytail: compila como root en este checkout, así que bin/ y obj/ quedan de root (git pull no se
# entera); si alguien compila después con su usuario: sudo rm -rf src/*/bin src/*/obj.
set -euo pipefail

[ "$(id -u)" -eq 0 ] || { echo "Ejecútalo con sudo." >&2; exit 1; }
# Con sudo, el PATH es el de root: un SDK instalado solo para un usuario (~/.dotnet) no se ve.
command -v dotnet >/dev/null || { echo "Falta el SDK de .NET 8 (dotnet), o sudo no lo ve: instálalo para todo el sistema." >&2; exit 1; }

repo="$(cd "$(dirname "$0")/.." && pwd)"
destino=/opt/impresoras
conf=/etc/impresoras/impresoras.env

id impresoras >/dev/null 2>&1 || useradd --system --no-create-home --shell /usr/sbin/nologin impresoras

if [ ! -f "$conf" ]; then
    install -d -m 0750 -o root -g impresoras /etc/impresoras
    install -m 0640 -o root -g impresoras "$repo/deploy/linux/impresoras.env.example" "$conf"
    # 64 caracteres. Erp__Secret va también en IMPRESION_SECRETO del ERP.
    sed -i "s/^Erp__Secret=GENERAR$/Erp__Secret=$(openssl rand -hex 32)/" "$conf"
    echo "Creado $conf con la clave generada. Rellena ConnectionStrings__PrintQueue y vuelve a lanzar el script."
    exit 1
fi
if grep -q RELLENAR "$conf"; then
    echo "Falta rellenar ConnectionStrings__PrintQueue en $conf." >&2
    exit 1
fi

systemctl stop impresoras-worker impresoras-api 2>/dev/null || true

for s in api worker; do
    proyecto="ImpresorasService.$([ $s = api ] && echo Api || echo Worker)"
    echo "-- Publicando $proyecto --"
    rm -rf "${destino:?}/$s"
    dotnet publish "$repo/src/$proyecto" -c Release -r linux-x64 --self-contained true -o "$destino/$s"
    install -m 0644 "$repo/deploy/linux/impresoras-$s.service" /etc/systemd/system/
done

systemctl daemon-reload
systemctl enable --now impresoras-api impresoras-worker

echo "Listo. Comprueba: curl http://127.0.0.1:5105/health  y  journalctl -u impresoras-worker -f"
echo "Clave para IMPRESION_SECRETO del ERP: la línea Erp__Secret de $conf (no la copies por chat ni correo)."
