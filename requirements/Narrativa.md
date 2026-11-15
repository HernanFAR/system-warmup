# Narrativa — System Warmup

> **Tipo:** levantamiento narrativo del problema y de la intención humana.
> **Procedencia:** solicitudes y observaciones de Hernán durante la conversación de desarrollo, 15 de noviembre de 2026.
> **Estado:** registro retrospectivo de la intención expresada; no es una especificación técnica ni una certificación de la implementación.
> **Caso de estudio:** desarrollo inicialmente asistido por IA y exploratorio (*vibe coding*), conservado como referencia para futuras comparaciones con procesos VSlices.

## 1. Situación de partida

Hernán utiliza un portátil Windows para actividades diferentes, especialmente desarrollo de software e investigación de inferencia local con modelos de lenguaje. Tiene instalados programas, componentes y servicios que necesita en ciertos momentos —Docker, WSL, PostgreSQL, SQL Server, herramientas de JetBrains y otros—, pero habitualmente quedan iniciados para que el equipo esté «preparado» aunque no esté trabajando con ellos.

El problema no es que las aplicaciones estén instaladas ni que sean inútiles. Es que **la preparación permanente para muchas actividades consume recursos cuando solo se está realizando una**.

Este costo es relevante en su máquina de referencia, con un Intel Core i5-8265U, gráfica integrada UHD 620 y 16 GB de RAM compartida, que además quiere utilizar para ejecutar modelos de IA.

## 2. Primer episodio: preparar la máquina para inferencia

Hernán comenzó intentando ejecutar experimentos de inferencia local con la menor interferencia posible. Quería reiniciar el computador después de varios días de uso, dejar abierto únicamente Edge con una ventana de ChatGPT y detener los programas y servicios innecesarios.

Solicitó apoyo para identificar procesos y servicios en ejecución a partir de un diagnóstico de PowerShell. En esa exploración aparecieron, entre otros:

- Docker Desktop y Docker Engine;
- PostgreSQL 15 y 16;
- TeamViewer;
- OneDrive;
- Phone Link / CrossDevice;
- Widgets;
- Apple Mobile Device y Bonjour;
- JetBrains ETW Host;
- WSL y servicios auxiliares de SQL Server.

Aclaró que **WSL y SQL Server estaban presentes como apoyo al desarrollo y no necesitaban permanecer activos** durante esos experimentos.

La detención manual resultó incómoda. Algunos intentos de detener servicios desde PowerShell fallaron con mensajes de permisos, lo que hizo visible la necesidad de una operación más clara y centralizada.

## 3. Segunda necesidad: cambiar de contexto sin repetir el procedimiento

Hernán pidió automatizar la preparación del PC después de cada reinicio, inicialmente mediante un script y con dos modos:

- Un modo para ejecutar las pruebas y otros trabajos que requieran liberar recursos.
- Un modo **«Minimal Developer»**, con lo necesario para trabajar con Docker, pero sin levantar todo lo demás.

La intención era **poder cambiar entre contextos de uso**, en lugar de detener servicios manualmente cada vez.

## 4. Tercera necesidad: una aplicación de escritorio configurable

