## 1. UI Foundation and Tokens

- [x] 1.1 Definir tokens visuales base (color, tipografia, espaciado, radios, sombras) para tema claro y oscuro en los estilos globales.
- [x] 1.2 Reestructurar `resources/views/layouts/app.blade.php` para un layout consistente (sidebar, topbar, contenedor principal) alineado con el template.
- [x] 1.3 Normalizar componentes compartidos (botones, inputs, selects, tablas, badges, alerts) con clases reutilizables.
- [x] 1.4 Eliminar estilos inline presentes en layout y moverlos a estilos centralizados.

## 2. Migracion de Pantallas Core

- [x] 2.1 Migrar `resources/views/dashboard.blade.php` como pantalla faro del nuevo estilo visual.
- [x] 2.2 Migrar `resources/views/cola.blade.php` al nuevo sistema de layout y componentes.
- [x] 2.3 Migrar `resources/views/impresoras/index.blade.php` y mantener intactas las acciones funcionales existentes.
- [x] 2.4 Migrar `resources/views/reglas/index.blade.php` y mantener intactos filtros y acciones.
- [x] 2.5 Migrar `resources/views/auth/login.blade.php` para coherencia visual con el resto del sistema.

## 3. Calidad Visual y Validacion

- [x] 3.1 Verificar consistencia de dark mode en todas las pantallas core modernizadas.
- [x] 3.2 Validar contraste y legibilidad de tablas, formularios, alertas y estados vacios en flujos principales.
- [ ] 3.3 Ejecutar smoke funcional de login, navegacion, filtros y acciones para asegurar cero regresion de comportamiento.
- [x] 3.4 Documentar brevemente en `README` o `docs` las convenciones UI para futuras pantallas.
