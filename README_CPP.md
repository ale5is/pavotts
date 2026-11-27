# PavoTTS Native — C++ / Qt 6

Esta rama es una reescritura independiente del proyecto Unity. La aplicación usa C++17 y Qt 6; QML proporciona una interfaz adaptable a escritorio y pantallas táctiles Android.

## Componentes migrados

- Configuración de Twitch, YouTube Live, Kick, TikTok TTS, voz, prefijo de lectura, volumen y blacklist.
- Chat unificado con historial de 24 mensajes.
- Twitch IRC por WebSocket con NICK anónimo justinfan, CAP, JOIN, PING/PONG y PRIVMSG; solo lectura y sin OAuth. Se ingresa únicamente el nombre del canal. Es un modo comunitario no documentado como método oficial: Twitch puede limitarlo o rechazarlo y algunos canales podrían no ser accesibles.
- Kick: resolución del chatroom probando `/api/v2/channels/<canal>/chatroom`, `/api/v2/channels/<canal>` y `/api/v1/channels/<canal>`; negociación REST, token temporal, conexión Centrifugo, suscripción, keepalive y reconexión. Se tomó LiteKickChat como referencia para la resolución de IDs y el tratamiento de eventos, pero se conserva Centrifugo en lugar de copiar su cliente Pusher legado.
- YouTube Live: lectura del HTML del canal, extracción de InnerTube y polling de continuaciones.
- Cola FIFO de TTS limitada a 256 elementos, filtro por prefijo, blacklist y reproducción MP3 con Qt Multimedia.
- Configuración JSON en el directorio privado de configuración de la aplicación.
- Interfaz QML responsive: escritorio de dos columnas y móvil de una columna.

## Compilar escritorio

Dependencias: Qt 6.8 o superior con Core, Gui, Qml, Quick, Quick Controls 2, Network, WebSockets y Multimedia; CMake 3.21+ y compilador C++17.

```sh
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release
```

## Android

La misma base C++/QML está preparada para empaquetarse como APK arm64 (Android 9/API 28 o superior) con Qt for Android, Android SDK, NDK 26.1.10909125 y JDK 17. El workflow de GitHub Actions compila escritorio Linux/Windows y Android arm64; publica artefactos Linux, Windows con runtime Qt y APK Android firmado con la clave de depuración para pruebas (no es una firma de distribución para Play Store).

No se debe considerar una plataforma compilada hasta que su job termine correctamente y el artefacto se instale y pruebe en un dispositivo real.

## Seguridad

Los tokens y Session ID son credenciales sensibles. La configuración se guarda en el directorio privado de la aplicación, pero esta primera versión todavía no integra el almacén seguro del sistema operativo. No compartas el archivo `datos.config` ni lo subas al repositorio.

## Pendiente de validación funcional

- InnerTube no es una API pública estable y puede cambiar.
- Kick puede modificar endpoints, tokens o el protocolo Centrifugo.
- TikTok puede rechazar Session ID, User-Agent o endpoint TTS.
- La decodificación MP3 depende de los plugins multimedia disponibles en cada plataforma.
- Android puede suspender conexiones en segundo plano por políticas de batería.
- Hay que verificar Twitch, YouTube, Kick, TTS y reconexión con cuentas reales.

Esta rama no necesita Unity, escenas, MonoBehaviours ni WebView.
