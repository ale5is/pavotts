#include "ChatController.h"

#include <QDir>
#include <QFile>
#include <QJsonArray>
#include <QJsonDocument>
#include <QRegularExpression>
#include <QStandardPaths>
#include <QUrl>
#include <QUrlQuery>
#include <QDateTime>
#include <QUuid>
#include <functional>

namespace {
QString cleanText(QString value)
{
    value.remove(QChar(0x200B));
    value.remove(QChar(0x200C));
    value.remove(QChar(0x200D));
    value.remove(QChar(0xFEFF));
    value.replace(QRegularExpression(QStringLiteral("[\\r\\n\\t]+")), QStringLiteral(" "));
    value.replace(QRegularExpression(QStringLiteral(" {2,}")), QStringLiteral(" "));
    return value.trimmed();
}

QString jsonEscaped(const QString &value)
{
    QJsonArray array;
    array.append(value);
    const QByteArray json = QJsonDocument(array).toJson(QJsonDocument::Compact);
    return QString::fromUtf8(json.mid(1, json.size() - 2));
}

void collectYouTubeMessages(const QJsonValue &value, QList<QPair<QString, QString>> &messages)
{
    if (value.isArray()) {
        for (const QJsonValue &child : value.toArray())
            collectYouTubeMessages(child, messages);
        return;
    }
    if (!value.isObject())
        return;

    const QJsonObject object = value.toObject();
    const QStringList rendererTypes = {
        QStringLiteral("liveChatTextMessageRenderer"),
        QStringLiteral("liveChatPaidMessageRenderer"),
        QStringLiteral("liveChatMembershipItemRenderer"),
        QStringLiteral("liveChatPaidStickerRenderer")
    };
    for (const QString &type : rendererTypes) {
        if (!object.contains(type))
            continue;
        const QJsonObject renderer = object.value(type).toObject();
        const QString name = renderer.value(QStringLiteral("authorName")).toObject()
                                 .value(QStringLiteral("simpleText")).toString(QStringLiteral("YouTube"));
        QString message;
        const QJsonArray runs = renderer.value(QStringLiteral("message")).toObject()
                                    .value(QStringLiteral("runs")).toArray();
        for (const QJsonValue &run : runs) {
            const QJsonObject runObject = run.toObject();
            message += runObject.value(QStringLiteral("text")).toString();
            if (runObject.contains(QStringLiteral("emoji")) && message.isEmpty()) {
                const QJsonArray shortcuts = runObject.value(QStringLiteral("emoji")).toObject()
                                                .value(QStringLiteral("shortcuts")).toArray();
                if (!shortcuts.isEmpty())
                    message += shortcuts.first().toString();
            }
        }
        if (message.isEmpty())
            message = renderer.value(QStringLiteral("headerSubtext")).toObject()
                          .value(QStringLiteral("simpleText")).toString();
        if (!message.isEmpty())
            messages.append(qMakePair(name, message));
    }
    for (auto it = object.begin(); it != object.end(); ++it)
        collectYouTubeMessages(it.value(), messages);
}
}

ChatController::ChatController(QObject *parent)
    : QObject(parent), m_player(this), m_audioOutput(this)
{
    m_player.setAudioOutput(&m_audioOutput);
    m_audioOutput.setVolume(m_volume);
    connect(&m_player, &QMediaPlayer::mediaStatusChanged, this, [this](QMediaPlayer::MediaStatus state) {
        if (state == QMediaPlayer::EndOfMedia || state == QMediaPlayer::InvalidMedia)
            finishSpeech();
    });
    connect(&m_player, &QMediaPlayer::errorOccurred, this, [this](QMediaPlayer::Error, const QString &error) {
        setStatus(QStringLiteral("TTS"), QStringLiteral("Error de reproducción: ") + error);
        finishSpeech();
    });
    m_youtubeTimer.setSingleShot(true);
    connect(&m_youtubeTimer, &QTimer::timeout, this, &ChatController::pollYouTube);
    loadSettings();
}

ChatController::~ChatController()
{
    disconnectAll();
}

QString ChatController::status() const { return m_status; }
QVariantList ChatController::messages() const { return m_messages; }
QString ChatController::twitchChannel() const { return m_twitchChannel; }
QString ChatController::twitchUsername() const { return m_twitchUsername; }
QString ChatController::twitchToken() const { return m_twitchToken; }
QString ChatController::youtubeChannel() const { return m_youtubeChannel; }
QString ChatController::kickChannel() const { return m_kickChannel; }
QString ChatController::sessionId() const { return m_sessionId; }
QString ChatController::voice() const { return m_voice; }
QString ChatController::character() const { return m_character; }
QString ChatController::commandPrefix() const { return m_commandPrefix; }
double ChatController::volume() const { return m_volume; }
bool ChatController::connected() const { return m_connected; }
bool ChatController::speaking() const { return m_speaking; }
int ChatController::queuedSpeech() const { return m_speechQueue.size(); }

