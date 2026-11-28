package com.ale5is.pavotts;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.PowerManager;
import android.text.TextUtils;
import android.view.MotionEvent;
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.view.WindowManager;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.content.pm.ServiceInfo;
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
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Queue<String> messages = new ArrayDeque<>();
    private final Deque<String> chatHistory = new ArrayDeque<>();
    private WindowManager windowManager;
    private TextView ticker;
    private LinearLayout overlayRoot;
    private LinearLayout chatPanel;
    private TextView chatContent;
    private ScrollView chatScroll;
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
            if (overlayRoot == null)
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
        if (overlayRoot != null) {
            try { windowManager.removeView(overlayRoot); } catch (RuntimeException ignored) {}
            overlayRoot = null;
            chatPanel = null;
            chatContent = null;
            chatScroll = null;
        }
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
                        | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
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
        overlayRoot = new LinearLayout(this);
        overlayRoot.setOrientation(LinearLayout.VERTICAL);
        overlayRoot.setGravity(Gravity.END);
        overlayRoot.setPadding(0, 0, 0, 0);

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
        header.addView(title, new LinearLayout.LayoutParams(0, dp(40), 1));

        Button collapse = new Button(this);
        collapse.setText("—");
        collapse.setTextColor(Color.WHITE);
        collapse.setMinWidth(dp(42));
        collapse.setMinimumWidth(dp(42));
        collapse.setOnClickListener(v -> setExpanded(false));
        header.addView(collapse, new LinearLayout.LayoutParams(dp(44), dp(40)));
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
                dp(276), dp(290)));

        TextView bubble = new TextView(this);
        bubble.setText("P");
        bubble.setTextColor(Color.WHITE);
        bubble.setTextSize(22);
        bubble.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        bubble.setGravity(Gravity.CENTER);
        GradientDrawable bubbleBackground = new GradientDrawable();
        bubbleBackground.setColor(0xFF2388E8);
        bubbleBackground.setShape(GradientDrawable.OVAL);
        bubble.setBackground(bubbleBackground);
        bubble.setElevation(dp(8));
        final float[] touchStart = new float[2];
        final int[] windowStart = new int[2];
        final boolean[] dragged = new boolean[1];
        bubble.setOnTouchListener((view, event) -> {
            WindowManager.LayoutParams current = (WindowManager.LayoutParams) overlayRoot.getLayoutParams();
            switch (event.getAction()) {
                case MotionEvent.ACTION_DOWN:
                    touchStart[0] = event.getRawX();
                    touchStart[1] = event.getRawY();
                    windowStart[0] = current.x;
                    windowStart[1] = current.y;
                    dragged[0] = false;
                    return true;
                case MotionEvent.ACTION_MOVE:
                    float dx = event.getRawX() - touchStart[0];
                    float dy = event.getRawY() - touchStart[1];
                    if (Math.abs(dx) > dp(5) || Math.abs(dy) > dp(5))
                        dragged[0] = true;
                    if (dragged[0]) {
                        int maxX = Math.max(0, getResources().getDisplayMetrics().widthPixels - dp(58));
                        int maxY = Math.max(0, getResources().getDisplayMetrics().heightPixels - dp(58));
                        current.x = Math.max(0, Math.min(maxX, windowStart[0] - Math.round(dx)));
                        current.y = Math.max(0, Math.min(maxY, windowStart[1] - Math.round(dy)));
                        try { windowManager.updateViewLayout(overlayRoot, current); }
                        catch (RuntimeException ignored) {}
                    }
                    return true;
                case MotionEvent.ACTION_UP:
                    if (!dragged[0])
                        setExpanded(true);
                    return true;
                case MotionEvent.ACTION_CANCEL:
                    return true;
            }
            return true;
        });

        overlayRoot.addView(chatPanel, new LinearLayout.LayoutParams(dp(300), dp(350)));
        overlayRoot.addView(bubble, new LinearLayout.LayoutParams(dp(58), dp(58)));
        chatPanel.setVisibility(View.GONE);

        WindowManager.LayoutParams params = new WindowManager.LayoutParams(
                dp(58), WindowManager.LayoutParams.WRAP_CONTENT,
                overlayType(),
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                        | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
                        | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT);
        params.gravity = Gravity.BOTTOM | Gravity.END;
        params.x = dp(14);
        params.y = dp(72);
        try {
            windowManager.addView(overlayRoot, params);
            setExpanded(false);
        } catch (RuntimeException ignored) {
            overlayRoot = null;
        }
    }

    private int overlayType() {
        return Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
                : WindowManager.LayoutParams.TYPE_PHONE;
    }

    private void setExpanded(boolean expanded) {
        if (chatPanel == null || overlayRoot == null)
            return;
        chatPanel.setVisibility(expanded ? View.VISIBLE : View.GONE);
        View bubble = overlayRoot.getChildAt(1);
        if (bubble != null)
            bubble.setVisibility(expanded ? View.GONE : View.VISIBLE);
        WindowManager.LayoutParams params = (WindowManager.LayoutParams) overlayRoot.getLayoutParams();
        params.width = expanded ? dp(300) : dp(58);
        params.height = WindowManager.LayoutParams.WRAP_CONTENT;
        try {
            windowManager.updateViewLayout(overlayRoot, params);
        } catch (RuntimeException ignored) {}
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