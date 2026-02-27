# Planning: Interfaz Web y Sistema de Login

Documento de planificación para la implementación del panel operativo y autenticación.

---

## 1. Contexto y restricciones

| Aspecto | Requisito (OpenSpec) | Implicación |
|---------|----------------------|-------------|
| Entorno | Intranet corporativa Windows | AD disponible, Negotiate viable |
| Roles | Admin (global) / Supervisor (por StoreId) | Autorización por rol y ámbito |
| Stack actual | .NET 8, Blazor Server (template), API REST | Mantener coherencia |
| Base de datos | SQLite (dev) / SQL Server (prod) | Usuarios/roles pueden ir en BD o AD |

---

## 2. Decisiones de arquitectura

### 2.1 ¿Panel integrado o SPA separada?

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│  OPCIÓN A: Blazor Server (actual)                                                │
│  Web + API en mismo host o hosts distintos. Blazor llama API vía HttpClient.     │
│  Ventajas: C#, mismo ecosistema, SignalR para actualizaciones en tiempo real.    │
│  Desventajas: Conexión persistente por usuario, escalado vertical.              │
├─────────────────────────────────────────────────────────────────────────────────┤
│  OPCIÓN B: SPA (React/Vue) + API                                                 │
│  Frontend separado, consume API REST.                                            │
│  Ventajas: Desacoplamiento, ecosistema JS rico, despliegue independiente.       │
│  Desventajas: Dos stacks, CORS, gestión de tokens/sesiones.                      │
└─────────────────────────────────────────────────────────────────────────────────┘
```

**Recomendación:** Mantener **Blazor Server** (Opción A) porque:
- Ya existe el proyecto `ImpresorasService.Web`
- El design especifica "Panel Blazor Server"
- Intranet con pocos usuarios concurrentes
- Menos complejidad operativa (un solo despliegue .NET)

---

### 2.2 ¿API compartida o Web con backend propio?

| Enfoque | Descripción | Pros | Contras |
|---------|-------------|------|---------|
| **Web llama a API** | Web → HTTP → ImpresorasService.Api | API única, Swagger para integraciones | CORS si distintos orígenes |
| **Web + API mismo host** | Web y API en un solo proceso | Sin CORS, sesión compartida | Acoplamiento de despliegue |

**Recomendación:** **Web llama a API** (hosts pueden ser distintos). En desarrollo: API en 5105, Web en 5106. En producción: mismo dominio con reverse proxy (ej. `/api` → API, `/` → Web).

---

## 3. Sistema de Login

### 3.1 Opciones de autenticación

| Opción | Descripción | Entorno ideal | Complejidad |
|--------|-------------|---------------|-------------|
| **Windows/Negotiate (AD)** | SSO con cuenta de dominio | Intranet con AD | Media |
| **Forms + BD** | Usuario/contraseña en BD | Sin AD, dev local | Baja |
| **JWT + API login** | Token tras login, API stateless | SPA, móvil | Media-Alta |
| **Híbrido** | Negotiate en prod, Forms en dev | Flexibilidad | Media |

### 3.2 Recomendación por fase

| Fase | Autenticación | Justificación |
|------|---------------|---------------|
| **Fase 1 (MVP)** | Cookie Auth + usuario en BD o config | Rápido, sin depender de AD |
| **Fase 2 (Prod)** | Windows Negotiate (AD) | Cumple spec, SSO en intranet |

### 3.3 Modelo de usuarios y roles

```
┌─────────────────────────────────────────────────────────────────────────────────┐
│  Tabla: Users (o usar solo AD)                                                   │
│  - UserId, Login, DisplayName, StoreId? (para Supervisor), Role (Admin/Supervisor)│
├─────────────────────────────────────────────────────────────────────────────────┤
│  Roles:                                                                            │
│  - Admin:   StoreId = null, ve y actúa en todas las tiendas                       │
│  - Supervisor: StoreId = X, solo ve y actúa en tienda X                           │
└─────────────────────────────────────────────────────────────────────────────────┘
```

Para **Negotiate**: el login viene de AD; el rol/StoreId se resuelve por:
- Grupo de AD (ej. `Impresoras-Admin`, `Impresoras-Supervisor-Tienda1`)
- O tabla de mapeo `AdUser → Role, StoreId`

---

## 4. Stack tecnológico propuesto

### 4.1 Frontend (Blazor Server)

| Categoría | Opción | Versión | Uso |
|-----------|--------|---------|-----|
| Framework | Blazor Server | .NET 8 | Ya en uso |
| UI/CSS | MudBlazor o Radzen | Última | Componentes, formularios, tablas |
| Alternativa ligera | Bootstrap 5 + HTMX | — | Si se prefiere menos dependencias |
| Iconos | Bootstrap Icons / Lucide | — | Iconografía |
| Gráficos (opcional) | Chart.js via Blazor wrapper | — | Dashboard KPIs |

### 4.2 Comparativa de librerías UI

| Librería | Pros | Contras | Tamaño |
|----------|------|---------|--------|
| **MudBlazor** | Rica, Material Design, bien documentada | Peso, estilo Material | ~500KB |
| **Radzen Blazor** | Profesional, DataGrid potente | Licencia Pro para componentes avanzados | Medio |
| **Bootstrap 5** | Ligero, conocido | Menos componentes listos | Bajo |
| **Fluent UI Blazor** | Estilo Microsoft | Menos madura que Mud/Radzen | Medio |

**Recomendación:** **MudBlazor** para MVP (gratuita, completa, buena DX).

### 4.3 Autenticación

| Componente | Paquete | Uso |
|------------|---------|-----|
| Cookie Auth | `Microsoft.AspNetCore.Authentication.Cookies` | Sesión en Blazor |
| Windows Auth | `Microsoft.AspNetCore.Authentication.Negotiate` | AD en producción |
| Identity (opcional) | `Microsoft.AspNetCore.Identity` | Si usuarios en BD |

---

## 5. Plan de implementación por fases

### Fase 0: Preparación (1–2 días)

| Tarea | Descripción |
|-------|-------------|
| 0.1 | Crear `docs/PLANNING-UI-Y-LOGIN.md` (este doc) |
| 0.2 | Añadir MudBlazor a `ImpresorasService.Web` |
| 0.3 | Configurar Web para llamar a API (HttpClient, BaseAddress) |
| 0.4 | Definir estructura de carpetas (Pages, Components, Services) |

### Fase 1: Login básico (2–3 días)

| Tarea | Descripción |
|-------|-------------|
| 1.1 | Crear endpoint `POST /api/auth/login` (usuario/contraseña) o usar Identity |
| 1.2 | Crear tabla `Users` (UserId, Login, PasswordHash, Role, StoreId) si no hay AD |
| 1.3 | Configurar Cookie Authentication en Web |
| 1.4 | Página Login (`/login`) con formulario |
| 1.5 | Layout: mostrar usuario, botón Logout |
| 1.6 | Proteger rutas: `[Authorize]` en páginas |
| 1.7 | Redirect a `/login` si no autenticado |

### Fase 2: Layout y navegación (1 día)

| Tarea | Descripción |
|-------|-------------|
| 2.1 | Layout principal con MudBlazor (AppBar, Drawer) |
| 2.2 | Menú: Dashboard, Cola, Impresoras, Reglas, Prueba, Alertas |
| 2.3 | Filtro de tienda en header (para Supervisor) |
| 2.4 | Página 404 / Error |

### Fase 3: Pantallas core (3–4 días)

| Tarea | Descripción |
|-------|-------------|
| 3.1 | **Cola**: tabla con filtros, acciones Enrutar/Cancelar/Reintentar |
| 3.2 | **Detalle job**: historial de eventos, acciones |
| 3.3 | **Impresoras**: CRUD con MudDataGrid o tabla |
| 3.4 | **Reglas**: CRUD + simulador de resolución |
| 3.5 | **Crear prueba**: formulario que llama a `/api/sourceprintjobs/test` |

### Fase 4: Dashboard y alertas (1–2 días)

| Tarea | Descripción |
|-------|-------------|
| 4.1 | Dashboard: KPIs por estado (cards) |
| 4.2 | Lista de alertas (jobs ErrorFinal) |
| 4.3 | Auto-refresh opcional (Timer, cada 30s) |

### Fase 5: Autorización por rol (1–2 días)

| Tarea | Descripción |
|-------|-------------|
| 5.1 | Policy `Admin` / `Supervisor` |
| 5.2 | Ocultar menú según rol |
| 5.3 | Filtro automático por StoreId para Supervisor |
| 5.4 | Validar en API: rechazar acciones fuera de ámbito |

### Fase 6: Windows Auth (producción) (1–2 días)

| Tarea | Descripción |
|-------|-------------|
| 6.1 | Configurar `AddAuthentication(Negotiate)` en API y/o Web |
| 6.2 | Mapeo AD → Role/StoreId (grupos o tabla) |
| 6.3 | Modo híbrido: Negotiate si está, si no Cookie para dev |

---

## 6. Estructura de carpetas propuesta

```
ImpresorasService.Web/
├── Components/
│   ├── Layout/
│   │   ├── MainLayout.razor
│   │   ├── NavMenu.razor
│   │   └── AppBar.razor
│   ├── Pages/
│   │   ├── Login.razor
│   │   ├── Dashboard.razor
│   │   ├── Cola/
│   │   │   ├── Cola.razor
│   │   │   └── JobDetail.razor
│   │   ├── Impresoras/
│   │   │   └── Impresoras.razor
│   │   ├── Reglas/
│   │   │   └── Reglas.razor
│   │   ├── Prueba/
│   │   │   └── CrearPrueba.razor
│   │   └── Alertas/
│   │       └── Alertas.razor
│   └── Shared/
│       ├── JobStatusBadge.razor
│       └── ConfirmDialog.razor
├── Services/
│   ├── ApiClient.cs          # HttpClient wrapper para API
│   ├── AuthService.cs        # Login, logout, usuario actual
│   └── AuthorizationPolicies.cs
├── wwwroot/
│   └── css/
│       └── app.css
└── Program.cs
```

---

## 7. Configuración de desarrollo

### appsettings.Development.json (Web)

```json
{
  "ApiBaseUrl": "https://localhost:5105",
  "Auth": {
    "Mode": "Cookie",
    "CookieName": "Impresoras.Auth"
  }
}
```

### appsettings.json (Producción)

```json
{
  "ApiBaseUrl": "https://impresoras.empresa.local/api",
  "Auth": {
    "Mode": "Negotiate"
  }
}
```

---

## 8. Resumen de dependencias a añadir

### ImpresorasService.Web.csproj

```xml
<PackageReference Include="MudBlazor" Version="7.x" />
```

### ImpresorasService.Api (para login)

```xml
<!-- Si usamos Identity -->
<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="8.x" />

<!-- Si usamos Negotiate -->
<PackageReference Include="Microsoft.AspNetCore.Authentication.Negotiate" Version="8.x" />
```

---

## 9. Orden de ejecución recomendado

```
1. Fase 0 (preparación)
2. Fase 1 (login básico)     ← Bloqueante para el resto
3. Fase 2 (layout)
4. Fase 3 (pantallas core)   ← Valor principal
5. Fase 4 (dashboard)
6. Fase 5 (autorización)
7. Fase 6 (Windows Auth)     ← Cuando haya entorno AD
```

---

## 10. Decisiones pendientes

| Decisión | Opciones | Recomendación |
|----------|----------|---------------|
| Librería UI | MudBlazor / Radzen / Bootstrap | MudBlazor |
| Login MVP | Cookie+BD / JWT / Negotiate desde día 1 | Cookie + tabla Users |
| ¿Web y API mismo host? | Sí / No | No (más flexible) |
| ¿Identity completo? | Sí / No (auth manual) | No para MVP; auth manual más simple |

---

## 11. Próximos pasos inmediatos

1. Confirmar: MudBlazor vs alternativa
2. Confirmar: Login con tabla Users vs Identity
3. Crear tareas en `tasks.md` del OpenSpec
4. Iniciar Fase 0
