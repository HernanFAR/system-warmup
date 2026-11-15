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

## 14. Actualización de la aplicación (evolución posterior a v0.1 inicial)

**Origen del requisito:** solicitud expresa de Hernán de disponer de actualización desde la UI, con configuración persistida en JSON, inspirada en VSlices Tooling. **Mecanismo elegido por el asistente:** GitHub Releases con descarga explícita, SHA-256, preparación y script externo de reemplazo.

| Concepto | Representación y operación | Límite |
| --- | --- | --- |
| Configuración de actualizaciones | `UpdateOptions`: `Repository`, `Channel`, `CheckOnLaunch` dentro de `Settings` | Se admite solo el formato GitHub `owner/repo` |
| Publicación candidata | `AvailableUpdate`: tag/version, descripción, URL y SHA-256 | Requiere release público, archivo ZIP exacto y versión posterior |
| Consulta | `UpdateService.CheckAsync` | La consulta no instala ni descarga ejecutables |
| Preparación | `UpdateService.PrepareAsync`: descargar, comprobar digest y extraer | El paquete puede contener código ejecutable del repositorio confiado |
| Instalación | `UpdateScript`: espera al cierre, respaldo, reemplazo, reapertura | Recuperación ante fallos ordinarios, no transacción atómica absoluta |
| UI | `UpdateWindow` + botón en `MainWindow` | La persona debe confirmar la instalación |
| Distribución | Workflow de tags `v*` publica `SystemWarmup-win-x64.zip` | No equivale a artefactos de CI ni garantiza que ya exista una release instalable |

**Flujo conceptual:** configuración → descubrimiento de release → confirmación humana → descarga y comprobación SHA-256 → preparación → cierre de la aplicación → reemplazo con respaldo → reabrir y observar resultado.

**Inspiración vs ejecución:** VSlices Tooling utiliza operaciones explícitas de actualización y, para determinadas fuentes instaladas, separación entre preparación, validación y reemplazo con recuperación. System Warmup aplica un mecanismo propio para sus binarios de escritorio; no se atribuye haber ejecutado Tooling ni reutilizado sus APIs.

**Verificación pendiente:** probar una release firmada por el proveedor de hosting, los permisos de Windows, compatibilidad de versiones, fallo de descarga, fallo de copia, rollback y relanzamiento. La compilación de CI solo valida la construcción; no demuestra una actualización de extremo a extremo.

## 15. Revisión de canales y builds de desarrollo

La petición posterior cambió el contrato de actualización. Los canales son **Release** (release estable), **Preview** (pre-release) y **Build** (CI asociada al PR configurado). El canal Build no requiere crear tags ni GitHub Releases.

La configuración `UpdateOptions` incluye `Repository`, `Channel`, `PullRequest` y `CheckOnLaunch`. El mecanismo de autenticación Build reutiliza el orden observado en VSlices Tooling: `GH_TOKEN`, `GITHUB_TOKEN`, `gh auth token`; no persiste secretos. Para Build se comprueba que el PR esté abierto, se busca la ejecución reciente del workflow para el commit HEAD, se exige resultado exitoso y se selecciona el artefacto no expirado `SystemWarmup-win-x64` con SHA-256 publicado por GitHub. Falta de artefacto/credencial/CI exitosa es un resultado observable y no debe ocultarse tras un build viejo.

La identidad visible es `build-<PR>-<run_number>`. El registro de instalación conserva adicionalmente el SHA y el identificador de ejecución para trazabilidad. El indicador de la ventana principal anuncia una actualización comprobada del canal elegido; sin actualización muestra únicamente la versión instalada. El usuario confirma la instalación; el componente externo realiza copia de seguridad y reemplazo después de que cierra la UI.

**Diferencia frente al requisito original:** el canal Build y la reutilización de autenticación surgieron de aclaraciones posteriores, mientras que el origen GitHub Releases para todos los canales fue una decisión inicial del asistente corregida tras recibir esa evidencia. **Estado:** código integrado; compilación CI y prueba real de descarga/instalación deben verificarse separadamente.

## 16. Seguridad de distribución: firma de código como exploración futura

