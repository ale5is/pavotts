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
#include <QRandomGenerator>
#include <QSslError>
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

QString cleanUserName(QString name)
{
    name = name.trimmed();
    return name.isEmpty() ? QStringLiteral("User") : name;
}

QJsonObject extractEmbeddedObject(const QString &html, const QStringList &markers)
{
    for (const QString &marker : markers) {
        const int markerAt = html.indexOf(marker);
        if (markerAt < 0)
            continue;
        const int start = html.indexOf(QLatin1Char('{'), markerAt + marker.size());
        if (start < 0)
            continue;
        int depth = 0;
        bool inString = false;
        bool escaped = false;
        for (int i = start; i < html.size(); ++i) {
            const QChar ch = html.at(i);
            if (inString) {
                if (escaped) {
                    escaped = false;
                } else if (ch == QLatin1Char('\\')) {
                    escaped = true;
                } else if (ch == QLatin1Char('"')) {
                    inString = false;
                }
                continue;
            }
            if (ch == QLatin1Char('"')) {
                inString = true;
            } else if (ch == QLatin1Char('{')) {
                ++depth;
            } else if (ch == QLatin1Char('}')) {
                --depth;
                if (depth == 0) {
                    const QJsonDocument document = QJsonDocument::fromJson(html.mid(start, i - start + 1).toUtf8());
                    if (document.isObject())
                        return document.object();
                    break;
                }
            }
        }
    }
    return {};
}

QJsonObject extractMergedEmbeddedObjects(const QString &html, const QStringList &markers)
{
    QJsonObject merged;
    for (const QString &marker : markers) {
        int searchFrom = 0;
        while (true) {
            const int markerAt = html.indexOf(marker, searchFrom);
            if (markerAt < 0)
                break;

            int start = markerAt + marker.size();
            while (start < html.size() && html.at(start).isSpace())
                ++start;
            if (start >= html.size() || html.at(start) != QLatin1Char('{')) {
                searchFrom = markerAt + marker.size();
                continue;
            }

            int depth = 0;
            bool inString = false;
            bool escaped = false;
            bool parsed = false;
            for (int i = start; i < html.size(); ++i) {
                const QChar ch = html.at(i);
                if (inString) {
                    if (escaped)
                        escaped = false;
                    else if (ch == QLatin1Char('\\'))
                        escaped = true;
                    else if (ch == QLatin1Char('"'))
                        inString = false;
                    continue;
                }
                if (ch == QLatin1Char('"')) {
                    inString = true;
                } else if (ch == QLatin1Char('{')) {
                    ++depth;
                } else if (ch == QLatin1Char('}')) {
                    --depth;
                    if (depth == 0) {
                        const QJsonDocument document =
                            QJsonDocument::fromJson(html.mid(start, i - start + 1).toUtf8());
                        if (document.isObject()) {
                            const QJsonObject part = document.object();
                            for (auto it = part.begin(); it != part.end(); ++it)
                                merged.insert(it.key(), it.value());
                        }
                        searchFrom = i + 1;
                        parsed = true;
                        break;
                    }
                }
            }
            if (!parsed)
                break;
        }
    }
    return merged;
}

QJsonObject findLiveChatRenderer(const QJsonValue &value)
{
    if (value.isObject()) {
        const QJsonObject root = value.toObject();
        const QJsonObject primary = root.value(QStringLiteral("contents")).toObject()
            .value(QStringLiteral("twoColumnWatchNextResults")).toObject()
            .value(QStringLiteral("conversationBar")).toObject()
            .value(QStringLiteral("liveChatRenderer")).toObject();
        if (primary.contains(QStringLiteral("continuations"))
            && !primary.value(QStringLiteral("isReplay")).toBool())
            return primary;

        for (auto it = root.begin(); it != root.end(); ++it) {
            if (it.key() == QStringLiteral("liveChatRenderer") && it.value().isObject()) {
                const QJsonObject renderer = it.value().toObject();
                if (renderer.contains(QStringLiteral("continuations"))
                    && !renderer.value(QStringLiteral("isReplay")).toBool())
                    return renderer;
            }
            const QJsonObject nested = findLiveChatRenderer(it.value());
            if (!nested.isEmpty())
                return nested;
        }
    } else if (value.isArray()) {
        for (const QJsonValue &child : value.toArray()) {
            const QJsonObject nested = findLiveChatRenderer(child);
            if (!nested.isEmpty())
                return nested;
        }
    }
    return {};
}

QString findLiveChatContinuation(const QJsonValue &rendererValue, int *pollingDelay = nullptr,
                                    bool preferTimed = false)
{
    if (pollingDelay)
        *pollingDelay = 0;
    if (!rendererValue.isObject())
        return {};
    const QJsonObject renderer = rendererValue.toObject();
    if (renderer.value(QStringLiteral("isReplay")).toBool())
        return {};
    const QJsonArray continuations = renderer.value(QStringLiteral("continuations")).toArray();
    const QStringList preferredTypes = preferTimed
        ? QStringList{QStringLiteral("timedContinuationData"),
                      QStringLiteral("invalidationContinuationData"),
                      QStringLiteral("liveChatReplayContinuationData"),
                      QStringLiteral("reloadContinuationData")}
        : QStringList{QStringLiteral("reloadContinuationData"),
                      QStringLiteral("timedContinuationData"),
                      QStringLiteral("invalidationContinuationData")};
    for (const QString &type : preferredTypes) {
        for (const QJsonValue &entry : continuations) {
            const QJsonObject data = entry.toObject().value(type).toObject();
            const QString token = data.value(QStringLiteral("continuation")).toString();
            if (token.isEmpty())
                continue;
            if (pollingDelay) {
                int timeout = data.value(QStringLiteral("timeoutMs")).toInt();
                if (timeout <= 0)
                    timeout = data.value(QStringLiteral("timeUntilLastMessageMsec")).toInt();
                if (timeout > 0)
                    *pollingDelay = qBound(1000, timeout, 60000);
            }
            return token;
        }
    }
    return {};
}

int findYouTubePollingDelay(const QJsonValue &value)
{
    if (value.isObject()) {
        const QJsonObject object = value.toObject();
        const QJsonValue timeout = object.value(QStringLiteral("timeoutMs"));
        if (timeout.isDouble()) {
            const int milliseconds = timeout.toInt();
            if (milliseconds > 0)
                return qBound(1000, milliseconds, 60000);
        }
        for (auto it = object.begin(); it != object.end(); ++it) {
            const int nested = findYouTubePollingDelay(it.value());
            if (nested > 0)
                return nested;
        }
    } else if (value.isArray()) {
        for (const QJsonValue &child : value.toArray()) {
            const int nested = findYouTubePollingDelay(child);
            if (nested > 0)
                return nested;
        }
    }
    return 0;
}


struct YouTubeProtoField {
    quint64 number = 0;
    quint8 wire = 0;
    quint64 varint = 0;
    QByteArray data;
};

bool readYouTubeVarint(const QByteArray &bytes, qsizetype &position, quint64 &value)
{
    value = 0;
    for (int index = 0; index < 10 && position < bytes.size(); ++index) {
        const quint8 byte = static_cast<quint8>(bytes.at(position++));
        if (index == 9 && byte > 1)
            return false;
        value |= static_cast<quint64>(byte & 0x7f) << (index * 7);
        if ((byte & 0x80) == 0)
            return true;
    }
    return false;
}

void appendYouTubeVarint(QByteArray &bytes, quint64 value)
{
    while (value >= 0x80) {
        bytes.append(static_cast<char>((value & 0x7f) | 0x80));
        value >>= 7;
    }
    bytes.append(static_cast<char>(value));
}

bool decodeYouTubeProto(const QByteArray &bytes, QList<YouTubeProtoField> &fields)
{
    qsizetype position = 0;
    while (position < bytes.size()) {
        quint64 tag = 0;
        if (!readYouTubeVarint(bytes, position, tag) || tag == 0)
            return false;

        YouTubeProtoField field;
        field.number = tag >> 3;
        field.wire = static_cast<quint8>(tag & 7);
        if (field.number == 0)
            return false;

        if (field.wire == 0) {
            if (!readYouTubeVarint(bytes, position, field.varint))
                return false;
        } else if (field.wire == 2) {
            quint64 length = 0;
            if (!readYouTubeVarint(bytes, position, length)
                || length > static_cast<quint64>(bytes.size() - position))
                return false;
            field.data = bytes.mid(position, static_cast<qsizetype>(length));
            position += static_cast<qsizetype>(length);
        } else if (field.wire == 1 || field.wire == 5) {
            const qsizetype length = field.wire == 1 ? 8 : 4;
            if (bytes.size() - position < length)
                return false;
            field.data = bytes.mid(position, length);
            position += length;
        } else {
            return false;
        }
        fields.append(field);
    }
    return true;
}

QByteArray encodeYouTubeProto(const QList<YouTubeProtoField> &fields)
{
    QByteArray encoded;
    for (const YouTubeProtoField &field : fields) {
        appendYouTubeVarint(encoded, (field.number << 3) | field.wire);
        if (field.wire == 0) {
            appendYouTubeVarint(encoded, field.varint);
        } else if (field.wire == 2) {
            appendYouTubeVarint(encoded, static_cast<quint64>(field.data.size()));
            encoded.append(field.data);
        } else {
            encoded.append(field.data);
        }
    }
    return encoded;
}

