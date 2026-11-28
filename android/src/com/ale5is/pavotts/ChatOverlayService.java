package com.ale5is.pavotts;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.PowerManager;
import android.provider.Settings;
import android.text.TextUtils;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import java.util.ArrayDeque;
import java.util.Deque;
import java.util.Queue;

public final class ChatOverlayService extends Service {
    public static final String ACTION_MESSAGE = "com.ale5is.pavotts.OVERLAY_MESSAGE";
    public static final String EXTRA_MESSAGE = "message";
    private static final String PREFS = "pavotts_overlay";
    private static final String BUBBLE_ENABLED = "enabled";
    private static final String TICKER_ENABLED = "ticker_enabled";
    private static final String CHANNEL_ID = "pavotts_overlay";
    private static final int BUBBLE_SIZE_DP = 58;
    private static final int PANEL_WIDTH_DP = 276;
    private static final int PANEL_HEIGHT_DP = 320;
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Queue<String> messages = new ArrayDeque<>();
    private final Deque<String> chatHistory = new ArrayDeque<>();
    private WindowManager windowManager;
    private TextView ticker;
    private View bubble;
    private LinearLayout chatPanel;
    private TextView chatContent;
    private ScrollView chatScroll;
    private WindowManager.LayoutParams bubbleParams;
    private WindowManager.LayoutParams panelParams;
    private boolean panelVisible;
    private boolean animating;
    private PowerManager.WakeLock wakeLock;

