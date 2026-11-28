import QtQuick
import QtQuick.Controls
import QtQuick.Layouts
import QtQuick.Window

ApplicationWindow {
    id: window
    Component.onCompleted: { chat.setOverlayEnabled(chat.overlayEnabled); chat.setTickerEnabled(chat.tickerEnabled); chat.setTtsEnabled(chat.ttsEnabled) }
    width: 1280
    height: 720
    minimumWidth: 360
    minimumHeight: 620
    visible: true
    title: "PavoTTS — Chat nativo"
    color: "#0b1019"
    property bool settingsPage: true
    property bool compact: width < 760

    function keepFocusedFieldVisible() {
        var item = window.activeFocusItem
        if (!item || !settingsPage || !settingsFlickable.visible)
            return false

        var keyboard = Qt.inputMethod.keyboardRectangle
        var viewportHeight = settingsFlickable.height
        if (Qt.inputMethod.visible && keyboard.height > 0) {
            var keyboardTop = settingsFlickable.mapFromItem(null, 0, keyboard.y).y
            viewportHeight = Math.max(0, Math.min(viewportHeight, keyboardTop))
        }

        var point = item.mapToItem(settingsFlickable.contentItem, 0, 0)
        var itemTop = point.y
        var itemBottom = point.y + item.height
        var visibleTop = settingsFlickable.contentY
        var visibleBottom = visibleTop + viewportHeight - 20
        var targetY = settingsFlickable.contentY

        if (itemBottom > visibleBottom)
            targetY = itemBottom - viewportHeight + 36
        else if (itemTop < visibleTop)
            targetY = itemTop - 20

        targetY = Math.max(0, Math.min(settingsFlickable.contentHeight - viewportHeight, targetY))
        if (Math.abs(targetY - settingsFlickable.contentY) > 1) {
            settingsFlickable.contentY = targetY
            return true
        }
        return false
    }

    function scheduleFocusedFieldScroll() {
        focusScrollAttempts = 0
        focusScrollTimer.restart()
    }

    property int focusScrollAttempts: 0

    onActiveFocusItemChanged: {
        Qt.callLater(function() { window.scheduleFocusedFieldScroll() })
    }

    Timer {
        id: focusScrollTimer
        interval: 180
        repeat: false
        onTriggered: {
            var moved = window.keepFocusedFieldVisible()
            if (Qt.inputMethod.visible && moved && window.focusScrollAttempts < 8) {
                window.focusScrollAttempts++
                restart()
            }
        }
    }

    Connections {
        target: Qt.inputMethod
        function onVisibleChanged() {
            if (Qt.inputMethod.visible)
                Qt.callLater(function() { window.scheduleFocusedFieldScroll() })
        }
        function onKeyboardRectangleChanged() {
            if (Qt.inputMethod.visible)
                Qt.callLater(function() { window.scheduleFocusedFieldScroll() })
        }
    }

    component Panel: Rectangle {
        radius: 16
        color: "#141d2b"
        border.color: "#26354a"
        border.width: 1
    }
    component Field: TextField {
        Layout.fillWidth: true
        implicitHeight: 46
        color: "#f3f6fb"
        placeholderTextColor: "#8392a8"
        leftPadding: 14
        rightPadding: 14
        background: Rectangle {
            radius: 9
            color: "#0d1521"
            border.color: parent.activeFocus ? "#36a3ff" : "#2a3b51"
        }
    }
    component ActionButton: Button {
        implicitHeight: 44
        contentItem: Text {
            text: parent.text
            color: "#ffffff"
            font.pixelSize: 14
            font.bold: true
            horizontalAlignment: Text.AlignHCenter
            verticalAlignment: Text.AlignVCenter
        }
        background: Rectangle {
            radius: 9
            color: parent.down ? "#176fc1" : (parent.enabled ? "#2388e8" : "#344154")
        }
    }

    header: Rectangle {
        color: "#111a27"
        height: 68
        border.color: "#26354a"
        RowLayout {
            anchors.fill: parent
            anchors.leftMargin: 20
            anchors.rightMargin: 20
            spacing: 12
            Rectangle {
                width: 38
                height: 38
                radius: 11
                color: "#2388e8"
                Text { anchors.centerIn: parent; text: "P"; color: "white"; font.bold: true; font.pixelSize: 22 }
            }
            ColumnLayout {
                spacing: 0
                Text { text: "PavoTTS"; color: "white"; font.bold: true; font.pixelSize: 19 }
                Text { text: "CHAT MULTIPLATAFORMA"; color: "#8da2bd"; font.pixelSize: 9; font.letterSpacing: 1.5 }
            }
            Item { Layout.fillWidth: true }
            Rectangle {
                radius: 20
                color: chat.connected ? "#123b32" : "#342b24"
                implicitWidth: stateLabel.implicitWidth + 22
                implicitHeight: 30
                Text {
                    id: stateLabel
                    anchors.centerIn: parent
                    text: chat.connected ? "● CONECTADO" : "● DESCONECTADO"
                    color: chat.connected ? "#59e0b0" : "#ffc078"
                    font.pixelSize: 10
                    font.bold: true
                }
            }
        }
    }

    ColumnLayout {
        anchors.fill: parent
        anchors.margins: compact ? 12 : 22
        spacing: 14

        RowLayout {
            Layout.fillWidth: true
            spacing: 8
            Button {
                text: "⚙  Configuración"
                checkable: true
                checked: settingsPage
                onClicked: settingsPage = true
            }
            Button {
                text: "☰  Chat en vivo"
                checkable: true
                checked: !settingsPage
                onClicked: settingsPage = false
            }
            Item { Layout.fillWidth: true }
            RowLayout {
                visible: !compact
                spacing: 3
                Button { text: "720p"; onClicked: { window.showNormal(); window.width = 1280; window.height = 720 } }
                Button { text: "900p"; onClicked: { window.showNormal(); window.width = 1600; window.height = 900 } }
                Button { text: "1080p"; onClicked: { window.showNormal(); window.width = 1920; window.height = 1080 } }
                Button { text: "−"; onClicked: window.showMinimized() }
                Button { text: "□"; onClicked: window.visibility === Window.Maximized ? window.showNormal() : window.showMaximized() }
                Button { text: "⛶"; onClicked: window.visibility === Window.FullScreen ? window.showNormal() : window.showFullScreen() }
            }
            ActionButton {
                text: chat.connected ? "Desconectar" : "Conectar"
                onClicked: chat.connected ? chat.disconnectAll() : chat.connectAll()
            }
        }

        ColumnLayout {
            Layout.fillWidth: true
            spacing: 3
            Label {
                Layout.fillWidth: true
                text: chat.status
                color: "#9fb2ca"
                wrapMode: Text.Wrap
                font.pixelSize: 12
            }
            Label {
                Layout.fillWidth: true
                text: "Twitch: " + chat.twitchStatus + "   |   Kick: " + chat.kickStatus + "   |   YouTube: " + chat.youtubeStatus
                color: "#7f91a8"
                wrapMode: Text.Wrap
                font.pixelSize: 10
            }
        }

        StackLayout {
            Layout.fillWidth: true
            Layout.fillHeight: true
            currentIndex: settingsPage ? 0 : 1

            Flickable {
                id: settingsFlickable
                clip: true
                contentWidth: width
                contentHeight: settingsColumn.implicitHeight
                ScrollBar.vertical: ScrollBar {}
                ColumnLayout {
                    id: settingsColumn
                    width: parent.width
                    spacing: 14

                    GridLayout {
                        Layout.fillWidth: true
                        columns: compact ? 1 : 2
                        columnSpacing: 14
                        rowSpacing: 14

                        Panel {
                            Layout.fillWidth: true
                            implicitHeight: twitchColumn.implicitHeight + 28
                            ColumnLayout {
                                id: twitchColumn
                                anchors.fill: parent
                                anchors.margins: 14
                                Label { text: "TWITCH"; color: "#b78cff"; font.bold: true; font.letterSpacing: 1.2 }
                                Field { placeholderText: "Nombre del canal (ej. ale5is14)"; text: chat.twitchChannel; onTextEdited: chat.twitchChannel = text }
                                Label { text: "Lectura anónima por IRC/WebSocket. Solo lectura; algunos canales o restricciones pueden impedir el acceso."; color: "#7f91a8"; font.pixelSize: 10; wrapMode: Text.Wrap }
                            }
                        }

                        Panel {
                            Layout.fillWidth: true
                            implicitHeight: youtubeColumn.implicitHeight + 28
                            ColumnLayout {
                                id: youtubeColumn
                                anchors.fill: parent
                                anchors.margins: 14
                                Label { text: "YOUTUBE LIVE"; color: "#ff7777"; font.bold: true; font.letterSpacing: 1.2 }
                                Field { placeholderText: "Canal o @handle (ej. @ale5is)"; text: chat.youtubeChannel; onTextEdited: chat.youtubeChannel = text }
                                Label { text: "Lectura del chat en vivo mediante InnerTube."; color: "#7f91a8"; font.pixelSize: 10; wrapMode: Text.Wrap }
                            }
                        }

                        Panel {
                            Layout.fillWidth: true
                            implicitHeight: kickColumn.implicitHeight + 28
                            ColumnLayout {
                                id: kickColumn
                                anchors.fill: parent
                                anchors.margins: 14
                                Label { text: "KICK"; color: "#65e6a6"; font.bold: true; font.letterSpacing: 1.2 }
                                Field { placeholderText: "Canal de Kick"; text: chat.kickChannel; onTextEdited: chat.kickChannel = text }
                                Label { text: "Negociación REST y chat realtime Centrifugo."; color: "#7f91a8"; font.pixelSize: 10; wrapMode: Text.Wrap }
                            }
                        }

                        Panel {
                            Layout.fillWidth: true
                            implicitHeight: ttsColumn.implicitHeight + 28
                            ColumnLayout {
                                id: ttsColumn
                                anchors.fill: parent
                                anchors.margins: 14
                                Label { text: "VOZ Y LECTURA"; color: "#62c4ff"; font.bold: true; font.letterSpacing: 1.2 }
                                Field { placeholderText: "TikTok Session ID"; echoMode: TextInput.Password; text: chat.sessionId; onTextEdited: chat.sessionId = text }
                                RowLayout {
                                    Layout.fillWidth: true
                                    Field { placeholderText: "Voz (es_002)"; text: chat.voice; onTextEdited: chat.voice = text }
                                    Field { Layout.maximumWidth: 110; placeholderText: "Prefijo"; text: chat.character; onTextEdited: chat.character = text }
                                }
                                RowLayout {
                                    Layout.fillWidth: true
                                    Label { text: "Volumen"; color: "#cbd6e5" }
                                    Slider { Layout.fillWidth: true; from: 0; to: 1; value: chat.volume; onMoved: chat.volume = value }
                                    Label { text: Math.round(chat.volume * 100) + "%"; color: "white"; Layout.minimumWidth: 42 }
                                }
                                RowLayout {
                                    Layout.fillWidth: true
                                    Switch {
                                        text: chat.ttsEnabled ? "TTS activado" : "Activar TTS"
                                        checked: chat.ttsEnabled
                                        onToggled: chat.ttsEnabled = checked
                                    }
                                    Item { Layout.fillWidth: true }
                                    Label { text: chat.queuedSpeech + " en cola"; color: "#91a4bd"; font.pixelSize: 11 }
                                }
                                RowLayout {
                                    Layout.fillWidth: true
                                    ActionButton { text: "Probar voz"; enabled: chat.ttsEnabled; onClicked: chat.testTts() }
                                    Button { text: "Vaciar cola"; onClicked: chat.clearSpeechQueue() }
                                }
                                Label {
                                    Layout.fillWidth: true
                                    text: "La prueba funciona sin conectar Twitch, Kick ni YouTube. Solo necesitás configurar el TikTok Session ID y la voz."
                                    color: "#91a4bd"
                                    font.pixelSize: 10
                                    wrapMode: Text.Wrap
                                }
                            }
                        }
                    }

                    Panel {
                        Layout.fillWidth: true
                        implicitHeight: overlayColumn.implicitHeight + 28
                        ColumnLayout {
                            id: overlayColumn
                            anchors.fill: parent
                            anchors.margins: 14
                            Label { text: "CHAT FLOTANTE Y CINTA"; color: "#62c4ff"; font.bold: true; font.letterSpacing: 1.2 }
                            RowLayout {
                                Layout.fillWidth: true
                                Switch {
                                    text: chat.tickerEnabled ? "Cinta visible" : "Activar línea de mensajes"
                                    checked: chat.tickerEnabled
                                    onToggled: chat.setTickerEnabled(checked)
                                }
                            }
                            Label {
                                Layout.fillWidth: true
                                text: "La cinta es independiente de la burbuja: queda fija arriba y desplaza cada mensaje completo de forma continua. Si todavía no llegan mensajes, muestra «PavoTTS · esperando mensajes»."
                                color: "#91a4bd"
                                font.pixelSize: 10
                                wrapMode: Text.Wrap
                            }
                            RowLayout {
                                Layout.fillWidth: true
                                Switch {
                                    text: chat.overlayEnabled ? "Burbuja flotante activada" : "Activar burbuja flotante"
                                    checked: chat.overlayEnabled
                                    onToggled: chat.setOverlayEnabled(checked)
                                }
                            }
                            Label {
                                Layout.fillWidth: true
                                text: "La cinta superior no intercepta toques: podés usar la aplicación que queda debajo. La burbuja azul se puede arrastrar; al tocarla, el chat se abre hacia el espacio libre según el lado de la pantalla y el botón × queda junto a la burbuja. Podés mover también la ventana arrastrando su encabezado. Tocar fuera del panel lo cierra sin bloquear la aplicación."
                                color: "#91a4bd"
                                font.pixelSize: 10
                                wrapMode: Text.Wrap
                            }
                            Label {
                                Layout.fillWidth: true
                                text: "El servicio en primer plano mantiene el proceso activo y usa un bloqueo parcial de CPU para ayudar a recibir mensajes mientras PavoTTS está minimizado. Android puede seguir limitando la red o detener la app si se fuerza su cierre."
                                color: "#91a4bd"
                                font.pixelSize: 10
                                wrapMode: Text.Wrap
                            }
                        }
                    }

                    Panel {
                        Layout.fillWidth: true
                        implicitHeight: blacklistColumn.implicitHeight + 28
                        ColumnLayout {
                            id: blacklistColumn
                            anchors.fill: parent
                            anchors.margins: 14
                            Label { text: "BLACKLIST"; color: "#ffbd70"; font.bold: true; font.letterSpacing: 1.2 }
                            RowLayout {
                                Layout.fillWidth: true
                                Field { id: blacklistEntry; placeholderText: "Nombre de usuario" }
                                Button { text: "Agregar"; onClicked: { chat.addBlacklist(blacklistEntry.text); blacklistEntry.text = "" } }
                                Button { text: "Quitar"; onClicked: { chat.removeBlacklist(blacklistEntry.text); blacklistEntry.text = "" } }
                                Button { text: "Vaciar"; onClicked: chat.clearBlacklist() }
                            }
                            TextArea {
                                Layout.fillWidth: true
                                readOnly: true
                                text: chat.blacklistText
                                color: "#cbd6e5"
                                background: Rectangle { color: "#0d1521"; radius: 8 }
                                wrapMode: TextEdit.Wrap
                                implicitHeight: 68
                            }
                        }
                    }
                    RowLayout {
                        Layout.fillWidth: true
                        Item { Layout.fillWidth: true }
                        ActionButton { text: "Guardar configuración"; onClicked: chat.saveSettings() }
                    }
                }
            }

            ColumnLayout {
                spacing: 10
                Panel {
                    Layout.fillWidth: true
                    Layout.fillHeight: true
                    ColumnLayout {
                        anchors.fill: parent
                        anchors.margins: 14
                        RowLayout {
                            Layout.fillWidth: true
                            Label { text: "CHAT MULTIPLATAFORMA"; color: "white"; font.bold: true; font.pixelSize: 16 }
                            Item { Layout.fillWidth: true }
                            Label { text: chat.messages.length + " mensajes"; color: "#8195ae"; font.pixelSize: 11 }
                            Button { text: "Limpiar"; onClicked: chat.clearChat() }
                        }
                        ListView {
                            id: chatList
                            Layout.fillWidth: true
                            Layout.fillHeight: true
                            clip: true
                            spacing: 12
                            model: chat.messages
                            delegate: Rectangle {
                                required property var modelData
                                width: ListView.view.width
                                height: messageColumn.implicitHeight + 30
                                radius: 9
                                color: "#0e1724"
                                ColumnLayout {
                                    id: messageColumn
                                    anchors.fill: parent
                                    anchors.margins: 16
                                    RowLayout {
                                        Layout.fillWidth: true
                                        Rectangle {
                                            radius: 6
                                            color: modelData.platform === "Twitch" ? "#39265b" : (modelData.platform === "Kick" ? "#164a35" : "#572b32")
                                            implicitWidth: platformName.implicitWidth + 18
                                            implicitHeight: 30
                                            Label {
                                                id: platformName
                                                anchors.centerIn: parent
                                                text: modelData.platform.toUpperCase()
                                                color: modelData.platform === "Twitch" ? "#d2b7ff" : (modelData.platform === "Kick" ? "#8bf0bd" : "#ffaaaa")
                                                font.pixelSize: 13
                                                font.bold: true
                                            }
                                        }
                                        Label { text: modelData.name; color: "#ffffff"; font.bold: true; font.pixelSize: 18 }
                                        Item { Layout.fillWidth: true }
                                        Label { text: modelData.time; color: "#8294aa"; font.pixelSize: 11 }
                                    }
                                    Label {
                                        Layout.fillWidth: true
                                        text: modelData.text
                                        color: "#f0f5fc"
                                        wrapMode: Text.Wrap
                                        font.pixelSize: 21
                                        font.weight: Font.Medium
                                    }
                                }
                            }
                            onCountChanged: Qt.callLater(function() { chatList.positionViewAtEnd() })
                        }
                    }
                }
                RowLayout {
                    Layout.fillWidth: true
                    Label { text: !chat.ttsEnabled ? "◉ Solo chat · TTS desactivado" : (chat.speaking ? "🔊 Reproduciendo voz" : "◉ TTS activado · En espera"); color: chat.ttsEnabled ? (chat.speaking ? "#63e4b0" : "#90a2b8") : "#ffc078" }
                    Item { Layout.fillWidth: true }
                    Label { text: "Cola: " + chat.queuedSpeech; color: "#90a2b8" }
                    ActionButton {
                        text: chat.ttsEnabled ? "Desactivar TTS" : "Activar TTS"
                        onClicked: chat.ttsEnabled = !chat.ttsEnabled
                    }
                }
            }
        }
    }
}
