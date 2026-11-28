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
import android.provider.Settings;
import android.view.Gravity;
import android.view.View;
import android.view.WindowManager;
import android.view.animation.LinearInterpolator;
import android.widget.Button;
import android.widget.LinearLayout;
import android.widget.ScrollView;
import android.widget.TextView;
import android.content.pm.ServiceInfo;
import java.util.ArrayDeque;
import java.util.Queue;

public final class ChatOverlayService extends Service {
    public static final String ACTION_MESSAGE = "com.ale5is.pavotts.OVERLAY_MESSAGE";
    public static final String EXTRA_MESSAGE = "message";
    private static final String PREFS = "pavotts_overlay";
    private static final String CHANNEL_ID = "pavotts_overlay";
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Queue<String> messages = new ArrayDeque<>();
    private WindowManager windowManager;
    private TextView ticker;
    private LinearLayout overlayRoot;
    private LinearLayout chatPanel;
    private TextView chatContent;
    private ScrollView chatScroll;
    private boolean animating;

    @Override
    public void onCreate() {
        super.onCreate();
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
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
        boolean enabled = getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .getBoolean("enabled", false);
        if (enabled && canDrawOverlays()) {
            if (overlayRoot == null) {
                createTicker();
                createBubbleAndChat();
            }
        } else {
            removeOverlay();
        }
    }

    private void removeOverlay() {
        if (ticker != null) {
            ticker.animate().cancel();
            try { windowManager.removeView(ticker); } catch (RuntimeException ignored) {}
            ticker = null;
        }
        if (overlayRoot != null) {
            try { windowManager.removeView(overlayRoot); } catch (RuntimeException ignored) {}
            overlayRoot = null;
            chatPanel = null;
            chatContent = null;
            chatScroll = null;
        }
    }

    private void createTicker() {
        ticker = new TextView(this);
        ticker.setTextColor(Color.WHITE);
        ticker.setTextSize(16);
        ticker.setTypeface(Typeface.DEFAULT, Typeface.BOLD);
        ticker.setSingleLine(true);
        ticker.setPadding(dp(16), dp(10), dp(16), dp(10));
        GradientDrawable background = new GradientDrawable();
        background.setColor(0xE817202E);
        background.setCornerRadius(dp(12));
        ticker.setBackground(background);
        WindowManager.LayoutParams params = new WindowManager.LayoutParams(
                WindowManager.LayoutParams.WRAP_CONTENT,
                WindowManager.LayoutParams.WRAP_CONTENT,
                overlayType(),
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                        | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
                        | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT);
        params.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        params.y = dp(36);
        try {
            windowManager.addView(ticker, params);
            ticker.setVisibility(View.GONE);
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
        bubble.setOnClickListener(v -> setExpanded(true));

        overlayRoot.addView(chatPanel, new LinearLayout.LayoutParams(dp(300), dp(350)));
        overlayRoot.addView(bubble, new LinearLayout.LayoutParams(dp(58), dp(58)));
        setExpanded(false);

        WindowManager.LayoutParams params = new WindowManager.LayoutParams(
                dp(300), WindowManager.LayoutParams.WRAP_CONTENT,
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
        params.width = dp(300);
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

    private void showNext() {
        if (animating || ticker == null || messages.isEmpty())
            return;
        String next = messages.poll();
        ticker.setText(next);
        ticker.measure(
                View.MeasureSpec.makeMeasureSpec(0, View.MeasureSpec.UNSPECIFIED),
                View.MeasureSpec.makeMeasureSpec(dp(64), View.MeasureSpec.AT_MOST));
        int textWidth = Math.max(dp(80), ticker.getMeasuredWidth());
        int screenWidth = getResources().getDisplayMetrics().widthPixels;
        WindowManager.LayoutParams params = (WindowManager.LayoutParams) ticker.getLayoutParams();
        params.width = textWidth;
        try {
            windowManager.updateViewLayout(ticker, params);
        } catch (RuntimeException ignored) {
            animating = false;
            return;
        }
        ticker.setTranslationX(-textWidth);
        ticker.setVisibility(View.VISIBLE);
        animating = true;
        float density = getResources().getDisplayMetrics().density;
        float speedPixelsPerSecond = 42f * density;
        long duration = Math.max(3500L, (long) ((screenWidth + textWidth) / speedPixelsPerSecond * 1000f));
        ticker.animate().cancel();
        ticker.animate()
                .translationX(screenWidth)
                .setDuration(duration)
                .setInterpolator(new LinearInterpolator())
                .withEndAction(() -> {
                    ticker.setVisibility(View.GONE);
                    animating = false;
                    handler.postDelayed(this::showNext, 250);
                })
                .start();
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
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}