QString ChatController::blacklistText() const
{
    QStringList names = m_blacklist.values();
    names.sort(Qt::CaseInsensitive);
    return names.join(QStringLiteral("\n"));
}

void ChatController::setTwitchChannel(const QString &v) { if (m_twitchChannel != v) { m_twitchChannel = v.trimmed(); emit settingsChanged(); } }
void ChatController::setTwitchUsername(const QString &v) { if (m_twitchUsername != v) { m_twitchUsername = v.trimmed(); emit settingsChanged(); } }
void ChatController::setTwitchToken(const QString &v) { if (m_twitchToken != v) { m_twitchToken = v.trimmed(); emit settingsChanged(); } }
void ChatController::setYoutubeChannel(const QString &v) { if (m_youtubeChannel != v) { m_youtubeChannel = v.trimmed(); emit settingsChanged(); } }
void ChatController::setKickChannel(const QString &v) { if (m_kickChannel != v) { m_kickChannel = v.trimmed(); emit settingsChanged(); } }
void ChatController::setSessionId(const QString &v) { if (m_sessionId != v) { m_sessionId = v.trimmed(); emit settingsChanged(); } }
void ChatController::setVoice(const QString &v) { if (m_voice != v) { m_voice = v.trimmed(); emit settingsChanged(); } }
void ChatController::setCharacter(const QString &v) { if (m_character != v) { m_character = v; emit settingsChanged(); } }
void ChatController::setCommandPrefix(const QString &v) { if (m_commandPrefix != v) { m_commandPrefix = v; emit settingsChanged(); } }
void ChatController::setVolume(double v) { m_volume = qBound(0.0, v, 1.0); m_audioOutput.setVolume(m_volume); emit settingsChanged(); }

void ChatController::loadSettings()
{
    const QString directory = QStandardPaths::writableLocation(QStandardPaths::AppConfigLocation);
    QDir().mkpath(directory);
    QFile file(directory + QStringLiteral("/datos.config"));
    if (!file.open(QIODevice::ReadOnly))
        return;
    const QJsonDocument document = QJsonDocument::fromJson(file.readAll());
    if (!document.isObject())
        return;
    const QJsonObject o = document.object();
    m_twitchChannel = o.value("twitchChannel").toString();
    m_twitchUsername = o.value("twitchUsername").toString();
    m_twitchToken = o.value("twitchAccessToken").toString();
    m_youtubeChannel = o.value("youtubeChannel").toString();
    m_kickChannel = o.value("kickChannel").toString();
    m_sessionId = o.value("sessionId").toString();
    m_voice = o.value("voz").toString("es_002");
    m_character = o.value("caracter").toString();
    m_commandPrefix = o.value("prefijo").toString("*");
    m_volume = qBound(0.0, o.value("volumen").toDouble(1.0), 1.0);
    const QStringList blocked = o.value("blacklist").toString().split(',', Qt::SkipEmptyParts);
    for (const QString &name : blocked)
        m_blacklist.insert(name.trimmed().toCaseFolded());
    m_audioOutput.setVolume(m_volume);
    emit settingsChanged();
}

void ChatController::saveSettings()
{
    QJsonObject o;
    o.insert("twitchChannel", m_twitchChannel);
    o.insert("twitchUsername", m_twitchUsername);
    o.insert("twitchAccessToken", m_twitchToken);
    o.insert("youtubeChannel", m_youtubeChannel);
    o.insert("kickChannel", m_kickChannel);
    o.insert("sessionId", m_sessionId);
    o.insert("voz", m_voice);
    o.insert("caracter", m_character);
    o.insert("prefijo", m_commandPrefix);
    o.insert("volumen", m_volume);
    o.insert("blacklist", blacklistText().replace('\n', ','));
    const QString directory = QStandardPaths::writableLocation(QStandardPaths::AppConfigLocation);
    QDir().mkpath(directory);
    QFile file(directory + QStringLiteral("/datos.config"));
    if (!file.open(QIODevice::WriteOnly | QIODevice::Truncate)) {
        setStatus(QStringLiteral("Configuración"), QStringLiteral("No se pudo guardar: ") + file.errorString());
        return;
    }
    file.write(QJsonDocument(o).toJson(QJsonDocument::Indented));
    setStatus(QStringLiteral("Configuración"), QStringLiteral("Configuración guardada."));
}

