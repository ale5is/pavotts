#pragma once
#include <QObject>
#include <QVariantList>
#include <QQueue>
#include <QPointer>
#include <QWebSocket>
#include <QNetworkAccessManager>
#include <QNetworkReply>
#include <QTimer>
#include <QMediaPlayer>
#include <QAudioOutput>
#include <QSet>
#include <QJsonObject>
#include <QStringList>
#include <QNetworkRequest>
class ChatController final : public QObject
{
    Q_OBJECT
    Q_PROPERTY(QString status READ status NOTIFY statusChanged)
    Q_PROPERTY(QVariantList messages READ messages NOTIFY messagesChanged)
    Q_PROPERTY(QString twitchChannel READ twitchChannel WRITE setTwitchChannel NOTIFY settingsChanged)
    Q_PROPERTY(QString youtubeChannel READ youtubeChannel WRITE setYoutubeChannel NOTIFY settingsChanged)
    Q_PROPERTY(QString kickChannel READ kickChannel WRITE setKickChannel NOTIFY settingsChanged)
    Q_PROPERTY(QString sessionId READ sessionId WRITE setSessionId NOTIFY settingsChanged)
    Q_PROPERTY(QString voice READ voice WRITE setVoice NOTIFY settingsChanged)
    Q_PROPERTY(QString character READ character WRITE setCharacter NOTIFY settingsChanged)
    Q_PROPERTY(QString blacklistText READ blacklistText NOTIFY settingsChanged)
    Q_PROPERTY(QString commandPrefix READ commandPrefix WRITE setCommandPrefix NOTIFY settingsChanged)
    Q_PROPERTY(double volume READ volume WRITE setVolume NOTIFY settingsChanged)
    Q_PROPERTY(bool connected READ connected NOTIFY connectionChanged)
    Q_PROPERTY(bool speaking READ speaking NOTIFY speakingChanged)
    Q_PROPERTY(int queuedSpeech READ queuedSpeech NOTIFY speakingChanged)
public:
    explicit ChatController(QObject *parent = nullptr);
    ~ChatController() override;
    QString status() const;
    QVariantList messages() const;
    QString twitchChannel() const;
    QString youtubeChannel() const;
    QString kickChannel() const;
    QString sessionId() const;
    QString voice() const;
    QString character() const;
    QString blacklistText() const;
    QString commandPrefix() const;
    double volume() const;
    bool connected() const;
    bool speaking() const;
    int queuedSpeech() const;
    void setTwitchChannel(const QString &v);
    void setYoutubeChannel(const QString &v);
    void setKickChannel(const QString &v);
    void setSessionId(const QString &v);
    void setVoice(const QString &v);
    void setCharacter(const QString &v);
    void setCommandPrefix(const QString &v);
    void setVolume(double v);
    Q_INVOKABLE void connectAll();
    Q_INVOKABLE void disconnectAll();
    Q_INVOKABLE void saveSettings();
    Q_INVOKABLE void testTts();
    Q_INVOKABLE void clearChat();
    Q_INVOKABLE void addBlacklist(const QString &name);
    Q_INVOKABLE void removeBlacklist(const QString &name);
    Q_INVOKABLE void clearBlacklist();
    Q_INVOKABLE void clearSpeechQueue();
    Q_INVOKABLE QString oauthHelp() const;
signals:
    void statusChanged();
    void messagesChanged();
    void settingsChanged();
    void connectionChanged();
    void speakingChanged();
private:
    void loadSettings();
    void publishMessage(const QString &name, const QString &text, const QString &platform);
    void processForSpeech(const QString &name, const QString &text);
    void speakNext();
    void finishSpeech();
    void requestSpeech(const QString &text);
    void setStatus(const QString &platform, const QString &message);
    void connectTwitch();
    void connectKick();
    void connectYouTube();
    void handleTwitchLine(const QString &line, QWebSocket *socket);
    void handleKickFrame(const QJsonObject &frame, QWebSocket *socket);
    void pollYouTube();
    void parseYouTubeResponse(const QJsonObject &root);
    void stopYouTube();
    void setConnectedState();
    void scheduleKickRetry(const QString &reason);
    void scheduleTwitchRetry(const QString &reason);
    QString m_status = QStringLiteral("Ingresá los datos y presioná CONECTAR.");
    QVariantList m_messages;
    QString m_twitchChannel, m_youtubeChannel, m_kickChannel, m_sessionId;
    QString m_voice = QStringLiteral("es_002"), m_character, m_commandPrefix;
    double m_volume = 1.0;
    QSet<QString> m_blacklist;
    QNetworkAccessManager m_network;
    QPointer<QWebSocket> m_twitchSocket, m_kickSocket;
    QPointer<QNetworkReply> m_youtubeReply, m_ttsReply;
    QTimer m_youtubeTimer, m_twitchRetryTimer, m_kickRetryTimer, m_twitchHandshakeTimer, m_kickHandshakeTimer, m_kickLeaseTimer;
    QString m_twitchBuffer, m_kickChatroomId, m_youtubeApiKey, m_youtubeClientVersion, m_youtubeContinuation;
    int m_kickLookupAttempt = 0;
    QSet<QString> m_youtubeSeenIds;
    QQueue<QString> m_youtubeSeenOrder;
    int m_youtubePollDelay = 5000;
    bool m_stopping = false, m_connected = false, m_speaking = false;
    bool m_twitchRejected = false, m_twitchAuthenticated = false, m_kickSubscribed = false;
    int m_twitchRetryDelay = 3000, m_kickRetryDelay = 3000;
    quint64 m_connectionGeneration = 0;
    QQueue<QString> m_speechQueue;
    static constexpr int MaxSpeechQueue = 256;
    quint64 m_speechGeneration = 0;
    QMediaPlayer m_player;
    QAudioOutput m_audioOutput;
    QString m_currentAudioFile;
};
