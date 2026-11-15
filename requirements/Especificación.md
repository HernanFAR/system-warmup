# Especificación — System Warmup

> **Tipo:** reconstrucción técnica y funcional de los conceptos y decisiones introducidos por el asistente durante la primera realización de System Warmup.
> **Fecha de reconstrucción:** 15 de noviembre de 2026.
> **Estado:** descriptivo y revisable; distingue comportamiento implementado, intención de diseño y capacidades pendientes de verificar.
> **Autoría/procedencia:** las necesidades de la persona usuaria constan en [Narrativa](Narrativa.md); las decisiones concretas de diseño descritas aquí proceden de la implementación asistida por IA y del código del repositorio.
> **No equivale** a una especificación VSlices canónica ni a una prueba de que el producto satisface todos los requisitos.

## 1. Problema que realiza el sistema

System Warmup organiza decisiones sobre qué **servicios de Windows** deben permanecer activos o detenerse para un contexto de trabajo. Complementariamente, puede solicitar el cierre de ciertas ventanas de aplicaciones, apagar las distribuciones WSL y lanzar Docker Desktop.

La primera realización fue implementada como **aplicación WPF para Windows sobre .NET 8**, sin framework UI externo.

La implementación actual no administra completamente todas las aplicaciones de inicio del sistema y no es un servicio residente de monitoreo.

## 2. Conceptos y sus responsabilidades

| Concepto | Significado en el sistema | Realización actual |
| --- | --- | --- |
| **Servicio descubierto** | Servicio consultado a Windows, con nombre, descripción, estado y tipo de inicio observados | `ServiceItem`; inventario vía `Get-CimInstance Win32_Service` |
| **Estado observado** | Estado reportado por el sistema al consultar | `State` (texto, p. ej. `Running` / `Stopped`) |
| **Tipo de inicio** | Configuración informativa de arranque del servicio | `StartMode`; solo lectura |
| **Estado deseado** | Política asignada a un servicio dentro de un perfil | `DesiredState`: `Ignore`, `KeepRunning`, `Stop` |
| **Perfil** | Conjunto de políticas y acciones auxiliares para un contexto de trabajo | `Profile`, identificado por `Name`, con descripción, diccionario de servicios, WSL, Docker y procesos opcionales |
| **Servicio protegido** | Servicio cuyo cambio bloquea el controlador por precaución | Conjunto explícito `SystemOperations.Protected`; no es una prueba general de dependencias |
| **Captura de estado** | Lista de servicios encontrados activos en un momento determinado | `BaselineRunningServices` y `BaselineCapturedAt` |
| **Preferencias locales** | Perfil seleccionado, tema y aplicación automática en inicio de sesión | `Settings` en JSON |
| **Plan de cambios** | Representación previa de las acciones propuestas | `DescribePlan()` |
| **Aplicación de perfil** | Solicitudes de iniciar o detener servicios, cierre de ventanas y operaciones WSL/Docker | `ApplyAsync()` y `SystemOperations` |
| **Registro de actividad** | Evidencia textual de acciones solicitadas y errores observados | `activity.log` |
| **Tema visual** | Paleta compartida de recursos de interfaz | `ThemeManager`: `Dark` / `Light` |

**Distinción fundamental:** un `KeepRunning` expresa el estado deseado, no una garantía de que el servicio esté funcional después de una solicitud de inicio. Similarmente, `Stop` no implica detención exitosa sin observación posterior.

## 3. Perfiles iniciales

### Benchmark

**Propósito:** reducir actividad ajena a los experimentos de inferencia.

Configuración predeterminada en `Settings.Defaults()`:

- Solicitar detención de PostgreSQL 15/16, TeamViewer, servicios Apple/Bonjour, JetBrains ETW Host, `IpOverUsbSvc`, `SQLTELEMETRY` y `SQLWriter`.
- Solicitar detención de `docker` y `com.docker.service`.
- Solicitar apagado de las distribuciones WSL.
- Solicitar cierre de ventanas de Docker Desktop, OneDrive, Phone Link/CrossDevice y componentes de Widgets/Start Feed cuando corresponda.