void ChatController::setStatus(const QString &platform, const QString &message)
{
    m_status = platform + QStringLiteral(": ") + message;
    emit statusChanged();
}

void ChatController::setConnectedState()
{
    const bool now = !m_stopping && (m_twitchSocket || m_kickSocket || !m_youtubeContinuation.isEmpty());
    if (m_connected != now) {
        m_connected = now;
        emit connectionChanged();
    }
}

void ChatController::connectAll()
{
    disconnectAll();
    m_stopping = false;
    saveSettings();
    bool any = false;
    if (!m_twitchChannel.isEmpty() && !m_twitchUsername.isEmpty() && !m_twitchToken.isEmpty()) {
        connectTwitch();
        any = true;
    }
    if (!m_kickChannel.isEmpty()) {
        connectKick();
        any = true;
    }
    if (!m_youtubeChannel.isEmpty()) {
        connectYouTube();
        any = true;
    }
    if (!any)
        setStatus(QStringLiteral("Chat"), QStringLiteral("Configurá al menos una plataforma."));
    else
        setStatus(QStringLiteral("Chat"), QStringLiteral("Iniciando conexiones nativas..."));
}

void ChatController::disconnectAll()
{
    ++m_connectionGeneration;
    m_stopping = true;
    m_youtubeTimer.stop();
    if (m_youtubeReply) {
        auto *reply = m_youtubeReply;
        m_youtubeReply = nullptr;
        reply->abort();
        reply->deleteLater();
    }
    if (m_ttsReply) {
        auto *reply = m_ttsReply;
        m_ttsReply = nullptr;
        reply->abort();
        reply->deleteLater();
    }
    if (m_twitchSocket) {
        auto *socket = m_twitchSocket;
        m_twitchSocket = nullptr;
        socket->close();
        socket->deleteLater();
    }
    if (m_kickSocket) {
        auto *socket = m_kickSocket;
        m_kickSocket = nullptr;
        socket->close();
        socket->deleteLater();
    }
    ++m_speechGeneration;
    m_player.stop();
    m_speechQueue.clear();
    m_speaking = false;
    emit speakingChanged();
    m_connected = false;
    emit connectionChanged();
    setStatus(QStringLiteral("Chat"), QStringLiteral("Desconectado."));
}

void ChatController::connectTwitch()
{
    auto *socket = new QWebSocket(QString(), QWebSocketProtocol::VersionLatest, this);
    m_twitchSocket = socket;
    connect(socket, &QWebSocket::connected, this, [this, socket]() {
        if (m_twitchSocket != socket || m_stopping) return;
        setStatus(QStringLiteral("Twitch"), QStringLiteral("Conectado; autenticando..."));
        socket->sendTextMessage(QStringLiteral("PASS oauth:%1\r\n").arg(m_twitchToken));
        socket->sendTextMessage(QStringLiteral("NICK %1\r\n").arg(m_twitchUsername));
        socket->sendTextMessage(QStringLiteral("CAP REQ :twitch.tv/tags twitch.tv/commands\r\n"));
        socket->sendTextMessage(QStringLiteral("JOIN #%1\r\n").arg(m_twitchChannel));
        setConnectedState();
    });
    connect(socket, &QWebSocket::textMessageReceived, this, [this, socket](const QString &chunk) {
        if (m_twitchSocket != socket || m_stopping) return;
        m_twitchBuffer += chunk;
        if (m_twitchBuffer.size() > 65536) m_twitchBuffer.clear();
        int newline = -1;
        while ((newline = m_twitchBuffer.indexOf('\n')) >= 0) {
            if (m_twitchSocket != socket || m_stopping) return;
            const QString line = m_twitchBuffer.left(newline).trimmed();
            m_twitchBuffer.remove(0, newline + 1);
            handleTwitchLine(line, socket);
        }
    });
    connect(socket, &QWebSocket::disconnected, this, [this, socket]() {
        if (m_twitchSocket == socket) {
            m_twitchSocket = nullptr;
            setStatus(QStringLiteral("Twitch"), QStringLiteral("Conexión cerrada."));
            setConnectedState();
        }
    });
    connect(socket, &QWebSocket::errorOccurred, this, [this, socket](QAbstractSocket::SocketError) {
        if (m_twitchSocket == socket && !m_stopping)
            setStatus(QStringLiteral("Twitch"), socket->errorString());
    });
    socket->open(QUrl(QStringLiteral("wss://irc-ws.chat.twitch.tv:443")));
}

