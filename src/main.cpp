#include "ChatController.h"
#include <QGuiApplication>
#include <QQmlApplicationEngine>
#include <QQmlContext>
#include <QQuickStyle>
int main(int argc, char *argv[])
{
    QGuiApplication app(argc, argv);
    QGuiApplication::setApplicationName(QStringLiteral("PavoTTS"));
    QGuiApplication::setOrganizationName(QStringLiteral("ale5is"));
    QQuickStyle::setStyle(QStringLiteral("Basic"));
    ChatController controller;
    QQmlApplicationEngine engine;
    engine.rootContext()->setContextProperty(QStringLiteral("chat"), &controller);
    engine.loadFromModule(QStringLiteral("PavoTTS"), QStringLiteral("Main"));
    if (engine.rootObjects().isEmpty()) return -1;
    return app.exec();
}
