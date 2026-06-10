## Purpose

Definir los requisitos del frontend Laravel que sustituye a Blazor para el panel de operación del servicio de impresión centralizado.

## ADDED Requirements

### Requirement: Autenticación por sesión PHP con token API
El sistema SHALL permitir login mediante formulario que valida contra la API .NET y SHALL mantener la sesión del usuario sin exponer el token al navegador.

#### Scenario: Login exitoso
- **WHEN** el usuario envía credenciales válidas en el formulario de login
- **THEN** Laravel SHALL llamar a la API para validar
- **AND** la API SHALL devolver un token (JWT) y datos del usuario
- **AND** Laravel SHALL guardar token y usuario en sesión
- **AND** el usuario SHALL ser redirigido al dashboard

#### Scenario: Login fallido
- **WHEN** las credenciales son inválidas
- **THEN** el sistema SHALL mostrar mensaje de error
- **AND** el usuario SHALL permanecer en la página de login

#### Scenario: Llamadas a la API autenticadas
- **WHEN** Laravel realiza una petición a la API
- **THEN** SHALL incluir el header `Authorization: Bearer {token}` con el token de sesión
- **AND** si la API devuelve 401, Laravel SHALL redirigir al login y limpiar la sesión

### Requirement: Layout con Metronic y paleta SERCA
El sistema SHALL usar el layout Metronic con la paleta corporativa SERCA y el logo ad SERCA.

#### Scenario: Paleta de colores
- **WHEN** se renderiza cualquier página del panel
- **THEN** el sistema SHALL aplicar primary `#1a237e`, accent `#c62828` y neutral `#78909c`
- **AND** la fuente SHALL ser Poppins

#### Scenario: Logo en sidebar
- **WHEN** el usuario está autenticado
- **THEN** el sidebar SHALL mostrar el logo desde `assets/logo.png`
- **AND** SHALL mostrar el texto "Impresoras Service" junto al logo

### Requirement: Tema claro y oscuro
El sistema SHALL permitir alternar entre tema claro y oscuro con persistencia entre sesiones.

#### Scenario: Toggle de tema
- **WHEN** el usuario activa el toggle de tema
- **THEN** el sistema SHALL cambiar entre tema claro y oscuro
- **AND** la preferencia SHALL guardarse en `localStorage` con clave `theme`

#### Scenario: Carga inicial con preferencia guardada
- **WHEN** el usuario carga la aplicación
- **THEN** el sistema SHALL leer `localStorage.theme`
- **AND** SHALL aplicar el tema correspondiente (light o dark)

### Requirement: Pantallas operativas
El sistema SHALL incluir las pantallas necesarias para la operación del servicio de impresión.

#### Scenario: Login
- **WHEN** el usuario no autenticado accede a cualquier ruta protegida
- **THEN** SHALL ser redirigido a `/login`

#### Scenario: Dashboard
- **WHEN** el usuario autenticado accede a `/`
- **THEN** SHALL ver el dashboard con KPIs por estado

#### Scenario: Cola
- **WHEN** el usuario accede a la cola
- **THEN** SHALL ver la tabla de trabajos con filtros por tienda, estado, impresora
- **AND** SHALL poder realizar acciones: reintentar, cancelar

#### Scenario: Impresoras y Reglas
- **WHEN** el usuario Admin accede a Impresoras o Reglas
- **THEN** SHALL ver el CRUD correspondiente
- **AND** el Supervisor SHALL tener acceso restringido según su tienda

#### Scenario: Alertas
- **WHEN** existen trabajos en ErrorFinal
- **THEN** SHALL mostrarse en la sección de alertas
- **AND** el Admin y Supervisor de la tienda SHALL ver las alertas relevantes

### Requirement: Autorización por rol
El sistema SHALL restringir el acceso según el rol del usuario (Admin, Supervisor).

#### Scenario: Admin global
- **WHEN** el usuario tiene rol Admin
- **THEN** SHALL acceder a todas las tiendas y funciones
- **AND** el menú SHALL mostrar todas las opciones

#### Scenario: Supervisor por tienda
- **WHEN** el usuario tiene rol Supervisor
- **THEN** SHALL ver solo datos de su tienda (`StoreId`)
- **AND** el menú SHALL mostrar el filtro de tienda (o estar fijado a su tienda)