void ChatController::handleTwitchLine(const QString &line, QWebSocket *socket)
{
    if (m_twitchSocket != socket || m_stopping) return;
    if (line.startsWith(QStringLiteral("PING "))) {
        socket->sendTextMessage(QStringLiteral("PONG ") + line.mid(5) + QStringLiteral("\r\n"));
        return;
    }
    if (line.contains(QStringLiteral("Login authentication failed"))) {
        setStatus(QStringLiteral("Twitch"), QStringLiteral("Token rechazado (verificá chat:read)."));
        socket->close();
        return;
    }
    const int marker = line.indexOf(QStringLiteral(" PRIVMSG "));
    if (marker < 0) return;
    QString name;
    if (line.startsWith('@')) {
        const int display = line.indexOf(QStringLiteral("display-name="));
        if (display >= 0) {
            const int end = line.indexOf(';', display);
            name = line.mid(display + 13, (end < 0 ? line.size() : end) - display - 13);
        }
    }
    if (name.isEmpty()) {
        const int bang = line.indexOf('!');
        const int colon = line.indexOf(':');
        if (colon >= 0 && bang > colon) name = line.mid(colon + 1, bang - colon - 1);
    }
    const int body = line.indexOf(QStringLiteral(" :"), marker);
    if (body >= 0) publishMessage(name, line.mid(body + 2), QStringLiteral("Twitch"));
}

