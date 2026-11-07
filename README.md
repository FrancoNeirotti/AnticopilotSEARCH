# Copilot Launcher

Buscador nativo para Windows al estilo Spotlight de macOS. Presionás la **tecla Copilot** del teclado (o **Alt+Espacio**) y aparece una barra centrada para buscar **aplicaciones** y **archivos**.

## Características

- Se activa con la tecla Copilot de Windows 11, que envía `Win + Shift + F23`. La tecla queda interceptada, así que Windows no abre la app de Copilot.
- Atajo de respaldo: **Alt+Espacio**.
- Busca aplicaciones instaladas: escritorio, Microsoft Store (`shell:AppsFolder`) y accesos directos del menú Inicio y del escritorio.
- Busca archivos y carpetas con el índice de **Windows Search**, que es instantáneo. Si el índice no está disponible, escanea Escritorio, Documentos, Descargas, OneDrive, etc.
- Coincidencia difusa: por prefijo, por inicio de palabra, por iniciales (`vsc` → Visual Studio Code) y sin distinguir acentos.
- Muestra los íconos del sistema y funciona en la pantalla donde está el mouse.
- Corre en la bandeja del sistema, con una sola instancia y la opción "Iniciar con Windows".

## Atajos dentro del buscador

| Tecla | Acción |
|---|---|
| ↑ / ↓ | Moverse entre resultados |
| Enter | Abrir |
| Ctrl+Enter | Abrir la ubicación del archivo |
| Ctrl+Shift+Enter | Ejecutar como administrador |
| Esc | Cerrar (también se cierra al hacer clic afuera) |

## Compilar

Requisitos: Windows 10/11 y el SDK de .NET 8 (`winget install Microsoft.DotNet.SDK.8`).

```powershell
.\build.ps1
```

Genera `publish\CopilotLauncher.exe`, un ejecutable autónomo que no necesita .NET instalado.

## Instalar / desinstalar (sin permisos de administrador)

```powershell
.\install.ps1     # copia a %LOCALAPPDATA%\Programs\CopilotLauncher, activa el inicio con Windows y lo ejecuta
.\uninstall.ps1
```

También podés ejecutar `publish\CopilotLauncher.exe` directamente.

## Menú de la bandeja

Haciendo clic derecho en el ícono de la lupa:

- Abrir buscador
- Actualizar lista de aplicaciones
- Iniciar con Windows
- Estado de los atajos
- Abrir registro (`%LOCALAPPDATA%\CopilotLauncher\launcher.log`)
- Salir

## Notas sobre la tecla Copilot

- Algunos teclados o fabricantes remapean la tecla Copilot a otra combinación. Si al presionarla no aparece el buscador, usá **Alt+Espacio**.
- Si querés, podés configurar la tecla en *Configuración → Personalización → Entrada de texto → Personalizar la tecla Copilot del teclado*. Elegí "Buscar" o una app cualquiera: el launcher intercepta la tecla antes que Windows.
- Si otra app ya usa Alt+Espacio (por ejemplo, PowerToys Run), el menú de la bandeja lo indica y la tecla Copilot sigue funcionando.

## Diagnóstico

```powershell
.\publish\CopilotLauncher.exe --selftest excel
```

Escribe en el registro las aplicaciones y archivos encontrados para "excel".
