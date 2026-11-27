# PavoTTS Native — C++ / Qt 6

Esta rama es una reescritura independiente del proyecto Unity. La aplicación usa C++17 y Qt 6; QML proporciona una interfaz adaptable a escritorio y pantallas táctiles Android.

## Componentes migrados

- Configuración de Twitch, YouTube Live, Kick, TikTok TTS, voz, prefijo de lectura, volumen y blacklist.
- Chat unificado con historial de 24 mensajes.
- Twitch IRC por WebSocket: autenticación, CAP, JOIN, PING/PONG y PRIVMSG.
- Kick: resolución del canal, negociación REST, token invitado, conexión Centrifugo, suscripción y publicaciones.
- YouTube Live: lectura del HTML del canal, extracción de InnerTube y polling de continuaciones.
- Cola FIFO de TTS limitada a 256 elementos, filtro por prefijo, blacklist y reproducción MP3 con Qt Multimedia.
- Configuración JSON en el directorio privado de configuración de la aplicación.
- Interfaz QML responsive: escritorio de dos columnas y móvil de una columna.

## Compilar escritorio

Dependencias: Qt 6.5 o superior con Core, Gui, Qml, Quick, Quick Controls 2, Network, WebSockets y Multimedia; CMake 3.21+ y compilador C++17.

```sh
cmake -S . -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build --config Release
```

## Android

La misma base C++/QML está preparada para empaquetarse como APK arm64 con Qt for Android, Android SDK, NDK y JDK 17. El workflow de GitHub Actions intenta compilar escritorio Linux/Windows y Android arm64.

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