void ChatController::connectKick()
{
    const quint64 generation = m_connectionGeneration;
    setStatus(QStringLiteral("Kick"), QStringLiteral("Resolviendo el canal..."));
    QNetworkRequest request{QUrl(QStringLiteral("https://kick.com/api/v2/channels/") + QString::fromLatin1(QUrl::toPercentEncoding(m_kickChannel)))};
    request.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36"));
    request.setRawHeader("Accept", "application/json");
    auto *reply = m_network.get(request);
    connect(reply, &QNetworkReply::finished, this, [this, reply, generation]() {
        if (m_stopping || generation != m_connectionGeneration) { reply->deleteLater(); return; }
        const QByteArray bytes = reply->readAll();
        const QString error = reply->errorString();
        const int code = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        const QJsonDocument doc = QJsonDocument::fromJson(bytes);
        reply->deleteLater();
        if (!doc.isObject() || code < 200 || code >= 300) {
            setStatus(QStringLiteral("Kick"), QStringLiteral("No se pudo resolver el canal: ") + error + QStringLiteral(" (HTTP %1)").arg(code));
            return;
        }
        const int room = doc.object().value(QStringLiteral("chatroom")).toObject().value(QStringLiteral("id")).toInt();
        if (room <= 0) { setStatus(QStringLiteral("Kick"), QStringLiteral("No se obtuvo chatroom ID.")); return; }
        m_kickChatroomId = QString::number(room);
        const QString clientId = QUuid::createUuid().toString(QUuid::WithoutBraces);
        const QJsonObject body{
            {QStringLiteral("client"), QJsonObject{{QStringLiteral("id"), clientId}, {QStringLiteral("type"), QStringLiteral("web")}}},
            {QStringLiteral("capabilities"), QJsonObject{{QStringLiteral("accepted_providers"), QJsonArray{QJsonObject{{QStringLiteral("provider"), QStringLiteral("centrifugo")}}}}}}
        };
        QNetworkRequest post{QUrl(QStringLiteral("https://web.kick.com/api/v1/realtime/channels/%1/chat/connection").arg(m_kickChatroomId))};
        post.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
        post.setRawHeader("Origin", "https://kick.com");
        auto *connection = m_network.post(post, QJsonDocument(body).toJson(QJsonDocument::Compact));
        connect(connection, &QNetworkReply::finished, this, [this, connection, clientId, generation]() {
            if (m_stopping || generation != m_connectionGeneration) { connection->deleteLater(); return; }
            const QByteArray bytes = connection->readAll();
            const int code = connection->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
            connection->deleteLater();
            const QJsonDocument data = QJsonDocument::fromJson(bytes);
            if (!data.isObject() || code < 200 || code >= 300) {
                setStatus(QStringLiteral("Kick"), QStringLiteral("Falló la negociación realtime (HTTP %1).").arg(code));
                return;
            }
            QString wsUrl;
            const QJsonArray list = data.object().value(QStringLiteral("data")).toObject().value(QStringLiteral("connections")).toArray();
            for (const QJsonValue &value : list) {
                const QJsonObject candidate = value.toObject();
                if (candidate.value(QStringLiteral("provider")).toString() == QStringLiteral("centrifugo"))
                    wsUrl = candidate.value(QStringLiteral("credentials")).toObject().value(QStringLiteral("url")).toString();
            }
            if (wsUrl.isEmpty()) { setStatus(QStringLiteral("Kick"), QStringLiteral("No se ofreció endpoint Centrifugo.")); return; }
            QNetworkRequest auth{QUrl(QStringLiteral("https://web.kick.com/api/v1/realtime/auth/connection"))};
            auth.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
            auth.setRawHeader("Origin", "https://kick.com");
            const QByteArray authBody = QJsonDocument(QJsonObject{{QStringLiteral("client_id"), clientId}}).toJson(QJsonDocument::Compact);
            auto *tokenReply = m_network.post(auth, authBody);
            connect(tokenReply, &QNetworkReply::finished, this, [this, tokenReply, wsUrl, generation]() {
                if (m_stopping || generation != m_connectionGeneration) { tokenReply->deleteLater(); return; }
                const QByteArray bytes = tokenReply->readAll();
                const int code = tokenReply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
                tokenReply->deleteLater();
                const QJsonDocument tokenDoc = QJsonDocument::fromJson(bytes);
                const QString token = tokenDoc.object().value(QStringLiteral("data")).toObject().value(QStringLiteral("token")).toString();
                if (code < 200 || code >= 300 || token.isEmpty()) {
                    setStatus(QStringLiteral("Kick"), QStringLiteral("No se pudo obtener el token realtime (HTTP %1).").arg(code));
                    return;
                }
                auto *socket = new QWebSocket(QString(), QWebSocketProtocol::VersionLatest, this);
                m_kickSocket = socket;
                socket->setProperty("kickToken", token);
                connect(socket, &QWebSocket::connected, this, [this, socket, generation]() {
                    if (m_kickSocket != socket || m_stopping || generation != m_connectionGeneration) return;
                    socket->sendTextMessage(QStringLiteral("{\"id\":1,\"connect\":{\"token\":\"%1\",\"name\":\"pavotts-native\"}}")
                                                .arg(jsonEscaped(socket->property("kickToken").toString())));
                    setStatus(QStringLiteral("Kick"), QStringLiteral("Autenticando realtime..."));
                });
                connect(socket, &QWebSocket::textMessageReceived, this, [this, socket, generation](const QString &text) {
                    if (m_kickSocket != socket || m_stopping || generation != m_connectionGeneration) return;
                    const QJsonDocument frame = QJsonDocument::fromJson(text.toUtf8());
                    if (frame.isObject()) handleKickFrame(frame.object(), socket);
                });
                connect(socket, &QWebSocket::disconnected, this, [this, socket, generation]() {
                    if (m_kickSocket == socket && generation == m_connectionGeneration) {
                        m_kickSocket = nullptr;
                        setStatus(QStringLiteral("Kick"), QStringLiteral("Conexión cerrada."));
                        setConnectedState();
                    }
                });
                connect(socket, &QWebSocket::errorOccurred, this, [this, socket, generation](QAbstractSocket::SocketError) {
                    if (m_kickSocket == socket && !m_stopping && generation == m_connectionGeneration)
                        setStatus(QStringLiteral("Kick"), socket->errorString());
                });
                QNetworkRequest wsRequest{QUrl(wsUrl)};
                wsRequest.setRawHeader("Origin", "https://kick.com");
                socket->open(wsRequest);
            });
        });
    });
}