### Minimal Developer

**Propósito:** disponer de Docker y WSL sin activar innecesariamente los demás servicios.

Configuración predeterminada:

- Mantener o iniciar `WSLService` y `com.docker.service`.
- Solicitar apertura de Docker Desktop.
- Solicitar detención de PostgreSQL 15/16 y los demás servicios auxiliares listados en la configuración original.
- Solicitar cierre de ventanas de widgets y feed.

**Limitación:** el código no verifica que el motor Docker esté listo mediante `docker info`, ni garantiza que todos los componentes dependientes queden operativos. El nombre «Minimal Developer» define una intención del perfil, no una certificación de entorno completo.

### Perfiles personalizados

La UI permite elegir un perfil, duplicarlo, eliminarlo (siempre que quede al menos uno) y ajustar políticas por servicio. Dos opciones por perfil, `ShutdownWsl` y `LaunchDockerDesktop`, son editables.

En la primera versión, la lista `CloseProcesses` se almacena en JSON, pero **no cuenta todavía con una interfaz equivalente para editarla**.

## 4. Operaciones de la aplicación

| Operación | Comportamiento esperado según el código | Estado |
| --- | --- | --- |
| Descubrir servicios | Consultar inventario real y actualizar tabla/contador | Implementada; depende de PowerShell/Windows |
| Capturar estado | Guardar nombres de los servicios activos al momento de la captura | Implementada; no es captura garantizada del arranque original |
| Buscar servicios | Filtrar por nombre o nombre visible | Implementada |
| Configurar política | Guardar `Ignore`, `KeepRunning` o `Stop` para un servicio no protegido | Implementada |
| Cambiar perfil | Seleccionar otro conjunto de políticas | Implementada |
| Duplicar/eliminar perfil | Administrar perfiles locales | Implementada |
| Ver plan | Mostrar acciones propuestas y si corresponden a cambio respecto del estado observado | Implementada |
| Aplicar perfil manualmente | Pedir confirmación y ejecutar las acciones descritas | Implementada; resultado por acción sujeto a permisos y dependencias |
| Aplicar al iniciar sesión | Aplicar el perfil seleccionado solo si está habilitada la preferencia y se inicia con `--startup` | Implementada, comportamiento real de tarea programada pendiente de validación |
| Autoinicio | Crear/quitar tarea programada al inicio de sesión | Implementada con `schtasks.exe`; requiere permisos |
| Instancia única | Evitar segunda instancia concurrente por sesión de usuario | Mutex local |
| Cambiar tema | Alternar paleta clara/oscura y persistir selección | Implementada en rama del PR; actualización de recursos WPF |
| Cerrar aplicación | Terminar la aplicación UI | Implementado; no hay daemon residente |

Las filas de la tabla y los contadores dependen de la última consulta; no hay actualización continua.

## 5. Reglas actuales y límites

1. **La política `Ignore` no inicia ni detiene el servicio.**
2. **La política `Stop` solicita detenerlo si el estado observado no era `Stopped`.**
3. **La política `KeepRunning` solicita iniciarlo si el estado observado no era `Running`.**
4. El sistema bloquea cambios para una lista conservadora de servicios protegidos. Esa lista no sustituye una evaluación de dependencias de Windows.
5. No cambia automáticamente el tipo de inicio (`StartMode`) del servicio.
6. La aplicación exige elevación administrativa mediante manifiesto.
7. Antes de la aplicación manual se presenta una vista previa y se solicita confirmación. El inicio automático requiere habilitación previa y no solicita confirmación interactiva equivalente.
8. La ejecución del cambio no es transaccional: si una operación falla, las anteriores pueden haber tenido efecto. **No hay rollback automático.**
9. El controlador registra la respuesta de las herramientas del sistema y vuelve a consultar servicios, pero un código de salida exitoso de `sc.exe` puede representar aceptación de solicitud, no estado final alcanzado.
10. Para procesos opcionales, la aplicación solicita un cierre de ventana mediante `CloseMainWindow()`; un proceso sin ventana principal puede permanecer abierto. No hay terminación forzada general.

