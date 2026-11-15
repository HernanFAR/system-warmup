# System Warmup

**Lightweight Windows desktop profile manager** — experimental WPF/.NET 8 application for reducing background activity on a low-end development laptop.

## Capabilities (v0.1)

- Attractive dark-mode dashboard, RAM available, running services and active profile summary.
- Enumerate actual Windows services with their name, state, start mode and a **captured baseline** of running services. A captured baseline describes that moment, not guaranteed factory/default boot behavior.
- Search all services and assign **Ignore**, **KeepRunning** or **Stop** per profile. Profiles are stored locally in `%LOCALAPPDATA%\SystemWarmup\settings.json`; edit rules in UI.
- Two presets: **Benchmark** (stop Docker/PostgreSQL/optional third parties, shutdown WSL) and **Minimal Developer** (start WSL service and Docker Desktop prerequisites, stop optional third-party services).
- Duplicate/delete profiles; choose whether a profile shuts down WSL or launches Docker Desktop.
- Preview changes and require confirmation for manual application. All service changes are issued using Windows `sc.exe`; no service startup type is modified. Protected Windows services are read-only.
- Activity log in `%LOCALAPPDATA%\SystemWarmup\activity.log`.
- **Single instance** per Windows user session, with a Mutex. App closes normally; no background polling/daemon yet.
- Optional Windows logon task via Task Scheduler (`/RL HIGHEST`). Applying profile at logon is **OFF by default** and must be explicitly enabled in UI. Task only starts the app unless opted in.
- Process close requests are graceful (`CloseMainWindow()`) and may not close every background process; the tool intentionally does **not** force kill processes or uninstall/disable services.

## Requirements

Windows 10/11, .NET 8 Desktop Runtime (or the .NET 8 SDK for development), administrator permissions for changes and task registration.

No NuGet dependencies. WPF native controls (good fit for resource-constrained machines). Windows-only.

## Run locally

```powershell
git clone https://github.com/HernanFAR/system-warmup
cd system-warmup
dotnet run --project .\SystemWarmup\SystemWarmup.csproj
```

Approve UAC elevation. Application intentionally requires administrator privilege for service controls. For a normal deployment, publish to a **stable directory**, then start its EXE:

```powershell
dotnet publish .\SystemWarmup\SystemWarmup.csproj -c Release -r win-x64 --self-contained false -o "$env:LOCALAPPDATA\Programs\SystemWarmup"
& "$env:LOCALAPPDATA\Programs\SystemWarmup\SystemWarmup.exe"
```

A GitHub Actions Windows workflow builds the application and uploads a portable artifact for each branch/PR run; build workflow result should be verified before unattended use.

## First run procedure

1. Open the app and let Windows service discovery finish.
2. Inspect the **captured baseline** (or capture again after login before applying any profile).
3. Select Benchmark or Minimal Developer and inspect actions via **Ver plan de cambios**.
4. Adjust individual service policies in the table. Protected services remain untouched regardless of profile.
5. Press **Aplicar este perfil** and confirm.
6. Only after verifying the executable from its final location, optionally install autostart. The app does not change services on boot unless **Aplicar perfil al iniciar Windows** is checked.

## Safety and current limitations

- The intended machine has an Intel i5-8265U, UHD 620 and 16 GB RAM. Dashboard refresh is **manual**; no background CPU sampling, process watchdog, resource throttling or adaptive service management.
- The initial service list is based on a previous, user-provided Windows snapshot; no running-state guesses are silently treated as facts on a new boot.
- Minimal Developer starts `WSLService` and `com.docker.service` if available, then launches Docker Desktop. Exact backend readiness must still be verified via `docker info`; launch is not proof of a working daemon.
- Benchmark shuts down WSL using `wsl --shutdown`, which interrupts active distributions and workloads. Save work before applying.
- The app requests process window closure but does not force-close background/headless apps. Use the app's log to see what was actually requested.
- No realtime resource monitoring or always-on Windows Service is implemented yet. Single-instance + task-on-logon is the current design.
- **Development state:** CI compilation and actual WPF/Windows behavior have not been verified in this environment; review PR and Windows build artifacts before considering this stable.

## Next experiments

Add verifiable service dependency inspection, editable process policies, per-action confirmation, Docker readiness checks, performance sampling and a resource-sensitive scheduler. Measure RAM/CPU before microoptimizing the shell.

## Actualizaciones desde la aplicación (experimental)

La ventana **Actualizaciones** se abre desde la barra superior. Puedes configurar el repositorio GitHub (por defecto `HernanFAR/system-warmup`), el canal `Stable` o `Prerelease` y la comprobación al abrir la aplicación. Todo se guarda en el mismo JSON local: `%LOCALAPPDATA%\SystemWarmup\settings.json`, dentro de `Updates`. **Nunca se instala ni se descarga una versión al comprobarla automáticamente.**

1. `Buscar actualización` consulta las releases públicas del repositorio configurado.
2. Una versión candidata debe ser más nueva que la versión instalada y publicar `SystemWarmup-win-x64.zip` con `digest: sha256:...` proporcionado por GitHub.
3. `Descargar e instalar` requiere aprobación explícita. Se descarga a `%LOCALAPPDATA%\SystemWarmup\updates`, se valida SHA-256 y se extrae a un directorio preparado.
4. Tras la verificación, un script PowerShell **fuera del directorio de instalación** espera a que System Warmup cierre, conserva la instalación anterior en un directorio hermano `*.previous-<fecha>`, copia el paquete nuevo e inicia el programa actualizado. Ante un error de copia o arranque inmediato intenta restaurar la instalación anterior.
5. Los respaldos anteriores **no se eliminan automáticamente**. Revisar el archivo `apply.log` dentro de la carpeta de preparación para diagnosticar fallos. El mecanismo no garantiza recuperación ante cortes de energía, bloqueos de archivos por antivirus o fallas graves del sistema.

**Requisitos y límites:** ejecutar desde una instalación publicada, no desde `dotnet run` ni `bin/`; conservar el directorio instalado completo y los archivos `.dll`/runtime. Un repositorio distinto puede distribuir código ejecutable distinto: configura solamente orígenes de confianza. Por seguridad la descarga exige SHA-256 de los metadatos del release y un ZIP con estructura reconocible. La preferencia de canal `Prerelease` admite tanto previews como versiones estables; `Stable` omite previews. Las releases no son equivalentes a artefactos temporales de CI.

El workflow `.github/workflows/release.yml` genera el ZIP al publicar tags `v*` y crea la GitHub Release correspondiente. La primera versión liberada por ese flujo requerirá un tag apropiado después de revisar/mergear el PR. **No se ha ejecutado todavía una actualización real en la máquina del usuario.** Esta implementación es una adaptación independiente de la idea de actualización explícita de VSlices Tooling; no incorpora ni depende de su código.
