namespace ImpresorasService.Infrastructure;

/// <summary>
/// Carga el primer fichero .env que encuentra subiendo desde la carpeta del ejecutable
/// (en desarrollo, la raíz del repo; instalado, C:\ImpresorasService\.env) como variables de entorno.
/// Nunca pisa una variable ya definida: las del servicio o del sistema mandan sobre el .env.
/// Formato: CLAVE=valor por línea, # para comentarios. Las claves siguen el patrón de .NET
/// (ConnectionStrings__PrintQueue) o las de libpq que Npgsql lee solo (PGPASSWORD).
/// </summary>
public static class DotEnv
{
    public static void Load()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, ".env");
            if (!File.Exists(path))
                continue;

            foreach (var line in File.ReadAllLines(path))
            {
                var trimmed = line.Trim();
                var eq = trimmed.IndexOf('=');
                if (trimmed.StartsWith('#') || eq <= 0)
                    continue;

                var key = trimmed[..eq].Trim();
                if (Environment.GetEnvironmentVariable(key) is null)
                    Environment.SetEnvironmentVariable(key, trimmed[(eq + 1)..].Trim());
            }
            return;
        }
    }
}