QString youtubeAllMessagesContinuation(const QString &continuation)
{
    if (continuation.isEmpty())
        return continuation;

    const QByteArray decoded = QByteArray::fromBase64(
        continuation.toLatin1(), QByteArray::Base64UrlEncoding);
    if (decoded.isEmpty())
        return continuation;

    QList<YouTubeProtoField> outer;
    if (!decodeYouTubeProto(decoded, outer))
        return continuation;

    bool changed = false;
    for (YouTubeProtoField &outerField : outer) {
        if (outerField.number != 119693434 || outerField.wire != 2)
            continue;

        QList<YouTubeProtoField> entity;
        if (!decodeYouTubeProto(outerField.data, entity))
            return continuation;

        for (YouTubeProtoField &entityField : entity) {
            if (entityField.number == 13 && entityField.wire == 0) {
                entityField.varint = 1;
                changed = true;
            } else if (entityField.number == 16 && entityField.wire == 2) {
                QList<YouTubeProtoField> chatType;
                if (!decodeYouTubeProto(entityField.data, chatType))
                    return continuation;
                for (YouTubeProtoField &chatTypeField : chatType) {
                    if (chatTypeField.number == 1 && chatTypeField.wire == 0) {
                        chatTypeField.varint = 1;
                        changed = true;
                    }
                }
                entityField.data = encodeYouTubeProto(chatType);
            }
        }
        outerField.data = encodeYouTubeProto(entity);
    }

    if (!changed)
        return continuation;

    return QString::fromLatin1(encodeYouTubeProto(outer).toBase64(
        QByteArray::Base64UrlEncoding | QByteArray::OmitTrailingEquals));
}

bool isYouTubeChannelId(const QString &value)
{
    return value.size() == 24 && value.startsWith(QStringLiteral("UC"));
}

QString findYouTubeChannelId(const QJsonValue &value)
{
    if (value.isObject()) {
        const QJsonObject object = value.toObject();
        const QJsonObject channelMetadata = object.value(QStringLiteral("channelMetadataRenderer")).toObject();
        const QString externalId = channelMetadata.value(QStringLiteral("externalId")).toString();
        if (isYouTubeChannelId(externalId))
            return externalId;

        const QJsonObject tabHeader = object.value(QStringLiteral("c4TabbedHeaderRenderer")).toObject();
        const QString tabChannelId = tabHeader.value(QStringLiteral("channelId")).toString();
        if (isYouTubeChannelId(tabChannelId))
            return tabChannelId;

        const QString directBrowseId = object.value(QStringLiteral("browseId")).toString();
        if (isYouTubeChannelId(directBrowseId))
            return directBrowseId;

        for (auto it = object.begin(); it != object.end(); ++it) {
            const QString nested = findYouTubeChannelId(it.value());
            if (!nested.isEmpty())
                return nested;
        }
    } else if (value.isArray()) {
        for (const QJsonValue &child : value.toArray()) {
            const QString nested = findYouTubeChannelId(child);
            if (!nested.isEmpty())
                return nested;
        }
    }
    return {};
}

QString findLiveVideoId(const QJsonValue &value, const QSet<QString> &excluded = QSet<QString>())
{
    if (value.isObject()) {
        const QJsonObject object = value.toObject();
        const QString rendererId = object.value(QStringLiteral("videoId")).toString();
        if (rendererId.size() == 11 && !excluded.contains(rendererId)) {
            for (const QJsonValue &overlay : object.value(QStringLiteral("thumbnailOverlays")).toArray()) {
                if (overlay.toObject().value(QStringLiteral("thumbnailOverlayTimeStatusRenderer")).toObject()
                        .value(QStringLiteral("style")).toString() == QStringLiteral("LIVE"))
                    return rendererId;
            }
        }

        const QString lockupId = object.value(QStringLiteral("contentId")).toString();
        if (lockupId.size() == 11 && !excluded.contains(lockupId)
            && object.value(QStringLiteral("contentType")).toString() == QStringLiteral("LOCKUP_CONTENT_TYPE_VIDEO")) {
            const QJsonArray overlays = object.value(QStringLiteral("contentImage")).toObject()
                .value(QStringLiteral("thumbnailViewModel")).toObject().value(QStringLiteral("overlays")).toArray();
            for (const QJsonValue &overlay : overlays) {
                for (const QJsonValue &badge : overlay.toObject()
                         .value(QStringLiteral("thumbnailBottomOverlayViewModel")).toObject()
                         .value(QStringLiteral("badges")).toArray()) {
                    if (badge.toObject().value(QStringLiteral("thumbnailBadgeViewModel")).toObject()
                            .value(QStringLiteral("badgeStyle")).toString()
                        == QStringLiteral("THUMBNAIL_OVERLAY_BADGE_STYLE_LIVE"))
                        return lockupId;
                }
            }
        }

        for (auto it = object.begin(); it != object.end(); ++it) {
            const QString nested = findLiveVideoId(it.value(), excluded);
            if (!nested.isEmpty())
                return nested;
        }
    } else if (value.isArray()) {
        for (const QJsonValue &child : value.toArray()) {
            const QString nested = findLiveVideoId(child, excluded);
            if (!nested.isEmpty())
                return nested;
        }
    }
    return {};
}

QString normalizeKickChannel(QString channel)
{
    channel = channel.trimmed();
    if (channel.startsWith(QStringLiteral("http://"), Qt::CaseInsensitive) ||
        channel.startsWith(QStringLiteral("https://"), Qt::CaseInsensitive)) {
        const QStringList parts = QUrl(channel).path().split(QLatin1Char('/'), Qt::SkipEmptyParts);
        channel = parts.isEmpty() ? QString() : parts.first();
    }
    if (channel.startsWith(QLatin1Char('@')))
        channel.remove(0, 1);
    return channel.trimmed();
}

QString correctPronunciation(QString text)
{
    text.replace('@', QStringLiteral(" arroba "));
    text.replace('#', QStringLiteral(" almohadilla "));
    text.replace('$', QStringLiteral(" dólar "));
    text.replace('%', QStringLiteral(" por ciento "));
    text.replace('_', ' ');
    const QList<QPair<QString, QString>> words = {
        {QStringLiteral("\\bRoblox\\b"), QStringLiteral("Ró-bloks")},
        {QStringLiteral("\\bMinecraft\\b"), QStringLiteral("Máin-cráft")},
        {QStringLiteral("\\bDiscord\\b"), QStringLiteral("Dis-córd")},
        {QStringLiteral("\\bYouTube\\b"), QStringLiteral("Yutub")}
    };
    for (const auto &word : words)
        text.replace(QRegularExpression(word.first, QRegularExpression::CaseInsensitiveOption), word.second);
    text.replace(QRegularExpression(QStringLiteral(" +")), QStringLiteral(" "));
    return text.trimmed();
}

QString findYouTubeVideoOwnerChannelId(const QJsonValue &value)
{
    if (value.isObject()) {
        const QJsonObject object = value.toObject();
        const QString detailsChannelId = object.value(QStringLiteral("videoDetails")).toObject()
                                             .value(QStringLiteral("channelId")).toString();
        if (isYouTubeChannelId(detailsChannelId))
            return detailsChannelId;
        const QJsonObject owner = object.value(QStringLiteral("videoOwnerRenderer")).toObject();
        if (!owner.isEmpty()) {
            const QString channelId = owner.value(QStringLiteral("navigationEndpoint")).toObject()
                                          .value(QStringLiteral("browseEndpoint")).toObject()
                                          .value(QStringLiteral("browseId")).toString();
            if (isYouTubeChannelId(channelId))
                return channelId;
        }
        for (auto it = object.begin(); it != object.end(); ++it) {
            const QString nested = findYouTubeVideoOwnerChannelId(it.value());
            if (!nested.isEmpty())
                return nested;
        }
    } else if (value.isArray()) {
        for (const QJsonValue &child : value.toArray()) {
            const QString nested = findYouTubeVideoOwnerChannelId(child);
            if (!nested.isEmpty())
                return nested;
        }
    }
    return {};
}

void collectYouTubeMessages(const QJsonValue &value, QList<QVariantMap> &messages)
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
            messages.append(QVariantMap{{QStringLiteral("id"), renderer.value(QStringLiteral("id")).toString()}, {QStringLiteral("timestampUsec"), renderer.value(QStringLiteral("timestampUsec")).toString()}, {QStringLiteral("name"), name}, {QStringLiteral("text"), message}});
    }
    for (auto it = object.begin(); it != object.end(); ++it)
        collectYouTubeMessages(it.value(), messages);
}
}