Estas son **características del código actual**, no invariantes formalmente demostradas mediante pruebas.

## 6. Persistencia y datos locales

Archivo: `%LOCALAPPDATA%\SystemWarmup\settings.json`.

Campos representados por `Settings`:

- `SelectedProfile`: nombre del perfil seleccionado.
- `ApplyAtLogin`: permite aplicar el perfil al abrir desde inicio automático.
- `Theme`: `Dark` o `Light`.
- `BaselineRunningServices`: nombres de servicios de la captura.
- `BaselineCapturedAt`: instante de captura.
- `Profiles`: perfiles, sus políticas y acciones auxiliares.

La aplicación escribe mediante archivo temporal y reemplazo con `File.Move(..., overwrite: true)`. Esta estrategia reduce ciertos riesgos de escritura parcial, pero no se ha demostrado recuperación ante fallos o conflictos de edición concurrente.

Si no puede deserializarse el JSON, `Settings.Load()` utiliza los valores por defecto. **No hay todavía diagnóstico visible ni mecanismo de recuperación del archivo original**.

Registro de acciones: `%LOCALAPPDATA%\SystemWarmup\activity.log`, independiente de la configuración.

## 7. Arquitectura materializada

```text
MainWindow.xaml / MainWindow.xaml.cs
    ├── ThemeManager                 (recursos visuales)
    ├── Settings / Profile           (configuración JSON)
    ├── ServiceItem                  (inventario + política editada)
    └── SystemOperations             (frontera con Windows)
            ├── powershell.exe       (descubrimiento CIM)
            ├── sc.exe               (start / stop)
            ├── wsl.exe              (shutdown)
            ├── Docker Desktop.exe   (solicitud de apertura)
            ├── Process.CloseMainWindow()
            └── schtasks.exe         (autoinicio)
```

La elección de WPF, `sc.exe`, `schtasks.exe`, el mutex y los recursos dinámicos pertenece al diseño producido por el asistente; **no es un requisito declarado por Hernán**.

Tampoco existe aún un mecanismo genérico de «capacidades» que abstraiga completamente cada integración con Windows.

## 8. Interfaz y retroalimentación

La primera versión implementó panel de servicios activos, RAM disponible, reglas del perfil, tabla filtrable, panel lateral, vista previa, acciones y log.

Después de probarla, Hernán reportó inconsistencia visual: fondo diurno con tarjetas oscuras y títulos de bajo contraste. Pidió alternar modo día/noche.

Como respuesta, se introdujeron dos paletas de colores y recursos compartidos en `ThemeManager`, botón en encabezado y persistencia de `Theme`. Esta revisión debe validarse visualmente en el equipo; que compile no prueba ausencia de problemas de contraste en todos los controles WPF.

## 9. Verificación y brechas conocidas