    @Override
    public void onCreate() {
        super.onCreate();
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
        PowerManager powerManager = (PowerManager) getSystemService(POWER_SERVICE);
        if (powerManager != null) {
            wakeLock = powerManager.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "PavoTTS:ChatBackground");
            wakeLock.setReferenceCounted(false);
            wakeLock.acquire();
        }
        createNotificationChannel();
        Notification notification = new Notification.Builder(this, CHANNEL_ID)
                .setSmallIcon(android.R.drawable.ic_dialog_info)
                .setContentTitle("PavoTTS")
                .setContentText("Chat y voz en segundo plano activos")
                .setOngoing(true)
                .build();
        if (Build.VERSION.SDK_INT >= 34) {
            startForeground(41, notification,
                    ServiceInfo.FOREGROUND_SERVICE_TYPE_SPECIAL_USE
                            | ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK);
        } else {
            startForeground(41, notification);
        }
        syncOverlay();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        syncOverlay();
        if (intent != null && ACTION_MESSAGE.equals(intent.getAction())) {
            String message = intent.getStringExtra(EXTRA_MESSAGE);
            if (message != null && !message.trim().isEmpty()) {
                if (messages.size() >= 100)
                    messages.poll();
                messages.offer(message);
                chatHistory.offerLast(message);
                while (chatHistory.size() > 40)
                    chatHistory.pollFirst();
                appendToChat(message);
                showNext();
            }
        }
        return START_STICKY;
    }

    private boolean canDrawOverlays() {
        return Build.VERSION.SDK_INT < Build.VERSION_CODES.M || Settings.canDrawOverlays(this);
    }

    private void syncOverlay() {
        android.content.SharedPreferences prefs = getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        boolean bubbleEnabled = prefs.getBoolean(BUBBLE_ENABLED, false);
        boolean tickerEnabled = prefs.getBoolean(TICKER_ENABLED, false);
        boolean allowed = canDrawOverlays();

        if (tickerEnabled && allowed) {
            if (ticker == null) {
                createTicker();
                showNext();
            }
        } else {
            removeTicker();
        }

        if (bubbleEnabled && allowed) {
            if (bubble == null)
                createBubbleAndChat();
        } else {
            removeBubble();
        }
    }

    private void removeTicker() {
        handler.removeCallbacks(tickerAdvance);
        animating = false;
        if (ticker != null) {
            ticker.setSelected(false);
            try { windowManager.removeView(ticker); } catch (RuntimeException ignored) {}
            ticker = null;
        }
    }

    private void removeBubble() {
        closePanel();
        if (bubble != null) {
            try { windowManager.removeView(bubble); } catch (RuntimeException ignored) {}
            bubble = null;
            bubbleParams = null;
        }
        chatPanel = null;
        chatContent = null;
        chatScroll = null;
    }

    private void removeOverlay() {
        removeTicker();
        removeBubble();
    }

    private void createTicker() {
        ticker = new TextView(this);
        ticker.setTextColor(Color.WHITE);
        ticker.setTextSize(15);
        ticker.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        ticker.setSingleLine(true);
        ticker.setEllipsize(TextUtils.TruncateAt.MARQUEE);
        ticker.setMarqueeRepeatLimit(-1);
        ticker.setSelected(true);
        ticker.setGravity(Gravity.CENTER_VERTICAL);
        ticker.setPadding(dp(16), dp(8), dp(16), dp(8));
        ticker.setText("PavoTTS · esperando mensajes");
        GradientDrawable background = new GradientDrawable();
        background.setColor(0xF0182435);
        background.setCornerRadius(dp(10));
        background.setStroke(dp(1), 0xFF3B5878);
        ticker.setBackground(background);
        WindowManager.LayoutParams params = new WindowManager.LayoutParams(
                WindowManager.LayoutParams.MATCH_PARENT,
                dp(46),
                overlayType(),
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                        | WindowManager.LayoutParams.FLAG_NOT_TOUCHABLE
                        | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT);
        params.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        params.y = dp(36);
        try {
            windowManager.addView(ticker, params);
            ticker.setVisibility(View.VISIBLE);
        } catch (RuntimeException ignored) {
            ticker = null;
        }
    }

    private void createBubbleAndChat() {
        chatPanel = new LinearLayout(this);
        chatPanel.setOrientation(LinearLayout.VERTICAL);
        chatPanel.setPadding(dp(12), dp(10), dp(12), dp(10));
        GradientDrawable panelBackground = new GradientDrawable();
        panelBackground.setColor(0xF0141D2B);
        panelBackground.setCornerRadius(dp(16));
        panelBackground.setStroke(dp(1), 0xFF34445B);
        chatPanel.setBackground(panelBackground);

        LinearLayout header = new LinearLayout(this);
        header.setGravity(Gravity.CENTER_VERTICAL);
        TextView title = new TextView(this);
        title.setText("PavoTTS · Chat");
        title.setTextColor(Color.WHITE);
        title.setTextSize(14);
        title.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        Button close = new Button(this);
        close.setText("×");
        close.setTextColor(Color.WHITE);
        close.setTextSize(20);
        close.setMinWidth(dp(42));
        close.setMinimumWidth(dp(42));
        close.setOnClickListener(v -> closePanel());
        header.addView(title, new LinearLayout.LayoutParams(0, dp(40), 1));
        header.addView(close, new LinearLayout.LayoutParams(dp(44), dp(40)));
        chatPanel.addView(header);

        chatScroll = new ScrollView(this);
        chatContent = new TextView(this);
        chatContent.setTextColor(0xFFF0F5FC);
        chatContent.setTextSize(13);
        chatContent.setPadding(dp(4), dp(8), dp(4), dp(8));
        chatContent.setText("Los mensajes nuevos aparecerán aquí.");
        if (!chatHistory.isEmpty()) {
            StringBuilder history = new StringBuilder();
            for (String item : chatHistory) {
                if (history.length() > 0)
                    history.append("\n\n");
                history.append(item);
            }
            chatContent.setText(history.toString());
        }
        chatScroll.addView(chatContent);
        chatPanel.addView(chatScroll, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, dp(260)));

        bubble = new TextView(this);
        ((TextView) bubble).setText("P");
        ((TextView) bubble).setTextColor(Color.WHITE);
        ((TextView) bubble).setTextSize(22);
        ((TextView) bubble).setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        ((TextView) bubble).setGravity(Gravity.CENTER);
        GradientDrawable bubbleBackground = new GradientDrawable();
        bubbleBackground.setColor(0xFF2388E8);
        bubbleBackground.setShape(GradientDrawable.OVAL);
        bubble.setBackground(bubbleBackground);
        bubble.setElevation(dp(8));

        bubbleParams = new WindowManager.LayoutParams(
                dp(BUBBLE_SIZE_DP), dp(BUBBLE_SIZE_DP), overlayType(),
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                        | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
                        | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT);
        bubbleParams.gravity = Gravity.TOP | Gravity.LEFT;
        bubbleParams.x = Math.max(0, getResources().getDisplayMetrics().widthPixels - dp(72));
        bubbleParams.y = Math.max(dp(80), getResources().getDisplayMetrics().heightPixels - dp(150));

        final float[] touchStart = new float[2];
        final int[] windowStart = new int[2];
        final boolean[] dragged = new boolean[1];
        bubble.setOnTouchListener((view, event) -> {
            if (bubbleParams == null)
                return true;
            switch (event.getAction()) {
                case MotionEvent.ACTION_DOWN:
                    touchStart[0] = event.getRawX();
                    touchStart[1] = event.getRawY();
                    windowStart[0] = bubbleParams.x;
                    windowStart[1] = bubbleParams.y;
                    dragged[0] = false;
                    return true;
                case MotionEvent.ACTION_MOVE:
                    float dx = event.getRawX() - touchStart[0];
                    float dy = event.getRawY() - touchStart[1];
                    if (Math.abs(dx) > dp(5) || Math.abs(dy) > dp(5))
                        dragged[0] = true;
                    if (dragged[0]) {
                        int maxX = Math.max(0, getResources().getDisplayMetrics().widthPixels - dp(BUBBLE_SIZE_DP));
                        int maxY = Math.max(0, getResources().getDisplayMetrics().heightPixels - dp(BUBBLE_SIZE_DP));
                        bubbleParams.x = Math.max(0, Math.min(maxX, windowStart[0] + Math.round(dx)));
                        bubbleParams.y = Math.max(0, Math.min(maxY, windowStart[1] + Math.round(dy)));
                        try { windowManager.updateViewLayout(bubble, bubbleParams); }
                        catch (RuntimeException ignored) {}
                    }
                    return true;
                case MotionEvent.ACTION_UP:
                    if (!dragged[0])
                        openPanel();
                    return true;
                case MotionEvent.ACTION_CANCEL:
                    return true;
            }
            return true;
        });

        try {
            windowManager.addView(bubble, bubbleParams);
        } catch (RuntimeException ignored) {
            bubble = null;
            bubbleParams = null;
        }
    }

    private void openPanel() {
        if (bubble == null || bubbleParams == null || chatPanel == null || panelVisible)
            return;

        int screenWidth = getResources().getDisplayMetrics().widthPixels;
        int screenHeight = getResources().getDisplayMetrics().heightPixels;
        int bubbleSize = dp(BUBBLE_SIZE_DP);
        int panelWidth = dp(PANEL_WIDTH_DP);
        int panelHeight = dp(PANEL_HEIGHT_DP);
        int bubbleCenterX = bubbleParams.x + bubbleSize / 2;
        int bubbleCenterY = bubbleParams.y + bubbleSize / 2;
        boolean bubbleOnRight = bubbleCenterX >= screenWidth / 2;
        boolean bubbleOnBottom = bubbleCenterY >= screenHeight / 2;

        panelParams = new WindowManager.LayoutParams(
                panelWidth, panelHeight, overlayType(),
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                        | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
                        | WindowManager.LayoutParams.FLAG_WATCH_OUTSIDE_TOUCH
                        | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT);
        panelParams.gravity = Gravity.TOP | Gravity.LEFT;

        int panelX = bubbleOnRight
                ? bubbleParams.x + bubbleSize - panelWidth
                : bubbleParams.x;
        int panelY = bubbleOnBottom
                ? bubbleParams.y - panelHeight
                : bubbleParams.y + bubbleSize;
        panelParams.x = Math.max(0, Math.min(Math.max(0, screenWidth - panelWidth), panelX));
        panelParams.y = Math.max(0, Math.min(Math.max(0, screenHeight - panelHeight), panelY));

        if (bubbleOnRight) {
            headerCloseOnRight();
        } else {
            headerCloseOnLeft();
        }

        chatPanel.setOnTouchListener((view, event) -> {
            if (event.getAction() == MotionEvent.ACTION_OUTSIDE) {
                closePanel();
                return true;
            }
            return false;
        });
        try {
            windowManager.addView(chatPanel, panelParams);
            panelVisible = true;
        } catch (RuntimeException ignored) {
            panelParams = null;
            panelVisible = false;
        }
    }

    private void headerCloseOnRight() {
        LinearLayout header = (LinearLayout) chatPanel.getChildAt(0);
        View title = header.getChildAt(0);
        View close = header.getChildAt(1);
        header.removeAllViews();
        header.addView(title, new LinearLayout.LayoutParams(0, dp(40), 1));
        header.addView(close, new LinearLayout.LayoutParams(dp(44), dp(40)));
    }

    private void headerCloseOnLeft() {
        LinearLayout header = (LinearLayout) chatPanel.getChildAt(0);
        View title = header.getChildAt(0);
        View close = header.getChildAt(1);
        header.removeAllViews();
        header.addView(close, new LinearLayout.LayoutParams(dp(44), dp(40)));
        header.addView(title, new LinearLayout.LayoutParams(0, dp(40), 1));
    }

    private void closePanel() {
        if (chatPanel != null && panelVisible) {
            try { windowManager.removeView(chatPanel); } catch (RuntimeException ignored) {}
        }
        panelVisible = false;
        panelParams = null;
    }

    private int overlayType() {
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
                : WindowManager.LayoutParams.TYPE_PHONE;
    }

    private void appendToChat(String message) {
        if (chatContent == null)
            return;
        String current = chatContent.getText().toString();
        if (current.equals("Los mensajes nuevos aparecerán aquí."))
            current = "";
        String updated = current.isEmpty() ? message : current + "\n\n" + message;
        String[] lines = updated.split("\n\n");
        if (lines.length > 40) {
            StringBuilder trimmed = new StringBuilder();
            for (int i = lines.length - 40; i < lines.length; i++) {
                if (trimmed.length() > 0)
                    trimmed.append("\n\n");
                trimmed.append(lines[i]);
            }
            updated = trimmed.toString();
        }
        chatContent.setText(updated);
        if (chatScroll != null)
            chatScroll.post(() -> chatScroll.fullScroll(View.FOCUS_DOWN));
    }

    private final Runnable tickerAdvance = new Runnable() {
        @Override
        public void run() {
            animating = false;
            showNext();
        }
    };

    private void showNext() {
        if (animating || ticker == null || messages.isEmpty())
            return;
        String next = messages.poll();
        ticker.setSelected(false);
        ticker.setText(next);
        ticker.setSelected(true);
        animating = true;
        long duration = Math.max(6500L, Math.min(180000L, next.length() * 600L));
        handler.removeCallbacks(tickerAdvance);
        handler.postDelayed(tickerAdvance, duration);
    }

    private int dp(int value) {
        return (int) (value * getResources().getDisplayMetrics().density + 0.5f);
    }

    private void createNotificationChannel() {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            NotificationChannel channel = new NotificationChannel(
                    CHANNEL_ID, "Chat y audio en segundo plano", NotificationManager.IMPORTANCE_LOW);
            getSystemService(NotificationManager.class).createNotificationChannel(channel);
        }
    }

    @Override
    public void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        removeOverlay();
        if (wakeLock != null && wakeLock.isHeld())
            wakeLock.release();
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}