ChatController::ChatController(QObject *parent)
    : QObject(parent), m_player(this), m_audioOutput(this)
{
    m_player.setAudioOutput(&m_audioOutput);
    m_network.setTransferTimeout(120000);
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
    m_twitchRetryTimer.setSingleShot(true);
    connect(&m_twitchRetryTimer, &QTimer::timeout, this, [this]() {
        if (!m_stopping && !m_twitchRejected && !m_twitchSocket && !m_twitchChannel.isEmpty()) connectTwitch();
    });
    m_kickRetryTimer.setSingleShot(true);
    connect(&m_kickRetryTimer, &QTimer::timeout, this, [this]() {
        if (!m_stopping && !m_kickSocket && !m_kickChannel.isEmpty()) connectKick();
    });
    m_twitchHandshakeTimer.setSingleShot(true);
    connect(&m_twitchHandshakeTimer, &QTimer::timeout, this, [this]() {
        if (!m_stopping && m_twitchSocket && !m_twitchAuthenticated) {
            setStatus(QStringLiteral("Twitch"), QStringLiteral("No se confirmó el inicio de sesión en 15 segundos; reconectando."));
            m_twitchSocket->close();
        }
    });
    m_kickHeartbeatTimer.setInterval(150000);
    connect(&m_kickHeartbeatTimer, &QTimer::timeout, this, [this]() {
        if (!m_stopping && m_kickSocket && m_kickLastActivity.isValid()
            && m_kickLastActivity.elapsed() >= m_kickHeartbeatTimer.interval()) {
            m_kickHeartbeatTimer.stop();
            setStatus(QStringLiteral("Kick"), QStringLiteral("Se perdió el heartbeat de Centrifugo; reconectando."));
            m_kickSocket->close();
        }
    });
    m_kickHandshakeTimer.setSingleShot(true);
    connect(&m_kickHandshakeTimer, &QTimer::timeout, this, [this]() {
        if (!m_stopping && m_kickSocket && !m_kickSubscribed) {
            setStatus(QStringLiteral("Kick"), QStringLiteral("No se confirmó la suscripción en 20 segundos; reconectando."));
            m_kickSocket->close();
        }
    });
    m_kickLeaseTimer.setSingleShot(true);
    connect(&m_kickLeaseTimer, &QTimer::timeout, this, [this]() {
        if (!m_stopping && m_kickSocket) {
            setStatus(QStringLiteral("Kick"), QStringLiteral("Renovando el token temporal."));
            m_kickSocket->close();
        }
    });
    loadSettings();
}

ChatController::~ChatController()
{
    disconnectAll();
}

QString ChatController::status() const { return m_status; }
QString ChatController::twitchStatus() const { return m_twitchStatus; }
QString ChatController::kickStatus() const { return m_kickStatus; }
QString ChatController::youtubeStatus() const { return m_youtubeStatus; }
QVariantList ChatController::messages() const { return m_messages; }
QString ChatController::twitchChannel() const { return m_twitchChannel; }
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
    m_youtubeChannel = o.value("youtubeChannel").toString();
    m_kickChannel = o.value("kickChannel").toString();
    m_sessionId = o.value("sessionId").toString();
    m_voice = o.value("voz").toString("es_002");
    m_character = o.value("caracter").toString();
    m_commandPrefix = o.value("prefijo").toString();
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
    bool platformChanged = false;
    if (platform == QStringLiteral("Twitch") && m_twitchStatus != message) {
        m_twitchStatus = message;
        platformChanged = true;
    } else if (platform == QStringLiteral("Kick") && m_kickStatus != message) {
        m_kickStatus = message;
        platformChanged = true;
    } else if (platform == QStringLiteral("YouTube") && m_youtubeStatus != message) {
        m_youtubeStatus = message;
        platformChanged = true;
    }
    if (platformChanged)
        emit platformStatusesChanged();
}

void ChatController::setConnectedState()
{
    const bool now = !m_stopping && ((m_twitchSocket && m_twitchAuthenticated) || (m_kickSocket && m_kickSubscribed) || m_youtubeConnected);
    if (m_connected != now) {
        m_connected = now;
        emit connectionChanged();
    }
}