**Evidencia existente:** código publicado en el PR [#1](https://github.com/HernanFAR/system-warmup/pull/1), compilaciones de GitHub Actions en Windows y una captura de funcionamiento de la primera interfaz aportada por Hernán.

**No demostrados por esa evidencia:**

- Correcta detención y restauración de todos los servicios bajo distintos estados de dependencia.
- Disponibilidad real de Docker tras aplicar `Minimal Developer`.
- Comportamiento de tareas programadas en reinicios reales y permisos.
- Capacidad de ahorrar un volumen determinado de RAM por perfil.
- Cierre efectivo de todas las aplicaciones de inicio.
- Reversibilidad completa de las acciones.
- Consistencia visual definitiva del nuevo selector de tema en todos los controles.

**Tensiones relevantes para futuras pruebas:**

- «Mostrar servicios levantados por defecto» frente a «capturar el estado al abrir la aplicación» no son semánticamente lo mismo.
- «Mantener Docker disponible» frente a «solicitar iniciar servicios y lanzar Docker Desktop» requiere observación de disponibilidad real.
- «Aplicación única hasta cerrarla» no es lo mismo que un servicio residente que administra recursos permanentemente.
- El usuario mencionó Spotify, Teams, Steam y JetBrains Toolbox, pero el preset actual no los integra como lista editable en la UI.
- Una protección por nombres conocidos no garantiza que un servicio no protegido sea seguro de detener.

## 10. Perspectiva futura, no parte de v0.1

Candidatos expresados o inferidos a partir del uso:

- Descubrir y administrar aplicaciones que se inician con Windows, además de servicios.
- Verificar dependencias y operaciones efectivas, especialmente Docker/WSL.
- Mejorar confirmación, recuperación, historial y edición de políticas de procesos.
- Medir consumo real de System Warmup y de los distintos perfiles.
- Evaluar un observador de recursos en segundo plano y un planificador adaptativo.
- Investigar microoptimizaciones **después** de contar con evidencia de consumo.

Estos puntos **no deben considerarse funcionalidades entregadas**.

## 11. Trazabilidad con la Narrativa

| Necesidad registrada | Conceptos/realización | Estado de correspondencia |
| --- | --- | --- |
| Evitar pagar por preparación permanente | Perfil + políticas de servicio | Parcial: aún faltan aplicaciones de inicio |
| Cambiar entre inferencia y desarrollo mínimo | Benchmark / Minimal Developer | Implementado como intención operativa; validación de Docker pendiente |
| Centralizar configuración con UI y JSON | WPF + `Settings` | Implementado |
| Ver qué está activo al iniciar | Inventario + captura manual | Parcial: fotografía al abrir, no auditoría real de arranque |
| Ejecutarse una sola vez hasta cerrar | Tarea programada + mutex | Implementado; prueba real de autoinicio pendiente |
| Apariencia consistente de día y noche | Tema + paletas dinámicas | Implementado; verificación visual posterior pendiente |
| Futuro monitoreo de recursos | Hipótesis de observador residente | No implementado |

## 12. Valor como caso de comparación para VSlices

La especificación **reconstruye** las decisiones tomadas en el desarrollo inicial asistido por IA. No debe editarse para que parezca que esos conceptos fueron identificados de antemano o que la implementación es correcta por definición.

En una futura ejecución basada en VSlices será posible observar qué conceptos emergen, qué invariantes se formalizan, qué decisiones se preservan o se revisan y qué pruebas se vuelven reproducibles. Las diferencias deben juzgarse por evidencia, no por preferencia retrospectiva por una metodología.

## 13. Condición experimental: influencia previa de VSlices

**Aclaración de Hernán posterior a la reconstrucción:** System Warmup se desarrolló mediante *vibe coding*, pero el asistente tenía contexto histórico y conceptual de VSlices Framework y su ecosistema. La ausencia de un protocolo VSlices formal en este desarrollo **no implica ausencia de influencia de VSlices**.

Esta especificación refleja las decisiones efectivamente materializadas por el asistente, y algunas pueden compartir vocabulario o criterios con VSlices. Esa afinidad no demuestra por sí sola qué decisión fue causada por ese contexto. No se debe presentar esta realización como una línea base independiente, «sin VSlices», ni atribuirle una aplicación formal de VSlices que no está documentada.

Para futuros experimentos, distinguir al menos: **(a)** contexto previo disponible al agente, **(b)** instrucción o procedimiento explícito, **(c)** herramientas/contratos VSlices utilizados realmente, **(d)** decisiones e implementación observables, **(e)** evidencia y resultados. Esta reconstrucción conserva una **línea base contextualizada**, no un grupo de control aislado.