void ChatController::handleKickFrame(const QJsonObject &frame, QWebSocket *socket)
{
    if (m_kickSocket != socket || m_stopping) return;
    if (frame.contains(QStringLiteral("error"))) {
        setStatus(QStringLiteral("Kick"), QStringLiteral("Error de protocolo realtime."));
        socket->close();
        return;
    }
    if (frame.value(QStringLiteral("id")).toInt() == 1 && frame.contains(QStringLiteral("connect"))) {
        const QString subscription = QStringLiteral("{\"id\":2,\"subscribe\":{\"channel\":\"chatrooms.%1.v2\"}}").arg(m_kickChatroomId);
        socket->sendTextMessage(subscription);
        setStatus(QStringLiteral("Kick"), QStringLiteral("Autenticado; suscribiendo al chat..."));
        setConnectedState();
        return;
    }
    if (frame.value(QStringLiteral("id")).toInt() == 2 && frame.contains(QStringLiteral("subscribe"))) {
        setStatus(QStringLiteral("Kick"), QStringLiteral("Chat conectado."));
        return;
    }
    const QJsonObject push = frame.value(QStringLiteral("push")).toObject();
    if (push.contains(QStringLiteral("ping"))) {
        socket->sendTextMessage(QStringLiteral("{\"pong\":{}}"));
        return;
    }
    const QJsonObject publication = push.value(QStringLiteral("pub")).toObject();
    QJsonObject envelope = publication.value(QStringLiteral("data")).toObject();
    if (envelope.isEmpty() && publication.value(QStringLiteral("data")).isString()) {
        const QJsonDocument nested = QJsonDocument::fromJson(publication.value(QStringLiteral("data")).toString().toUtf8());
        envelope = nested.object();
    }
    if (envelope.value(QStringLiteral("event")).toString() != QStringLiteral("App\\Events\\ChatMessageEvent"))
        return;
    QJsonObject data = envelope.value(QStringLiteral("data")).toObject();
    if (data.isEmpty() && envelope.value(QStringLiteral("data")).isString()) {
        const QJsonDocument nested = QJsonDocument::fromJson(envelope.value(QStringLiteral("data")).toString().toUtf8());
        data = nested.object();
    }
    const QString message = data.value(QStringLiteral("content")).toString();
    const QString name = data.value(QStringLiteral("sender")).toObject().value(QStringLiteral("username")).toString(QStringLiteral("Kick"));
    if (!message.isEmpty())
        publishMessage(name, message, QStringLiteral("Kick"));
}

void ChatController::connectYouTube()
{
    stopYouTube();
    QString channel = m_youtubeChannel.trimmed();
    if (!channel.startsWith('@') && !channel.startsWith(QStringLiteral("UC")))
        channel.prepend('@');
    const QUrl pageUrl(QStringLiteral("https://www.youtube.com/") + channel + QStringLiteral("/live"));
    setStatus(QStringLiteral("YouTube"), QStringLiteral("Buscando transmisión en vivo..."));
    auto *reply = m_network.get(QNetworkRequest(pageUrl));
    m_youtubeReply = reply;
    connect(reply, &QNetworkReply::finished, this, [this, reply]() {
        if (m_stopping || m_youtubeReply != reply) { reply->deleteLater(); return; }
        const QString html = QString::fromUtf8(reply->readAll());
        const int code = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        reply->deleteLater();
        m_youtubeReply = nullptr;
        if (code < 200 || code >= 400) {
            setStatus(QStringLiteral("YouTube"), QStringLiteral("No se pudo abrir el canal (HTTP %1).").arg(code));
            return;
        }
        m_youtubeApiKey = QRegularExpression(QStringLiteral("\"INNERTUBE_API_KEY\"\\s*:\\s*\"([^\"]+)\"")).match(html).captured(1);
        m_youtubeClientVersion = QRegularExpression(QStringLiteral("\"clientVersion\"\\s*:\\s*\"([^\"]+)\"")).match(html).captured(1);
        m_youtubeContinuation = QRegularExpression(QStringLiteral("\"continuation\"\\s*:\\s*\"([^\"\\\\]+)")).match(html).captured(1);
        if (m_youtubeApiKey.isEmpty() || m_youtubeContinuation.isEmpty()) {
            setStatus(QStringLiteral("YouTube"), QStringLiteral("No se encontró un chat en vivo activo para ese canal."));
            return;
        }
        setStatus(QStringLiteral("YouTube"), QStringLiteral("Transmisión detectada; leyendo chat..."));
        setConnectedState();
        m_youtubePollDelay = 1000;
        pollYouTube();
    });
}

void ChatController::pollYouTube()
{
    if (m_stopping || m_youtubeContinuation.isEmpty()) return;
    const QString endpoint = QStringLiteral("https://www.youtube.com/youtubei/v1/live_chat/get_live_chat?key=")
                             + QString::fromLatin1(QUrl::toPercentEncoding(m_youtubeApiKey));
    const QString body = QStringLiteral("{\"context\":{\"client\":{\"clientName\":\"WEB\",\"clientVersion\":\"%1\",\"hl\":\"es\",\"gl\":\"AR\"}},\"continuation\":\"%2\"}")
                             .arg(jsonEscaped(m_youtubeClientVersion), jsonEscaped(m_youtubeContinuation));
    QNetworkRequest request{QUrl(endpoint)};
    request.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
    auto *reply = m_network.post(request, body.toUtf8());
    m_youtubeReply = reply;
    connect(reply, &QNetworkReply::finished, this, [this, reply]() {
        if (m_stopping || m_youtubeReply != reply) { reply->deleteLater(); return; }
        const QByteArray bytes = reply->readAll();
        const QString error = reply->errorString();
        const bool failed = reply->error() != QNetworkReply::NoError;
        reply->deleteLater();
        m_youtubeReply = nullptr;
        if (failed) {
            setStatus(QStringLiteral("YouTube"), QStringLiteral("Error consultando el chat: ") + error);
            m_youtubeTimer.start(5000);
            return;
        }
        const QJsonDocument document = QJsonDocument::fromJson(bytes);
        if (document.isObject())
            parseYouTubeResponse(document.object());
        m_youtubeTimer.start(qMax(1000, m_youtubePollDelay));
    });
}