void ChatController::connectAll()
{
    disconnectAll();
    m_stopping = false;
    m_twitchRejected = false; m_twitchRetryDelay = 3000; m_kickRetryDelay = 3000; m_kickLookupAttempt = 0;
    m_kickChannelId.clear(); m_kickChatroomId.clear();
    m_messages.clear();
    emit messagesChanged();
    saveSettings();
    bool any = false;
    if (!m_twitchChannel.isEmpty()) {
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
    m_twitchRetryTimer.stop(); m_kickRetryTimer.stop();
    m_twitchHandshakeTimer.stop(); m_kickHandshakeTimer.stop(); m_kickLeaseTimer.stop(); m_kickHeartbeatTimer.stop();
    m_twitchAuthenticated = false; m_kickSubscribed = false; m_youtubeConnected = false;
    m_twitchBuffer.clear();
    setStatus(QStringLiteral("Twitch"), QStringLiteral("Desconectado."));
    setStatus(QStringLiteral("Kick"), QStringLiteral("Desconectado."));
    setStatus(QStringLiteral("YouTube"), QStringLiteral("Desconectado."));
    if (m_youtubeReply) {
        auto *reply = m_youtubeReply.data();
        m_youtubeReply = nullptr;
        reply->abort();
        reply->deleteLater();
    }
    if (m_ttsReply) {
        auto *reply = m_ttsReply.data();
        m_ttsReply = nullptr;
        reply->abort();
        reply->deleteLater();
    }
    if (m_twitchSocket) {
        auto *socket = m_twitchSocket.data();
        m_twitchSocket = nullptr;
        socket->close();
        socket->deleteLater();
    }
    if (m_kickSocket) {
        auto *socket = m_kickSocket.data();
        m_kickSocket = nullptr;
        socket->close();
        socket->deleteLater();
    }
    ++m_speechGeneration;
    m_player.stop();
    if (!m_currentAudioFile.isEmpty()) {
        QFile::remove(m_currentAudioFile);
        m_currentAudioFile.clear();
    }
    m_speechQueue.clear();
    m_speaking = false;
    emit speakingChanged();
    m_connected = false;
    emit connectionChanged();
    setStatus(QStringLiteral("Chat"), QStringLiteral("Desconectado."));
}

void ChatController::scheduleTwitchRetry(const QString &reason)
{
    if (m_stopping || m_twitchRejected || m_twitchRetryTimer.isActive() || m_twitchChannel.isEmpty())
        return;
    setStatus(QStringLiteral("Twitch"), reason + QStringLiteral(" Reintentando en %1 s.").arg(m_twitchRetryDelay / 1000));
    m_twitchRetryTimer.start(m_twitchRetryDelay);
    m_twitchRetryDelay = qMin(m_twitchRetryDelay * 2, 30000);
}

void ChatController::connectTwitch()
{
    m_twitchBuffer.clear();
    m_twitchRetryTimer.stop();
    m_twitchHandshakeTimer.stop();
    m_twitchAuthenticated = false;
    auto *socket = new QWebSocket(QString(), QWebSocketProtocol::VersionLatest, this);
    m_twitchSocket = socket;
    connect(socket, &QWebSocket::connected, this, [this, socket]() {
        if (m_twitchSocket != socket || m_stopping) return;
        setStatus(QStringLiteral("Twitch"), QStringLiteral("Conectado; entrando al chat anónimamente..."));
        const quint32 randomNick = QRandomGenerator::global()->bounded(100000000u, 999999999u);
        const QString anonymousNick = QStringLiteral("justinfan%1").arg(randomNick);
        socket->sendTextMessage(QStringLiteral("PASS SCHMOOPIIE\r\n"));
        socket->sendTextMessage(QStringLiteral("NICK %1\r\n").arg(anonymousNick));
        socket->sendTextMessage(QStringLiteral("CAP REQ :twitch.tv/tags twitch.tv/commands\r\n"));
        socket->sendTextMessage(QStringLiteral("JOIN #%1\r\n").arg(m_twitchChannel));
        m_twitchHandshakeTimer.start(15000);
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
        if (m_twitchSocket != socket) return;
        m_twitchSocket = nullptr;
        m_twitchAuthenticated = false;
        m_twitchHandshakeTimer.stop();
        setConnectedState();
        scheduleTwitchRetry(QStringLiteral("Conexión cerrada."));
    });
    connect(socket, &QWebSocket::sslErrors, this, [this, socket](const QList<QSslError> &errors) {
        if (m_twitchSocket != socket || m_stopping) return;
        QStringList details;
        for (const QSslError &error : errors) details.append(error.errorString());
        setStatus(QStringLiteral("Twitch"), QStringLiteral("Error TLS: ") + details.join(QStringLiteral("; ")));
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
        m_twitchRejected = true;
        m_twitchHandshakeTimer.stop();
        setStatus(QStringLiteral("Twitch"), QStringLiteral("Twitch rechazó la conexión anónima; el canal puede requerir autenticación o el acceso anónimo puede estar limitado."));
        socket->close();
        return;
    }
    if (line.contains(QStringLiteral(" 001 "))) {
        m_twitchAuthenticated = true;
        m_twitchHandshakeTimer.stop();
        m_twitchRetryDelay = 3000;
        setStatus(QStringLiteral("Twitch"), QStringLiteral("Conectado anónimamente; leyendo el chat."));
        setConnectedState();
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

void ChatController::scheduleKickRetry(const QString &reason)
{
    if (m_stopping || m_kickRetryTimer.isActive() || m_kickChannel.isEmpty())
        return;
    setStatus(QStringLiteral("Kick"), reason + QStringLiteral(" Reintentando en %1 s.").arg(m_kickRetryDelay / 1000));
    m_kickRetryTimer.start(m_kickRetryDelay);
    m_kickRetryDelay = qMin(m_kickRetryDelay * 2, 30000);
}

void ChatController::connectKick()
{
    m_kickRetryTimer.stop();
    m_kickHandshakeTimer.stop();
    m_kickLeaseTimer.stop();
    m_kickHeartbeatTimer.stop();
    m_kickSubscribed = false;
    m_kickConnectAcknowledged = false;
    m_kickChannelId.clear();
    m_kickChatroomId.clear();
    const quint64 generation = m_connectionGeneration;
    setStatus(QStringLiteral("Kick"), QStringLiteral("Resolviendo el canal..."));
    const QString kickSlug = normalizeKickChannel(m_kickChannel);
    if (kickSlug.isEmpty()) { scheduleKickRetry(QStringLiteral("Ingresá un nombre de canal Kick válido.")); return; }
    const QString encodedChannel = QString::fromLatin1(QUrl::toPercentEncoding(kickSlug));
    // Resolve both IDs from Kick's canonical channel response first; retain
    // LiteKickChat's direct chatroom and v1 endpoints as fallback.
    const QString lookupPath = m_kickLookupAttempt == 0
        ? QStringLiteral("/api/v2/channels/%1").arg(encodedChannel)
        : (m_kickLookupAttempt == 1
            ? QStringLiteral("/api/v2/channels/%1/chatroom").arg(encodedChannel)
            : QStringLiteral("/api/v1/channels/%1").arg(encodedChannel));
    QNetworkRequest request{QUrl(QStringLiteral("https://kick.com") + lookupPath)};
    request.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("Mozilla/5.0"));
    request.setRawHeader("Accept", "application/json");
    request.setRawHeader("Origin", "https://kick.com");
    request.setRawHeader("Referer", "https://kick.com/");
    auto *reply = m_network.get(request);
    connect(reply, &QNetworkReply::sslErrors, this, [this](const QList<QSslError> &errors) {
        QStringList details;
        for (const QSslError &error : errors) details.append(error.errorString());
        setStatus(QStringLiteral("Kick"), QStringLiteral("Error TLS: ") + details.join(QStringLiteral("; ")));
    });
    connect(reply, &QNetworkReply::finished, this, [this, reply, generation]() {
        if (m_stopping || generation != m_connectionGeneration) { reply->deleteLater(); return; }
        const QByteArray bytes = reply->readAll();
        const QString error = reply->errorString();
        const int code = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        const QString responseDetail = QString::fromUtf8(bytes.left(400)).simplified();
        const QJsonDocument doc = QJsonDocument::fromJson(bytes);
        reply->deleteLater();
        if (!doc.isObject() || code < 200 || code >= 300) {
            if (m_kickLookupAttempt < 2) {
                ++m_kickLookupAttempt;
                connectKick();
            } else {
                m_kickLookupAttempt = 0;
                scheduleKickRetry(QStringLiteral("No se pudo resolver el canal: %1 (HTTP %2). Respuesta: %3")
                                       .arg(error).arg(code).arg(responseDetail.isEmpty() ? QStringLiteral("sin cuerpo") : responseDetail));
            }
            return;
        }
        const QJsonObject lookup = doc.object();
        const bool directChatroomResponse = m_kickLookupAttempt == 1;
        const QJsonObject chatroom = lookup.value(QStringLiteral("chatroom")).toObject();
        const QString roomText = directChatroomResponse
            ? lookup.value(QStringLiteral("id")).toVariant().toString()
            : chatroom.value(QStringLiteral("id")).toVariant().toString();
        QString channelText = directChatroomResponse
            ? lookup.value(QStringLiteral("chatable_id")).toVariant().toString()
            : chatroom.value(QStringLiteral("chatable_id")).toVariant().toString();
        if (channelText.isEmpty() && !directChatroomResponse)
            channelText = lookup.value(QStringLiteral("id")).toVariant().toString();
        bool roomOk = false;
        const qlonglong room = roomText.toLongLong(&roomOk);
        if (!roomOk || room <= 0) {
            if (m_kickLookupAttempt < 2) {
                ++m_kickLookupAttempt;
                connectKick();
            } else {
                m_kickLookupAttempt = 0;
                scheduleKickRetry(QStringLiteral("Las rutas de Kick no devolvieron IDs numéricos válidos de canal y chatroom."));
            }
            return;
        }
        m_kickLookupAttempt = 0;
        m_kickChannelId = channelText;
        m_kickChatroomId = QString::number(room);
        setStatus(QStringLiteral("Kick"), QStringLiteral("Chatroom %1 resuelto; negociando conexión realtime...").arg(m_kickChatroomId));
        const QString clientId = QUuid::createUuid().toString(QUuid::WithoutBraces);
        const QJsonObject body{
            {QStringLiteral("client"), QJsonObject{{QStringLiteral("id"), clientId}, {QStringLiteral("type"), QStringLiteral("web")}}},
            {QStringLiteral("capabilities"), QJsonObject{{QStringLiteral("accepted_providers"), QJsonArray{QJsonObject{{QStringLiteral("provider"), QStringLiteral("pusher")}}, QJsonObject{{QStringLiteral("provider"), QStringLiteral("centrifugo")}}}}}}
        };
        const bool usingChannelNegotiation = !m_kickChannelId.isEmpty() && !m_kickUseGenericRealtime;
        const QString realtimePath = usingChannelNegotiation
            ? QStringLiteral("/api/v1/realtime/channels/%1/chat/connection").arg(m_kickChannelId)
            : QStringLiteral("/api/v1/realtime/connection");
        QNetworkRequest post{QUrl(QStringLiteral("https://web.kick.com") + realtimePath)};
        post.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
        post.setRawHeader("Origin", "https://kick.com");
        post.setRawHeader("Referer", "https://kick.com/");
        post.setRawHeader("Accept", "application/json");
        post.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("Mozilla/5.0"));
        auto *connection = m_network.post(post, QJsonDocument(body).toJson(QJsonDocument::Compact));
        connect(connection, &QNetworkReply::finished, this, [this, connection, clientId, generation, usingChannelNegotiation]() {
            if (m_stopping || generation != m_connectionGeneration) { connection->deleteLater(); return; }
            const QByteArray bytes = connection->readAll();
            const int code = connection->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
            const QString responseDetail = QString::fromUtf8(bytes.left(400)).simplified();
            connection->deleteLater();
            const QJsonDocument data = QJsonDocument::fromJson(bytes);
            if (!data.isObject() || code < 200 || code >= 300) {
                if (usingChannelNegotiation) {
                    m_kickUseGenericRealtime = true;
                    setStatus(QStringLiteral("Kick"), QStringLiteral("La negociación por canal falló (HTTP %1); probando negociación global de Kick.").arg(code));
                    connectKick();
                    return;
                }
                scheduleKickRetry(QStringLiteral("Falló la negociación realtime global (HTTP %1). Respuesta: %2")
                                      .arg(code).arg(responseDetail.isEmpty() ? QStringLiteral("sin cuerpo") : responseDetail));
                return;
            }
            QString wsUrl;
            QStringList offeredProviders;
            const QJsonArray list = data.object().value(QStringLiteral("data")).toObject().value(QStringLiteral("connections")).toArray();
            for (const QJsonValue &value : list) {
                const QJsonObject candidate = value.toObject();
                const QString provider = candidate.value(QStringLiteral("provider")).toString();
                if (!provider.isEmpty())
                    offeredProviders.append(provider);
                if (provider == QStringLiteral("centrifugo"))
                    wsUrl = candidate.value(QStringLiteral("credentials")).toObject().value(QStringLiteral("url")).toString();
            }
            const QUrl realtimeUrl(wsUrl);
            if (realtimeUrl.scheme() != QStringLiteral("wss") || realtimeUrl.host().isEmpty()) {
                if (usingChannelNegotiation) {
                    m_kickUseGenericRealtime = true;
                    setStatus(QStringLiteral("Kick"), QStringLiteral("La negociación por canal no ofreció Centrifugo (%1); probando negociación global.").arg(offeredProviders.join(QStringLiteral(", "))));
                    connectKick();
                    return;
                }
                scheduleKickRetry(QStringLiteral("Kick no ofreció un endpoint Centrifugo válido. Proveedores: %1. Respuesta: %2")
                                      .arg(offeredProviders.isEmpty() ? QStringLiteral("ninguno") : offeredProviders.join(QStringLiteral(", ")),
                                           responseDetail.isEmpty() ? QStringLiteral("sin cuerpo") : responseDetail));
                return;
            }
            setStatus(QStringLiteral("Kick"), QStringLiteral("Centrifugo seleccionado; solicitando token de invitado..."));
            QNetworkRequest auth{QUrl(QStringLiteral("https://web.kick.com/api/v1/realtime/auth/connection"))};
            auth.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
            auth.setRawHeader("Origin", "https://kick.com");
            auth.setRawHeader("Referer", "https://kick.com/");
            auth.setRawHeader("Accept", "application/json");
            auth.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("Mozilla/5.0"));
            const QByteArray authBody = QJsonDocument(QJsonObject{{QStringLiteral("client_id"), clientId}}).toJson(QJsonDocument::Compact);
            auto *tokenReply = m_network.post(auth, authBody);
            connect(tokenReply, &QNetworkReply::finished, this, [this, tokenReply, wsUrl, generation]() {
                if (m_stopping || generation != m_connectionGeneration) { tokenReply->deleteLater(); return; }
                const QByteArray bytes = tokenReply->readAll();
                const int code = tokenReply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
                const QString responseDetail = QString::fromUtf8(bytes.left(400)).simplified();
                tokenReply->deleteLater();
                const QJsonDocument tokenDoc = QJsonDocument::fromJson(bytes);
                const QString token = tokenDoc.object().value(QStringLiteral("data")).toObject().value(QStringLiteral("token")).toString();
                if (code < 200 || code >= 300 || token.isEmpty()) {
                    scheduleKickRetry(QStringLiteral("No se pudo obtener el token realtime (HTTP %1). Respuesta: %2")
                                          .arg(code).arg(responseDetail.isEmpty() ? QStringLiteral("sin cuerpo") : responseDetail));
                    return;
                }
                setStatus(QStringLiteral("Kick"), QStringLiteral("Token recibido; abriendo WebSocket de Centrifugo..."));
                auto *socket = new QWebSocket(QStringLiteral("https://kick.com"), QWebSocketProtocol::VersionLatest, this);
                m_kickSocket = socket;
                socket->setProperty("kickToken", token);
                connect(socket, &QWebSocket::connected, this, [this, socket, generation]() {
                    if (m_kickSocket != socket || m_stopping || generation != m_connectionGeneration) return;
                    m_kickLastActivity.start();
                    m_kickHeartbeatTimer.start();
                    const QJsonObject connectCommand{
                        {QStringLiteral("id"), 1},
                        {QStringLiteral("connect"), QJsonObject{
                            {QStringLiteral("token"), socket->property("kickToken").toString()},
                            {QStringLiteral("name"), QStringLiteral("js")}
                        }}
                    };
                    socket->sendTextMessage(QString::fromUtf8(
                        QJsonDocument(connectCommand).toJson(QJsonDocument::Compact)));
                    setStatus(QStringLiteral("Kick"), QStringLiteral("Autenticando realtime..."));
                    m_kickHandshakeTimer.start(20000);
                });
                connect(socket, &QWebSocket::textMessageReceived, this, [this, socket, generation](const QString &text) {
                    if (m_kickSocket != socket || m_stopping || generation != m_connectionGeneration) return;
                    m_kickLastActivity.restart();
                    const QList<QByteArray> lines = text.toUtf8().split('\n');
                    for (const QByteArray &line : lines) {
                        if (line.trimmed().isEmpty())
                            continue;
                        const QJsonDocument frame = QJsonDocument::fromJson(line.trimmed());
                        if (frame.isObject()) {
                            handleKickFrame(frame.object(), socket);
                        } else if (frame.isArray()) {
                            for (const QJsonValue &entry : frame.array()) {
                                if (entry.isObject())
                                    handleKickFrame(entry.toObject(), socket);
                            }
                        }
                    }
                });
                connect(socket, &QWebSocket::disconnected, this, [this, socket, generation]() {
                    if (m_kickSocket == socket && generation == m_connectionGeneration) {
                        m_kickSocket = nullptr;
                        m_kickSubscribed = false;
                        m_kickHandshakeTimer.stop();
                        m_kickLeaseTimer.stop();
                        m_kickHeartbeatTimer.stop();
                        setConnectedState();
                        const QString closeDetail = QStringLiteral("WebSocket cerrado (código %1): %2")
                            .arg(static_cast<int>(socket->closeCode()))
                            .arg(socket->closeReason().isEmpty() ? QStringLiteral("sin motivo informado") : socket->closeReason().left(180));
                        scheduleKickRetry(closeDetail);
                    }
                });
                connect(socket, &QWebSocket::sslErrors, this, [this, socket, generation](const QList<QSslError> &errors) {
                    if (m_kickSocket != socket || m_stopping || generation != m_connectionGeneration) return;
                    QStringList details;
                    for (const QSslError &error : errors) details.append(error.errorString());
                    setStatus(QStringLiteral("Kick"), QStringLiteral("Error TLS: ") + details.join(QStringLiteral("; ")));
                });
                connect(socket, &QWebSocket::errorOccurred, this, [this, socket, generation](QAbstractSocket::SocketError) {
                    if (m_kickSocket != socket || m_stopping || generation != m_connectionGeneration)
                        return;
                    const QString error = socket->errorString();
                    setStatus(QStringLiteral("Kick"), QStringLiteral("Error WebSocket: ") + error);
                    m_kickSocket = nullptr;
                    m_kickSubscribed = false;
                    m_kickHandshakeTimer.stop();
                    m_kickLeaseTimer.stop();
                    m_kickHeartbeatTimer.stop();
                    setConnectedState();
                    socket->abort();
                    socket->deleteLater();
                    scheduleKickRetry(QStringLiteral("Falló el WebSocket: ") + error);
                });
                QNetworkRequest wsRequest{QUrl(wsUrl)};
                wsRequest.setRawHeader("User-Agent", "Mozilla/5.0");
                socket->open(wsRequest);
            });
        });
    });
}

void ChatController::handleKickFrame(const QJsonObject &frame, QWebSocket *socket)
{
    if (m_kickSocket != socket || m_stopping) return;
    // Kick Centrifugo heartbeats are empty JSON objects; pong=true requires {}.
    if (frame.isEmpty()) {
        socket->sendTextMessage(QStringLiteral("{}"));
        return;
    }
    if (frame.contains(QStringLiteral("error"))) {
        const QJsonObject error = frame.value(QStringLiteral("error")).toObject();
        const QString message = error.value(QStringLiteral("message")).toString(
            error.value(QStringLiteral("reason")).toString(QStringLiteral("error sin descripción")));
        const QString code = error.value(QStringLiteral("code")).toVariant().toString();
        setStatus(QStringLiteral("Kick"), QStringLiteral("Error Centrifugo %1: %2")
                      .arg(code.isEmpty() ? QStringLiteral("(sin código)") : code, message.left(240)));
        socket->close();
        return;
    }
    if (frame.value(QStringLiteral("id")).toInt() == 1 && frame.contains(QStringLiteral("connect"))) {
        m_kickConnectAcknowledged = true;
        const QJsonObject connectInfo = frame.value(QStringLiteral("connect")).toObject();
        const int pingSeconds = connectInfo.value(QStringLiteral("ping")).toInt();
        const int heartbeatTimeout = pingSeconds > 0
            ? qBound(15000, pingSeconds * 1000 + 15000, 600000)
            : 150000;
        const int ttlSeconds = connectInfo.value(QStringLiteral("ttl")).toInt();
        if (connectInfo.value(QStringLiteral("expires")).toBool() && ttlSeconds > 0)
            m_kickLeaseTimeoutMs = qBound(60000, ttlSeconds * 750, 45 * 60 * 1000);
        else
            m_kickLeaseTimeoutMs = 35 * 60 * 1000;
        m_kickLastActivity.restart();
        m_kickHeartbeatTimer.setInterval(heartbeatTimeout);
        m_kickHeartbeatTimer.start();
        const QJsonObject subscribeCommand{
            {QStringLiteral("id"), 2},
            {QStringLiteral("subscribe"), QJsonObject{
                {QStringLiteral("channel"), QStringLiteral("chatrooms.%1.v2").arg(m_kickChatroomId)}
            }}
        };
        socket->sendTextMessage(QString::fromUtf8(
            QJsonDocument(subscribeCommand).toJson(QJsonDocument::Compact)));
        setStatus(QStringLiteral("Kick"), QStringLiteral("Autenticado; suscribiendo al chat..."));
        return;
    }
    if (frame.value(QStringLiteral("id")).toInt() == 2 && frame.contains(QStringLiteral("subscribe"))) {
        m_kickSubscribed = true;
        m_kickHandshakeTimer.stop();
        m_kickRetryDelay = 3000;
        m_kickUseGenericRealtime = false;
        m_kickLeaseTimer.start(m_kickLeaseTimeoutMs);
        setStatus(QStringLiteral("Kick"), QStringLiteral("Chat conectado."));
        setConnectedState();
        return;
    }
    const QJsonObject push = frame.value(QStringLiteral("push")).toObject();
    if (push.contains(QStringLiteral("ping"))) {
        socket->sendTextMessage(QStringLiteral("{}"));
        return;
    }
    if (push.contains(QStringLiteral("disconnect"))) {
        const QJsonObject disconnect = push.value(QStringLiteral("disconnect")).toObject();
        const QString code = disconnect.value(QStringLiteral("code")).toVariant().toString();
        const QString message = disconnect.value(QStringLiteral("message")).toString();
        const QString phase = !m_kickConnectAcknowledged
            ? QStringLiteral("antes de confirmar connect")
            : (!m_kickSubscribed ? QStringLiteral("después de connect y durante subscribe")
                                 : QStringLiteral("con la suscripción activa"));
        const QString diagnostic = code == QStringLiteral("3501")
            ? QStringLiteral("; Centrifugo 3501 significa que rechazó un frame del protocolo JSON (%1)").arg(phase)
            : QString();
        setStatus(QStringLiteral("Kick"), QStringLiteral("Centrifugo cerró la conexión (%1): %2%3")
                      .arg(code.isEmpty() ? QStringLiteral("sin código") : code,
                           message.isEmpty() ? QStringLiteral("sin descripción") : message.left(200), diagnostic));
        socket->close();
        return;
    }
    if (push.contains(QStringLiteral("unsubscribe"))) {
        const QJsonObject unsubscribe = push.value(QStringLiteral("unsubscribe")).toObject();
        const QString code = unsubscribe.value(QStringLiteral("code")).toVariant().toString();
        const QString message = unsubscribe.value(QStringLiteral("message")).toString();
        setStatus(QStringLiteral("Kick"), QStringLiteral("Centrifugo canceló la suscripción (código %1): %2")
                      .arg(code.isEmpty() ? QStringLiteral("sin código") : code,
                           message.isEmpty() ? QStringLiteral("sin descripción") : message.left(200)));
        socket->close();
        return;
    }
    const QJsonObject publication = push.value(QStringLiteral("pub")).toObject();
    QJsonObject envelope = publication.value(QStringLiteral("data")).toObject();
    if (envelope.isEmpty() && publication.value(QStringLiteral("data")).isString()) {
        const QJsonDocument nested = QJsonDocument::fromJson(publication.value(QStringLiteral("data")).toString().toUtf8());
        envelope = nested.object();
    }
    const QString eventName = envelope.value(QStringLiteral("event")).toString();
    QJsonObject data = envelope.value(QStringLiteral("data")).toObject();
    if (data.isEmpty() && envelope.value(QStringLiteral("data")).isString()) {
        const QJsonDocument nested = QJsonDocument::fromJson(envelope.value(QStringLiteral("data")).toString().toUtf8());
        data = nested.object();
    }
    const bool knownChatEvent = eventName.contains(QStringLiteral("ChatMessage"), Qt::CaseInsensitive);
    const bool chatPayload = data.contains(QStringLiteral("content")) && data.value(QStringLiteral("sender")).isObject();
    if (!knownChatEvent && !chatPayload)
        return;
    const QJsonObject sender = data.value(QStringLiteral("sender")).toObject();
    const QString message = data.value(QStringLiteral("content")).toString().trimmed();
    QString name = sender.value(QStringLiteral("username")).toString();
    if (name.isEmpty())
        name = sender.value(QStringLiteral("slug")).toString();
    if (name.isEmpty())
        name = QStringLiteral("Kick");
    if (!message.isEmpty() && message.size() <= 8192)
        publishMessage(name, message, QStringLiteral("Kick"));
}

void ChatController::connectYouTube()
{
    stopYouTube();
    setConnectedState();
    m_youtubeSeenIds.clear();
    m_youtubeSeenOrder.clear();
    m_youtubeRejectedVideoIds.clear();
    m_youtubeBaselineReady = false;
    m_youtubeConnectedAtUsec = 0;
    m_youtubeResolvedChannelId.clear();
    m_youtubeResolvedHandle.clear();

    QString channel = m_youtubeChannel.trimmed();
    if (channel.startsWith(QStringLiteral("http://"), Qt::CaseInsensitive) ||
        channel.startsWith(QStringLiteral("https://"), Qt::CaseInsensitive)) {
        const QStringList parts = QUrl(channel).path().split(QLatin1Char('/'), Qt::SkipEmptyParts);
        channel.clear();
        for (const QString &part : parts) {
            if (part.startsWith(QLatin1Char('@')) || part.startsWith(QStringLiteral("UC"))) {
                channel = part;
                break;
            }
        }
    }
    if (channel.isEmpty()) {
        setStatus(QStringLiteral("YouTube"), QStringLiteral("Ingresá un canal, @handle o URL de YouTube válida."));
        return;
    }
    if (channel.startsWith(QStringLiteral("UC")))
        channel.prepend(QStringLiteral("channel/"));
    else if (!channel.startsWith(QLatin1Char('@')))
        channel.prepend(QLatin1Char('@'));

    m_youtubeResolvedChannelId = channel.startsWith(QStringLiteral("channel/"))
        ? channel.mid(QStringLiteral("channel/").size()) : QString();
    m_youtubeResolvedHandle = channel.startsWith(QLatin1Char('@')) ? channel.mid(1) : QString();

    const QUrl pageUrl(QStringLiteral("https://www.youtube.com/") + channel);
    setStatus(QStringLiteral("YouTube"), QStringLiteral("Buscando transmisión y chat en vivo..."));
    QNetworkRequest pageRequest(pageUrl);
    pageRequest.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131 Safari/537.36"));
    pageRequest.setRawHeader("Accept-Language", "es-AR,es;q=0.9,en;q=0.8");
    pageRequest.setRawHeader("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
    auto *reply = m_network.get(pageRequest);
    connect(reply, &QNetworkReply::sslErrors, this, [this](const QList<QSslError> &errors) {
        QStringList details;
        for (const QSslError &error : errors) details.append(error.errorString());
        setStatus(QStringLiteral("YouTube"), QStringLiteral("Error TLS: ") + details.join(QStringLiteral("; ")));
    });
    m_youtubeReply = reply;
    connect(reply, &QNetworkReply::finished, this, [this, reply, channel]() {
        if (m_stopping || m_youtubeReply != reply) { reply->deleteLater(); return; }
        const QByteArray response = reply->readAll();
        const QString error = reply->errorString();
        const int code = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        reply->deleteLater();
        m_youtubeReply = nullptr;

        if (code < 200 || code >= 400 || response.isEmpty()) {
            const QString detail = QString::fromUtf8(response.left(300)).simplified();
            setStatus(QStringLiteral("YouTube"), QStringLiteral("No se pudo abrir el canal (HTTP %1): %2. Respuesta: %3")
                          .arg(code).arg(error).arg(detail.isEmpty() ? QStringLiteral("sin cuerpo") : detail));
            m_youtubeTimer.start(30000);
            return;
        }

        const QString html = QString::fromUtf8(response);
        const QJsonObject initialData = extractEmbeddedObject(html, {
            QStringLiteral("var ytInitialData = "),
            QStringLiteral("ytInitialData = "),
            QStringLiteral("window[\"ytInitialData\"] = "),
            QStringLiteral("window.ytInitialData = ")
        });
        const QJsonObject ytcfg = extractMergedEmbeddedObjects(html, {
            QStringLiteral("ytcfg.set("),
            QStringLiteral("ytcfg.set ("),
            QStringLiteral("ytcfg.data_ = ")
        });

        m_youtubeApiKey = ytcfg.value(QStringLiteral("INNERTUBE_API_KEY")).toString();
        m_youtubeContext = ytcfg.value(QStringLiteral("INNERTUBE_CONTEXT")).toObject();
        m_youtubeClientVersion = ytcfg.value(QStringLiteral("INNERTUBE_CONTEXT_CLIENT_VERSION")).toString();
        if (m_youtubeClientVersion.isEmpty())
            m_youtubeClientVersion = m_youtubeContext.value(QStringLiteral("client")).toObject()
                                        .value(QStringLiteral("clientVersion")).toString();

        const QJsonObject exactHeader = initialData.value(QStringLiteral("header")).toObject()
                                            .value(QStringLiteral("c4TabbedHeaderRenderer")).toObject();
        const QString pageChannelId = exactHeader.value(QStringLiteral("channelId")).toString();
        if (m_youtubeResolvedChannelId.isEmpty() && isYouTubeChannelId(pageChannelId))
            m_youtubeResolvedChannelId = pageChannelId;

        if (m_youtubeApiKey.isEmpty() || m_youtubeContext.isEmpty()) {
            setStatus(QStringLiteral("YouTube"), QStringLiteral("No se pudieron obtener la configuración InnerTube de la página."));
            m_youtubeTimer.start(30000);
            return;
        }

        if (!m_youtubeResolvedChannelId.isEmpty()) {
            setStatus(QStringLiteral("YouTube"), QStringLiteral("Canal identificado; buscando transmisiones exclusivas de ese canal..."));
            requestYouTubeChannelStreams(m_youtubeResolvedChannelId);
            return;
        }
        if (channel.startsWith(QLatin1Char('@'))) {
            setStatus(QStringLiteral("YouTube"), QStringLiteral("Resolviendo el identificador real de @%1...").arg(channel.mid(1)));
            requestYouTubeResolveChannel(channel.mid(1));
            return;
        }
        setStatus(QStringLiteral("YouTube"), QStringLiteral("La página no confirmó el ID del canal; no se usará un video recomendado de otro canal."));
        m_youtubeTimer.start(30000);
    });
}


void ChatController::requestYouTubeResolveChannel(const QString &handle)
{
    if (m_stopping || handle.isEmpty())
        return;

    QUrl endpoint(QStringLiteral("https://www.youtube.com/youtubei/v1/navigation/resolve_url"));
    QUrlQuery query;
    query.addQueryItem(QStringLiteral("key"), m_youtubeApiKey);
    query.addQueryItem(QStringLiteral("prettyPrint"), QStringLiteral("false"));
    endpoint.setQuery(query);

    const QJsonObject body{
        {QStringLiteral("context"), m_youtubeContext},
        {QStringLiteral("url"), QStringLiteral("https://www.youtube.com/@") + handle}
    };
    QNetworkRequest request{endpoint};
    request.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
    request.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/122.0.0.0 Safari/537.36"));
    request.setRawHeader("Origin", "https://www.youtube.com");
    request.setRawHeader("Referer", (QStringLiteral("https://www.youtube.com/@") + handle).toUtf8());
    request.setRawHeader("X-YouTube-Client-Name", "1");
    request.setRawHeader("X-YouTube-Client-Version", m_youtubeClientVersion.toUtf8());
    const QString visitorData = m_youtubeContext.value(QStringLiteral("client")).toObject()
                                    .value(QStringLiteral("visitorData")).toString();
    if (!visitorData.isEmpty())
        request.setRawHeader("X-Goog-Visitor-Id", visitorData.toUtf8());

    auto *reply = m_network.post(request, QJsonDocument(body).toJson(QJsonDocument::Compact));
    m_youtubeReply = reply;
    connect(reply, &QNetworkReply::finished, this, [this, reply, handle]() {
        if (m_stopping || m_youtubeReply != reply) {
            reply->deleteLater();
            return;
        }
        const QByteArray bytes = reply->readAll();
        const QString error = reply->errorString();
        const int code = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        reply->deleteLater();
        m_youtubeReply = nullptr;

        const QJsonDocument document = QJsonDocument::fromJson(bytes);
        if (code < 200 || code >= 300 || !document.isObject()) {
            const QString detail = QString::fromUtf8(bytes.left(400)).simplified();
            setStatus(QStringLiteral("YouTube"), QStringLiteral("No se pudo resolver el handle @%1 (HTTP %2): %3. Respuesta: %4")
                          .arg(handle).arg(code).arg(error)
                          .arg(detail.isEmpty() ? QStringLiteral("sin cuerpo") : detail));
            m_youtubeTimer.start(15000);
            return;
        }

        const QJsonObject root = document.object();
        const QString channelId = root.value(QStringLiteral("endpoint")).toObject()
                                     .value(QStringLiteral("browseEndpoint")).toObject()
                                     .value(QStringLiteral("browseId")).toString();
        if (!isYouTubeChannelId(channelId)) {
            setStatus(QStringLiteral("YouTube"), QStringLiteral("InnerTube no devolvió un browseId de canal válido para @%1.").arg(handle));
            m_youtubeTimer.start(30000);
            return;
        }

        QJsonObject clientContext = m_youtubeContext.value(QStringLiteral("client")).toObject();
        const QString responseVisitor = root.value(QStringLiteral("responseContext")).toObject()
                                            .value(QStringLiteral("visitorData")).toString();
        if (!responseVisitor.isEmpty()) {
            clientContext.insert(QStringLiteral("visitorData"), responseVisitor);
            m_youtubeContext.insert(QStringLiteral("client"), clientContext);
        }
        m_youtubeResolvedChannelId = channelId;
        setStatus(QStringLiteral("YouTube"), QStringLiteral("Canal @%1 resuelto; buscando sus transmisiones...").arg(handle));
        requestYouTubeChannelStreams(channelId);
    });
}

void ChatController::requestYouTubeChannelStreams(const QString &channelId)
{
    if (m_stopping || channelId.isEmpty())
        return;
    m_youtubeResolvedChannelId = channelId;

    QUrl endpoint(QStringLiteral("https://www.youtube.com/youtubei/v1/browse"));
    QUrlQuery query;
    query.addQueryItem(QStringLiteral("key"), m_youtubeApiKey);
    query.addQueryItem(QStringLiteral("prettyPrint"), QStringLiteral("false"));
    endpoint.setQuery(query);

    const QJsonObject body{
        {QStringLiteral("context"), m_youtubeContext},
        {QStringLiteral("browseId"), channelId},
        {QStringLiteral("params"), QStringLiteral("EgdzdHJlYW1z8gYECgJ6AA%3D%3D")}
    };
    QNetworkRequest request{endpoint};
    request.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
    request.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 Chrome/122.0.0.0 Safari/537.36"));
    request.setRawHeader("Origin", "https://www.youtube.com");
    request.setRawHeader("Referer", QStringLiteral("https://www.youtube.com/channel/").append(channelId).toUtf8());
    request.setRawHeader("X-YouTube-Client-Name", "1");
    request.setRawHeader("X-YouTube-Client-Version", m_youtubeClientVersion.toUtf8());
    const QString visitorData = m_youtubeContext.value(QStringLiteral("client")).toObject()
                                    .value(QStringLiteral("visitorData")).toString();
    if (!visitorData.isEmpty())
        request.setRawHeader("X-Goog-Visitor-Id", visitorData.toUtf8());

    auto *reply = m_network.post(request, QJsonDocument(body).toJson(QJsonDocument::Compact));
    m_youtubeReply = reply;
    connect(reply, &QNetworkReply::finished, this, [this, reply, channelId]() {
        if (m_stopping || m_youtubeReply != reply) {
            reply->deleteLater();
            return;
        }
        const QByteArray bytes = reply->readAll();
        const QString error = reply->errorString();
        const int code = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        reply->deleteLater();
        m_youtubeReply = nullptr;

        const QJsonDocument document = QJsonDocument::fromJson(bytes);
        if (code < 200 || code >= 300 || !document.isObject()) {
            const QString detail = QString::fromUtf8(bytes.left(400)).simplified();
            setStatus(QStringLiteral("YouTube"), QStringLiteral("No se pudo consultar la pestaña de directos (HTTP %1): %2. Respuesta: %3")
                          .arg(code).arg(error).arg(detail.isEmpty() ? QStringLiteral("sin cuerpo") : detail));
            m_youtubeTimer.start(15000);
            return;
        }

        const QJsonObject root = document.object();
        QJsonObject clientContext = m_youtubeContext.value(QStringLiteral("client")).toObject();
        const QString responseVisitor = root.value(QStringLiteral("responseContext")).toObject()
                                            .value(QStringLiteral("visitorData")).toString();
        if (!responseVisitor.isEmpty()) {
            clientContext.insert(QStringLiteral("visitorData"), responseVisitor);
            m_youtubeContext.insert(QStringLiteral("client"), clientContext);
        }

        const QString videoId = findLiveVideoId(root, m_youtubeRejectedVideoIds);
        if (!videoId.isEmpty()) {
            setStatus(QStringLiteral("YouTube"), QStringLiteral("Directo detectado desde la pestaña de transmisiones (%1); solicitando el chat...").arg(videoId));
            requestYouTubeInitialContinuation(videoId, true);
            return;
        }

        setStatus(QStringLiteral("YouTube"), QStringLiteral("La pestaña de transmisiones del canal no devolvió otro video LIVE válido; se mantiene el canal seleccionado."));
        m_youtubeTimer.start(30000);
        setStatus(QStringLiteral("YouTube"), QStringLiteral("La pestaña de directos no devolvió ningún video con distintivo LIVE para el canal %1.").arg(channelId));
        m_youtubeTimer.start(30000);
    });
}

void ChatController::requestYouTubeInitialContinuation(const QString &videoId, bool allowChannelFallback)
{
    if (m_stopping || videoId.isEmpty())
        return;

    QUrl endpoint(QStringLiteral("https://www.youtube.com/youtubei/v1/next"));
    QUrlQuery query;
    query.addQueryItem(QStringLiteral("key"), m_youtubeApiKey);
    query.addQueryItem(QStringLiteral("prettyPrint"), QStringLiteral("false"));
    endpoint.setQuery(query);

    const QJsonObject body{
        {QStringLiteral("context"), m_youtubeContext},
        {QStringLiteral("videoId"), videoId}
    };
    QNetworkRequest request{endpoint};
    request.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
    request.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131 Safari/537.36"));
    request.setRawHeader("Origin", "https://www.youtube.com");
    request.setRawHeader("Referer", QStringLiteral("https://www.youtube.com/watch?v=") .append(videoId).toUtf8());
    request.setRawHeader("X-YouTube-Client-Name", "1");
    request.setRawHeader("X-YouTube-Client-Version", m_youtubeClientVersion.toUtf8());
    const QString visitorData = m_youtubeContext.value(QStringLiteral("client")).toObject()
                                    .value(QStringLiteral("visitorData")).toString();
    if (!visitorData.isEmpty())
        request.setRawHeader("X-Goog-Visitor-Id", visitorData.toUtf8());

    auto *reply = m_network.post(request, QJsonDocument(body).toJson(QJsonDocument::Compact));
    m_youtubeReply = reply;
    connect(reply, &QNetworkReply::finished, this, [this, reply, videoId, allowChannelFallback]() {
        if (m_stopping || m_youtubeReply != reply) {
            reply->deleteLater();
            return;
        }
        const QByteArray bytes = reply->readAll();
        const QString error = reply->errorString();
        const int code = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        reply->deleteLater();
        m_youtubeReply = nullptr;

        const QJsonDocument document = QJsonDocument::fromJson(bytes);
        if (code < 200 || code >= 300 || !document.isObject()) {
            const QString detail = QString::fromUtf8(bytes.left(400)).simplified();
            if (allowChannelFallback && (!m_youtubeResolvedChannelId.isEmpty() || !m_youtubeResolvedHandle.isEmpty())) {
                m_youtubeRejectedVideoIds.insert(videoId);
                setStatus(QStringLiteral("YouTube"), QStringLiteral("/next no pudo abrir el chat; volviendo a descubrir las transmisiones del canal."));
                if (!m_youtubeResolvedChannelId.isEmpty())
                    requestYouTubeChannelStreams(m_youtubeResolvedChannelId);
                else
                    requestYouTubeResolveChannel(m_youtubeResolvedHandle);
                return;
            }
            setStatus(QStringLiteral("YouTube"), QStringLiteral("No se pudo obtener la continuación desde /next (HTTP %1): %2. Respuesta: %3")
                          .arg(code).arg(error).arg(detail.isEmpty() ? QStringLiteral("sin cuerpo") : detail));
            m_youtubeTimer.start(15000);
            return;
        }

        const QJsonObject root = document.object();
        QJsonObject clientContext = m_youtubeContext.value(QStringLiteral("client")).toObject();
        const QString responseVisitor = root.value(QStringLiteral("responseContext")).toObject()
                                            .value(QStringLiteral("visitorData")).toString();
        if (!responseVisitor.isEmpty()) {
            clientContext.insert(QStringLiteral("visitorData"), responseVisitor);
            m_youtubeContext.insert(QStringLiteral("client"), clientContext);
        }
        const QString ownerChannelId = findYouTubeVideoOwnerChannelId(root);
        if (m_youtubeResolvedChannelId.isEmpty() || ownerChannelId.isEmpty()
            || ownerChannelId != m_youtubeResolvedChannelId) {
            m_youtubeRejectedVideoIds.insert(videoId);
            if (allowChannelFallback && !m_youtubeResolvedChannelId.isEmpty()) {
                const QString reason = ownerChannelId.isEmpty()
                    ? QStringLiteral("YouTube no confirmó el dueño del video")
                    : QStringLiteral("YouTube devolvió un video de otro canal (%1)").arg(ownerChannelId);
                setStatus(QStringLiteral("YouTube"), reason + QStringLiteral("; descartándolo y probando otro directo del canal configurado."));
                requestYouTubeChannelStreams(m_youtubeResolvedChannelId);
                return;
            }
            setStatus(QStringLiteral("YouTube"), QStringLiteral("Se rechazó el video %1 porque no se pudo verificar que pertenezca al canal configurado.").arg(videoId));
            m_youtubeTimer.start(15000);
            return;
        }

        const QJsonObject liveChat = findLiveChatRenderer(root);
        int initialDelay = 0;
        const QString continuation = findLiveChatContinuation(liveChat, &initialDelay);
        if (liveChat.isEmpty() || continuation.isEmpty()) {
            if (allowChannelFallback && (!m_youtubeResolvedChannelId.isEmpty() || !m_youtubeResolvedHandle.isEmpty())) {
                m_youtubeRejectedVideoIds.insert(videoId);
                setStatus(QStringLiteral("YouTube"), QStringLiteral("El video del canal correcto no entregó chat activo; probando otro directo de ese canal."));
                if (!m_youtubeResolvedChannelId.isEmpty())
                    requestYouTubeChannelStreams(m_youtubeResolvedChannelId);
                else
                    requestYouTubeResolveChannel(m_youtubeResolvedHandle);
                return;
            }
            setStatus(QStringLiteral("YouTube"), QStringLiteral("/next respondió, pero no incluyó liveChatRenderer con una continuación para el directo %1.").arg(videoId));
            m_youtubeTimer.start(15000);
            return;
        }

        m_youtubeConnectedAtUsec = QDateTime::currentDateTimeUtc().toMSecsSinceEpoch() * 1000;
        m_youtubeBaselineReady = true;
        m_youtubeContinuation = youtubeAllMessagesContinuation(continuation);
        m_youtubePollDelay = initialDelay > 0 ? initialDelay : 3000;
        setStatus(QStringLiteral("YouTube"), QStringLiteral("Continuación live-position obtenida; consultando el chat."));
        pollYouTube();
    });
}


void ChatController::pollYouTube()
{
    if (m_stopping) return;
    if (m_youtubeContinuation.isEmpty()) {
        if (!m_youtubeChannel.isEmpty()) connectYouTube();
        return;
    }

    QUrl endpoint(QStringLiteral("https://www.youtube.com/youtubei/v1/live_chat/get_live_chat"));
    QUrlQuery query;
    query.addQueryItem(QStringLiteral("key"), m_youtubeApiKey);
    query.addQueryItem(QStringLiteral("prettyPrint"), QStringLiteral("false"));
    endpoint.setQuery(query);

    QJsonObject requestBody{
        {QStringLiteral("context"), m_youtubeContext},
        {QStringLiteral("continuation"), m_youtubeContinuation}
    };
    QNetworkRequest request{endpoint};
    request.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/json"));
    request.setHeader(QNetworkRequest::UserAgentHeader, QStringLiteral("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131 Safari/537.36"));
    request.setRawHeader("Origin", "https://www.youtube.com");
    request.setRawHeader("Referer", "https://www.youtube.com/");
    request.setRawHeader("X-YouTube-Client-Name", "1");
    request.setRawHeader("X-YouTube-Client-Version", m_youtubeClientVersion.toUtf8());
    const QString visitorData = m_youtubeContext.value(QStringLiteral("client")).toObject()
                                    .value(QStringLiteral("visitorData")).toString();
    if (!visitorData.isEmpty())
        request.setRawHeader("X-Goog-Visitor-Id", visitorData.toUtf8());

    auto *reply = m_network.post(request, QJsonDocument(requestBody).toJson(QJsonDocument::Compact));
    m_youtubeReply = reply;
    connect(reply, &QNetworkReply::finished, this, [this, reply]() {
        if (m_stopping || m_youtubeReply != reply) { reply->deleteLater(); return; }
        const QByteArray bytes = reply->readAll();
        const QString error = reply->errorString();
        const int code = reply->attribute(QNetworkRequest::HttpStatusCodeAttribute).toInt();
        const bool failed = reply->error() != QNetworkReply::NoError || code < 200 || code >= 300;
        reply->deleteLater();
        m_youtubeReply = nullptr;

        if (failed) {
            m_youtubeConnected = false;
            if (code == 400 || code == 404)
                m_youtubeContinuation.clear();
            setConnectedState();
            const QString detail = QString::fromUtf8(bytes.left(400)).simplified();
            setStatus(QStringLiteral("YouTube"), QStringLiteral("InnerTube rechazó la consulta (HTTP %1): %2. Respuesta: %3")
                          .arg(code).arg(error).arg(detail.isEmpty() ? QStringLiteral("sin cuerpo") : detail));
            m_youtubeTimer.start(10000);
            return;
        }

        const QJsonDocument document = QJsonDocument::fromJson(bytes);
        if (!document.isObject()) {
            m_youtubeConnected = false;
            setConnectedState();
            setStatus(QStringLiteral("YouTube"), QStringLiteral("YouTube devolvió una respuesta que no es JSON válido."));
            m_youtubeTimer.start(10000);
            return;
        }
        const QJsonObject root = document.object();
        if (root.contains(QStringLiteral("error"))) {
            m_youtubeConnected = false;
            m_youtubeContinuation.clear();
            setConnectedState();
            const QJsonObject apiError = root.value(QStringLiteral("error")).toObject();
            setStatus(QStringLiteral("YouTube"), QStringLiteral("Error InnerTube: %1").arg(apiError.value(QStringLiteral("message")).toString(QStringLiteral("respuesta de error"))));
            m_youtubeTimer.start(15000);
            return;
        }
        parseYouTubeResponse(root);
        m_youtubeConnected = !m_youtubeContinuation.isEmpty();
        setConnectedState();
        if (m_youtubeContinuation.isEmpty()) {
            setStatus(QStringLiteral("YouTube"), QStringLiteral("El chat dejó de ofrecer continuaciones; comprobando nuevamente la transmisión."));
            m_youtubeTimer.start(15000);
        } else {
            setStatus(QStringLiteral("YouTube"), QStringLiteral("Chat conectado; consultando mensajes."));
            m_youtubeTimer.start(qMax(1000, m_youtubePollDelay));
        }
    });
}

void ChatController::parseYouTubeResponse(const QJsonObject &root)
{
    QList<QVariantMap> found;
    collectYouTubeMessages(root, found);
    for (const QVariantMap &entry : found) {
        const QString id = entry.value(QStringLiteral("id")).toString();
        if (id.isEmpty() || m_youtubeSeenIds.contains(id))
            continue;
        m_youtubeSeenIds.insert(id);
        m_youtubeSeenOrder.enqueue(id);
        while (m_youtubeSeenOrder.size() > 2048)
            m_youtubeSeenIds.remove(m_youtubeSeenOrder.dequeue());

        bool timestampOk = false;
        const qint64 messageTimestampUsec =
            entry.value(QStringLiteral("timestampUsec")).toString().toLongLong(&timestampOk);
        if (!timestampOk || messageTimestampUsec <= m_youtubeConnectedAtUsec)
            continue;

        publishMessage(entry.value(QStringLiteral("name")).toString(),
                       entry.value(QStringLiteral("text")).toString(),
                       QStringLiteral("YouTube"));
    }
    m_youtubeBaselineReady = true;

    const QJsonObject liveChat = root.value(QStringLiteral("continuationContents")).toObject()
                                     .value(QStringLiteral("liveChatContinuation")).toObject();
    int serverDelay = 0;
    const QString next = findLiveChatContinuation(liveChat, &serverDelay, true);
    if (!next.isEmpty()) {
        m_youtubeContinuation = youtubeAllMessagesContinuation(next);
        m_youtubePollDelay = serverDelay > 0 ? serverDelay : 3000;
    } else {
        m_youtubeContinuation.clear();
        m_youtubePollDelay = 30000;
    }
}

void ChatController::stopYouTube()
{
    m_youtubeTimer.stop();
    m_youtubeConnected = false;
    if (m_youtubeReply) {
        auto *reply = m_youtubeReply.data();
        m_youtubeReply = nullptr;
        reply->abort();
        reply->deleteLater();
    }
    m_youtubeContinuation.clear();
    setConnectedState();
}

void ChatController::publishMessage(const QString &rawName, const QString &rawText, const QString &platform)
{
    const QString message = cleanText(rawText);
    const QString name = cleanUserName(cleanText(rawName));
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
    const QString prefix = m_character;
    if (!prefix.isEmpty()) {
        if (!text.startsWith(prefix)) return;
        text = text.mid(prefix.size()).trimmed();
    }
    if (text.isEmpty()) return;
    text = correctPronunciation(text);
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
    request.setHeader(QNetworkRequest::ContentTypeHeader, QStringLiteral("application/x-www-form-urlencoded"));
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
        auto *reply = m_ttsReply.data();
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
