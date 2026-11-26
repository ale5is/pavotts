# Native multichat migration

Branch: `feature/native-multichat-no-webview`

## Current architecture

- Unity UI, configuration, blacklist, chat rendering and TTS remain in C#.
- `ChatMessageBus` is the single ingress for all platform messages. It buffers arrivals under a lock and dispatches them on Unity's main thread through `BotrixChat.Update`.
- The pending ingress queue is bounded to 4096 messages; when saturated it drops the oldest pending ingress items rather than growing without limit.
- The TTS FIFO backlog in the sample scene is set to 256 messages. If the voice engine falls too far behind, the existing bounded TTS policy drops the oldest queued speech items.
- Combined order is the order Unity receives and dispatches messages. The three platforms do not expose a shared synchronized clock, so this is not a claim of exact cross-platform source-event timestamp order.
- `NativeWebSocket` is installed through UPM for the native WebSocket transports. Version 2.x dispatches callbacks to Unity's main thread through `SynchronizationContext`; no per-frame manual dispatch loop is required.
- The existing TikTok TTS engine remains unchanged and still requires its TikTok Session ID.

## Platform implementations

### Twitch

Uses secure Twitch IRC WebSocket at `wss://irc-ws.chat.twitch.tv:443`.

Twitch's current developer documentation requires a user access token with `chat:read` and the username associated with that token. The configuration UI therefore includes a separate username and OAuth token field in addition to the channel name. The token is stored in the existing local `datos.config` file alongside the existing TikTok session ID; protect that file as a credential store.

The connector handles IRC CAP negotiation, JOIN, PRIVMSG parsing, display-name tags, PING/PONG, reconnect backoff and stops retrying after an explicit OAuth rejection. Incoming WebSocket chunks are buffered until complete CRLF-delimited IRC lines arrive, so a line split across WebSocket messages is not parsed prematurely. The partial-line buffer is cleared on reconnect and bounded to 64 KiB. Twitch recommends EventSub for new chat integrations; IRC remains supported but has feature limitations. IRC was selected here as the smallest direct read-only transport; a later EventSub migration would require an application Client ID and EventSub subscription lifecycle.

### YouTube

Uses an in-project InnerTube polling client, not a browser or WebView. It loads the channel's live page, locates the active live-chat continuation, then polls the InnerTube live-chat endpoint and extracts text, author names and common chat renderers.

InnerTube is an undocumented YouTube web interface. YouTube can change its page markup, API key/client version fields, continuation schema or rate limits without notice. The client rechecks the channel every 30 seconds when no live chat is available, then resumes polling when a public live chat appears. This connector must be tested against real streams and may need parser updates.

### Kick

Uses Kick's current Centrifugo realtime WebSocket path rather than the retired Pusher app key. It resolves the channel chatroom, negotiates the realtime endpoint, obtains a temporary guest token, sends the Kick Origin header on the WebSocket handshake, connects and subscribes to `chatrooms.{chatroom_id}.v2`.

The client answers Centri­fugo ping frames, parses both object and string publication payloads, surfaces protocol error replies instead of silently waiting for a handshake timeout, reconnects with bounded backoff and renews the guest session before the documented 45-minute token lifetime. Kick's website realtime API is not the same as its documented public API and may change.

## Performance and C++ decision

Do not port the whole application to C++. Unity scenes, TMP UI, MonoBehaviours, audio playback, configuration and lifecycle integration are already managed by Unity's C# runtime. Rewriting those parts would increase interop and maintenance costs without reducing network latency.

For release targets that use IL2CPP, Unity translates managed C# assemblies to C++ and compiles them to native code. This is the sensible native compilation route for the existing Unity application. A custom C++ plugin should only be introduced if profiling later proves a CPU-heavy parser or codec is a bottleneck. Chat parsing and three asynchronous network connections are not expected to be CPU-bound.

## Verification still required

This branch has not yet been opened or built in the Unity 2022.3.62f3 Editor in this environment. The repository edits remove all WebView-named source, plugin, platform and WebGL template assets, but the following runtime checks are still required:

1. Open the project and allow UPM to resolve NativeWebSocket.
2. Confirm there are no Unity compile errors and that the SampleScene loads without missing scripts.
3. Test Twitch with a valid token and the matching account username.
4. Test YouTube while the configured channel is live with public chat enabled.
5. Test Kick against a live channel; confirm the WebSocket handshake, Centrifugo connect reply, subscription acknowledgement and received chat messages.
6. Send messages on all three platforms simultaneously and verify the visible chat and TTS process them in the order Unity receives them.
7. Test disconnect, reconnect, application pause/resume and a Kick token renewal cycle.
8. Confirm the build contains no WebView/Chromium plugin or WebView native library.

A source-level change is not treated as a successful live connection until these tests pass.


## Verificación manual pendiente en Unity 2022.3

La revisión estática del código no sustituye una compilación ni una prueba contra los servicios. Antes de considerar terminado el reemplazo, ejecutar esta matriz en una copia del proyecto:

1. Importar el proyecto y esperar a que Unity resuelva `com.endel.nativewebsocket`.
2. Confirmar que no aparecen errores de compilación en Console.
3. Abrir la configuración, introducir solamente un canal y conectar cada plataforma por separado.
4. Twitch: usar un token OAuth válido con `chat:read`; comprobar JOIN, recepción de PRIVMSG, PING/PONG y rechazo de token inválido.
5. YouTube: probar con un canal que esté transmitiendo en directo con el chat habilitado; comprobar mensajes normales, Super Chats y reconexión al finalizar la emisión.
6. Kick: comprobar resolución del canal, negociación Centrifugo, confirmación de suscripción, mensajes y renovación de sesión.
7. Conectar dos y luego tres plataformas a la vez; comprobar que todos los mensajes llegan a una única cola TTS FIFO.
8. Desconectar mientras llegan mensajes; comprobar que no se encolan mensajes posteriores y que una reconexión no duplica suscripciones.
9. Mantener la aplicación en segundo plano y verificar el comportamiento con `Application.runInBackground`.
10. Repetir en Windows y Android si ambos son destinos de publicación.

No marcar el conector como validado hasta completar estas pruebas. En particular, YouTube InnerTube y el protocolo realtime de Kick no son interfaces públicas estables y pueden cambiar sin aviso.