void ChatController::parseYouTubeResponse(const QJsonObject &root)
{
    QList<QPair<QString, QString>> found;
    collectYouTubeMessages(root, found);
    for (const auto &entry : found)
        publishMessage(entry.first, entry.second, QStringLiteral("YouTube"));

    std::function<QString(const QJsonValue &)> findContinuation = [&](const QJsonValue &value) -> QString {
        if (value.isObject()) {
            const QJsonObject object = value.toObject();
            for (auto it = object.begin(); it != object.end(); ++it) {
                if (it.key().contains(QStringLiteral("ContinuationData"), Qt::CaseInsensitive)) {
                    const QString token = it.value().toObject().value(QStringLiteral("continuation")).toString();
                    if (!token.isEmpty()) return token;
                }
                const QString nested = findContinuation(it.value());
                if (!nested.isEmpty()) return nested;
            }
        } else if (value.isArray()) {
            for (const QJsonValue &child : value.toArray()) {
                const QString nested = findContinuation(child);
                if (!nested.isEmpty()) return nested;
            }
        }
        return {};
    };
    const QString next = findContinuation(root);
    if (!next.isEmpty()) m_youtubeContinuation = next;
    m_youtubePollDelay = 3000;
}

void ChatController::stopYouTube()
{
    m_youtubeTimer.stop();
    if (m_youtubeReply) {
        auto *reply = m_youtubeReply;
        m_youtubeReply = nullptr;
        reply->abort();
        reply->deleteLater();
    }
    m_youtubeContinuation.clear();
}

void ChatController::publishMessage(const QString &rawName, const QString &rawText, const QString &platform)
{
    const QString name = cleanText(rawName).isEmpty() ? QStringLiteral("User") : cleanText(rawName);
    const QString message = cleanText(rawText);
    if (message.isEmpty()) return;
    QVariantMap item{{QStringLiteral("name"), name},
                     {QStringLiteral("text"), message},
                     {QStringLiteral("platform"), platform},
                     {QStringLiteral("time"), QDateTime::currentDateTime().toString(QStringLiteral("HH:mm:ss"))}};
    m_messages.append(item);
    while (m_messages.size() > 24) m_messages.removeFirst();
    emit messagesChanged();
    processForSpeech(name, message);
}

void ChatController::processForSpeech(const QString &name, const QString &rawText)
{
    if (m_blacklist.contains(name.toCaseFolded())) return;
    QString text = cleanText(rawText);
    const QString prefix = m_character.isEmpty() ? m_commandPrefix : m_character;
    if (!prefix.isEmpty()) {
        if (!text.startsWith(prefix)) return;
        text = text.mid(prefix.size()).trimmed();
    }
    if (text.isEmpty()) return;
    QString speaker = name;
    speaker.replace('_', ' ');
    speaker.replace('@', QStringLiteral(" arroba "));
    speaker.replace(QRegularExpression(QStringLiteral(" +")), QStringLiteral(" "));
    const QString speech = QStringLiteral("%1 dice: %2").arg(speaker, text);
    if (m_speechQueue.size() >= MaxSpeechQueue)
        m_speechQueue.dequeue();
    m_speechQueue.enqueue(speech);
    emit speakingChanged();
    speakNext();
}

void ChatController::speakNext()
{
    if (m_speaking || m_speechQueue.isEmpty()) return;
    m_speaking = true;
    emit speakingChanged();
    requestSpeech(m_speechQueue.dequeue());
}