Después de proponer un script, Hernán compartió el repositorio [HernanFAR/system-warmup](https://github.com/HernanFAR/system-warmup) y pidió una aplicación de escritorio .NET.

Lo que pidió explícitamente:

- Configurar el comportamiento desde una interfaz gráfica.
- Persistir la configuración en un archivo JSON local.
- Mostrar qué servicios aparecen iniciados en Windows.
- Permitir seleccionar perfiles que mantengan activos o detengan servicios según las necesidades del momento.
- Hacer que la propia aplicación pueda iniciarse con Windows **una sola vez por sesión**, hasta que la persona la cierre.
- Crear una aplicación visualmente atractiva y funcional en el hardware limitado del portátil.

La automatización no debía exigir que el usuario recuerde comandos cada vez que inicia el equipo.

## 5. Evidencia de la molestia cotidiana

Después de cerrar manualmente Spotify, Teams, Steam, JetBrains Toolbox y otras aplicaciones que no reconocía, Hernán observó que el uso de memoria bajó aproximadamente de **80 % a 60 %**.

No se trató de un benchmark controlado ni de una medición que atribuya toda la variación a aplicaciones específicas. Es una **observación del usuario** que ejemplifica el costo de mantener muchos programas activos por previsión.

Hernán aclaró el sentido de la herramienta: tener un lugar central desde el que gestionar lo necesario para cada actividad. No pretendía simplemente «microoptimizar Windows» sin propósito.

De esta observación surge una necesidad que rebasa los servicios clásicos de Windows: también importa gestionar **aplicaciones de inicio y procesos de usuario**, como Spotify, Teams, Steam y JetBrains Toolbox. Esta extensión es una **implicación identificada**, no una afirmación de que ya exista una gestión completa de esas aplicaciones en el producto.

## 6. Cuarta necesidad: una interfaz coherente en modo claro y oscuro

Hernán ejecutó una primera versión de System Warmup y compartió una captura. El fondo de la ventana aparecía claro, mientras varios paneles permanecían oscuros; algunos textos tenían poco contraste y la interfaz resultaba visualmente inconsistente.

Solicitó corregir el aspecto diurno y nocturno y añadir un botón explícito para alternar entre ambos.

La petición se refiere tanto al botón como a la **legibilidad y coherencia visual del conjunto de la aplicación**, no solo a invertir el color de fondo.

## 7. Evolución posible, no requisito inmediato

Hernán propuso que, a futuro, System Warmup podría evolucionar hacia un servicio o componente en segundo plano que observe consumo de recursos y uso del equipo; después de contar con esas mediciones se podrían investigar microoptimizaciones.

Esto expresa una **dirección futura**, no un requisito para que la primera versión sea útil.

## 8. Qué valor espera obtener

La experiencia deseada puede resumirse así:

> «No quiero mantener todo encendido por si acaso lo necesito. Quiero decidir de forma centralizada qué necesita mi PC para la actividad actual y recuperarlo fácilmente cuando cambie de contexto».

Este párrafo es una **síntesis interpretativa**, no una cita literal.

La utilidad principal es reducir operaciones manuales recurrentes, recuperar recursos cuando corresponda y mantener disponible la capacidad de volver al entorno de desarrollo.

## 9. Alcance de esta narrativa y preguntas que permanecen abiertas

Esta narrativa conserva **lo pedido por Hernán**, no atribuye al usuario decisiones técnicas como WPF, una tarea programada concreta, un mecanismo de elevación o la lista exacta de servicios protegidos. Esas decisiones pertenecen a la [Especificación](Especificación.md) y al código.

Cuestiones no resueltas expresamente en el levantamiento:

- ¿Debe la aplicación capturar el estado *antes* de que arranquen los programas de usuario, o basta con una fotografía del momento en que se abre?
- ¿Debe cambiar la configuración de inicio de aplicaciones o solamente cerrar/abrir procesos en la sesión actual?
- ¿Qué hacer cuando detener un servicio afecta a otro programa activo?
- ¿Qué debe ocurrir si el inicio automático necesita permisos administrativos y falla?
- ¿Cómo comprobar que Docker realmente terminó de levantar, y no solamente que se solicitó su inicio?
- ¿Cuánto control debe tener el usuario sobre procesos sin interfaz, cierres forzados y restauración posterior?

Son preguntas para una evolución posterior, no requisitos que deban atribuirse retroactivamente.

## 10. Uso como caso base para VSlices

Este documento registra la intención humana **antes de imponerle una estructura de dominio retroactiva**. Debe poder compararse con las decisiones y artefactos generados en la experiencia original, incluidos sus aciertos, desviaciones, errores y revisiones.

La reconstrucción es posterior a la implementación inicial; por ello no debe presentarse como un documento de requisitos que haya guiado efectivamente aquel primer desarrollo.

**Fuentes de continuidad:** esta conversación de desarrollo (solicitudes y observaciones de Hernán, 15-11-2026) y el repositorio [system-warmup](https://github.com/HernanFAR/system-warmup). No se incorporan requisitos supuestos a partir de proyectos externos.