La primera ejecución distribuida mostró la advertencia de Microsoft Defender SmartScreen para un editor desconocido. Se acordó **investigar después del entregable** una firma Authenticode autofirmada para desarrollo; quedó registrada en el [issue #2](https://github.com/HernanFAR/system-warmup/issues/2).

**Estado:** pendiente, no implementado. Un certificado autofirmado exige confianza local explícita y no elimina automáticamente la reputación/advertencias de SmartScreen. La firma del ejecutable y la comprobación SHA-256 del paquete atienden problemas relacionados pero no equivalentes; no se debe confundir integridad con seguridad del comportamiento.

## 17. Incidente de arranque y corrección comprobable

**Evidencia:** Windows Event Viewer proporcionado por Hernán mostró un `XamlParseException` originado por `InvalidOperationException` al intentar escribir la propiedad `Color` de un `SolidColorBrush` WPF en estado de solo lectura, dentro de `ThemeManager.Apply`. El error ocurría en el constructor de `MainWindow` y precedía al registro de actividad propio.

**Modelo anterior:** actualizar una paleta modificando en el lugar los objetos brush ya cargados. **Límite observado:** los recursos XAML pueden estar congelados (*frozen*). **Revisión:** reemplazar cada entrada del `ResourceDictionary` por un nuevo `SolidColorBrush`, aprovechando `DynamicResource` para la actualización visual.

**Evidencia automatizada:** se incorporó `tests/ThemeSmoke`, que crea un brush congelado y verifica reemplazo y alternancia oscuro → claro → oscuro en CI Windows. La compilación y esa prueba se ejecutaron satisfactoriamente. Esto **no demuestra por sí solo** el arranque completo ni la ausencia de otros errores visuales.

**Brecha identificada:** registrar excepciones fatales desde `App.xaml.cs`, antes de crear `MainWindow`, en un medio recuperable cuando la interfaz no alcanza a mostrarse.

## 18. Segundo defecto visual: fondo y controles del tema oscuro

**Observación de Hernán:** el modo diurno se mostraba coherente, pero en el modo nocturno persistía un fondo blanco fuera de las tarjetas y selectores con texto/fondo de contraste insuficiente.

**Revisión materializada:**

- `MainWindow.xaml` declara explícitamente `Background="{DynamicResource WindowBrush}"` y `Foreground="{DynamicResource TextBrush}"`.
- Los brushes de controles de selección pasan a contrastar dentro de la paleta oscura.
- `App.xaml` introduce plantillas propias de `ComboBox` y `ComboBoxItem` para no depender exclusivamente de colores fijos de los controles nativos de Windows.

**Estado de evidencia:** código modificado en la rama del PR. Una inspección visual posterior en el equipo de Hernán sigue siendo necesaria, especialmente para desplegables, selección mediante teclado, accesibilidad y escalado. Este episodio demuestra que las pruebas de cambio de recursos no sustituyen las pruebas de presentación.

## 19. Migración a .NET 11 sin Native AOT

Hernán preguntó por .NET 11 y Native AOT. Se decidió adoptar **`net11.0-windows`**, manteniendo WPF. El SDK y la arquitectura UI actuales no admiten compilar toda la aplicación WPF como Native AOT; `PublishAot` permanece en `false`.

Se alinearon `SystemWarmup.csproj`, `tests/ThemeSmoke` y los workflows de GitHub Actions con .NET SDK 11. La estrategia inicialmente adoptada fue `self-contained` para Windows x64, de manera que la distribución no dependa de que el usuario instale el runtime de .NET 11.

**No confundir:** `self-contained` incorpora el runtime administrado; no produce por sí mismo código nativo AOT.

## 20. Publicación single-file y revisión de CI

A solicitud posterior de Hernán, se configuraron los workflows de Build y Release para publicar con `PublishSingleFile=true`, `--self-contained true`, inclusión/extracción de dependencias nativas y compresión del bundle. El entregable distribuido debe contener **un solo archivo `SystemWarmup.exe`** (aunque .NET/WPF pueda extraer bibliotecas internas en ejecución).

Se modificó el actualizador para aceptar paquetes que ya no contienen `SystemWarmup.dll` ni `SystemWarmup.runtimeconfig.json` como archivos sueltos. El mecanismo sigue utilizando ZIP de GitHub Actions o GitHub Releases como **transporte**, aunque el contenido útil sea un solo ejecutable. La actualización continúa siendo un reemplazo externo con respaldo de la carpeta dedicada.

**Error de integración detectado por CI:** declarar `SelfContained=true` en el proyecto principal impedía que la prueba `ThemeSmoke` —ejecutable no self-contained— referenciara el proyecto, con `NETSDK1151`. La revisión fue **aplicar las propiedades de single-file/self-contained al publicar**, no a las compilaciones ordinarias.

**Evidencia posterior:** [ejecución Windows CI 43766464077](https://github.com/HernanFAR/system-warmup/actions/runs/43766464077) completó build, prueba WPF, publicación, comprobación de **exactamente un archivo `SystemWarmup.exe`** y subida de artefacto. Se informó un artefacto comprimido de aproximadamente 61,7 MB.

**Pendiente:** ejecutar en el equipo real el nuevo binario, validar alternancia visual y verificar una actualización completa desde una instalación multifichero hacia single-file, incluido rollback. Single-file **no equivale a Native AOT** ni garantiza mejor RAM o inicio; requiere mediciones.

## 21. Evidencia y límites de la evolución

| Etapa | Evidencia | Conclusión permitida |
| --- | --- | --- |
| Primer panel y configuración | Captura de Hernán | La interfaz llegó a ejecutarse; no prueba todas las operaciones |
| Fallo fatal de temas | Event Viewer | Defecto preciso en modificación de brush congelado |
| Corrección de recursos | CI y `ThemeSmoke` | El caso de brush congelado quedó cubierto; no asegura toda la UI |
| Segundo problema de contraste | Capturas en Windows | El fondo y ciertos selectores no seguían el tema correctamente |
| Corrección del contraste | Cambios XAML/C# | Corrección propuesta/implementada; verificación visual pendiente |
| Migración a .NET 11 | Build/publish en CI | Compilación y distribución posibles sin AOT |
| Primera configuración single-file | Fallo `NETSDK1151` | Las propiedades de despliegue afectaban indebidamente al proyecto de pruebas |
| Single-file corregido | CI 43766464077 | Un solo `.exe` en salida publicada y pruebas verdes |
| Actualización de instalación en Windows | Sin prueba de extremo a extremo | No declarar verificada todavía |
| Firma Authenticode | Issue #2 | Exploración futura, no funcionalidad entregada |

Este documento conserva el **modelo anterior → evidencia o tensión → revisión → estado posterior** sin transformar todas las hipótesis de ingeniería en requisitos originarios del usuario.