void ChatController::requestSpeech(const QString &text)
{
    if (m_sessionId.isEmpty() || m_voice.isEmpty()) {
        setStatus(QStringLiteral("TTS"), QStringLiteral("Falta TikTok Session ID o voz; el chat sigue recibiendo mensajes."));
        finishSpeech();
        return;
    }
    QString prepared = text;
    prepared.replace('+', QStringLiteral("plus"));
    prepared.replace('&', QStringLiteral("and"));
    QUrl url(QStringLiteral("https://api16-normal-v6.tiktokv.com/media/api/text/speech/invoke/"));
    QUrlQuery query;
    query.addQueryItem(QStringLiteral("text_speaker"), m_voice);
    query.addQueryItem(QStringLiteral("req_text"), prepared);
    query.addQueryItem(QStringLiteral("speaker_map_type"), QStringLiteral("0"));
    query.addQueryItem(QStringLiteral("aid"), QStringLiteral("1233"));
    url.setQuery(query);
    QNetworkRequest request{url};
    request.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("com.zhiliaoapp.musically/2022600030 (Linux; U; Android 7.1.2; es_ES; SM-G988N; Build/NRD90M;tt-ok/3.12.13.1)"));
    request.setRawHeader("Cookie", QByteArray("sessionid=") + m_sessionId.toUtf8());
    auto *reply = m_network.post(request, QByteArray());
    m_ttsReply = reply;
    const quint64 generation = m_speechGeneration;
    connect(reply, &QNetworkReply::finished, this, [this, reply, generation]() {
        if (m_ttsReply == reply) m_ttsReply = nullptr;
        const QByteArray response = reply->readAll();
        const QString error = reply->errorString();
        reply->deleteLater();
        if (generation != m_speechGeneration || m_stopping) return;
        const QJsonDocument document = QJsonDocument::fromJson(response);
        const QJsonObject root = document.object();
        if (root.value(QStringLiteral("status_code")).toInt(-1) != 0) {
            setStatus(QStringLiteral("TTS"), QStringLiteral("TikTok rechazó el audio: ") + root.value(QStringLiteral("status_msg")).toString(error));
            finishSpeech();
            return;
        }
        const QByteArray audio = QByteArray::fromBase64(root.value(QStringLiteral("data")).toObject()
                                                            .value(QStringLiteral("v_str")).toString().toLatin1());
        if (audio.isEmpty()) {
            setStatus(QStringLiteral("TTS"), QStringLiteral("TikTok no devolvió audio."));
            finishSpeech();
            return;
        }
        const QString path = QStandardPaths::writableLocation(QStandardPaths::TempLocation)
                             + QStringLiteral("/pavotts_%1.mp3").arg(QUuid::createUuid().toString(QUuid::WithoutBraces));
        QFile file(path);
        if (!file.open(QIODevice::WriteOnly)) {
            setStatus(QStringLiteral("TTS"), QStringLiteral("No se pudo guardar el audio temporal."));
            finishSpeech();
            return;
        }
        file.write(audio);
        file.close();
        m_currentAudioFile = path;
        m_player.setSource(QUrl::fromLocalFile(path));
        m_player.play();
    });
}

void ChatController::finishSpeech()
{
    if (!m_speaking) return;
    m_speaking = false;
    if (!m_currentAudioFile.isEmpty()) {
        QFile::remove(m_currentAudioFile);
        m_currentAudioFile.clear();
    }
    emit speakingChanged();
    speakNext();
}

void ChatController::testTts()
{
    if (m_speechQueue.size() >= MaxSpeechQueue) m_speechQueue.dequeue();
    m_speechQueue.enqueue(QStringLiteral("Hola, esta es una prueba usando la voz de TikTok."));
    emit speakingChanged();
    speakNext();
}

void ChatController::clearChat()
{
    m_messages.clear();
    emit messagesChanged();
}

void ChatController::addBlacklist(const QString &name)
{
    const QString normalized = cleanText(name).toCaseFolded();
    if (!normalized.isEmpty()) {
        m_blacklist.insert(normalized);
        emit settingsChanged();
        saveSettings();
    }
}

void ChatController::removeBlacklist(const QString &name)
{
    m_blacklist.remove(cleanText(name).toCaseFolded());
    emit settingsChanged();
    saveSettings();
}

void ChatController::clearBlacklist()
{
    m_blacklist.clear();
    emit settingsChanged();
    saveSettings();
}

void ChatController::clearSpeechQueue()
{
    m_speechQueue.clear();
    ++m_speechGeneration;
    if (m_ttsReply) {
        auto *reply = m_ttsReply;
        m_ttsReply = nullptr;
        reply->abort();
        reply->deleteLater();
    }
    m_player.stop();
    if (!m_currentAudioFile.isEmpty()) {
        QFile::remove(m_currentAudioFile);
        m_currentAudioFile.clear();
    }
    m_speaking = false;
    emit speakingChanged();
}

QString ChatController::oauthHelp() const
{
    return QStringLiteral("Creá un token OAuth de Twitch con el permiso chat:read. El usuario debe coincidir con la cuenta del token.");
}
