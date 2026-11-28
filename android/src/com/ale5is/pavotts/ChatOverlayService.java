package com.ale5is.pavotts;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.Service;
import android.content.Intent;
import android.graphics.Color;
import android.graphics.PixelFormat;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.view.Gravity;
import android.view.WindowManager;
import android.view.animation.LinearInterpolator;
import android.widget.TextView;
import java.util.ArrayDeque;
import java.util.Queue;

public final class ChatOverlayService extends Service {
    public static final String ACTION_MESSAGE = "com.ale5is.pavotts.OVERLAY_MESSAGE";
    public static final String EXTRA_MESSAGE = "message";
    private static final String CHANNEL_ID = "pavotts_overlay";
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Queue<String> messages = new ArrayDeque<>();
    private WindowManager windowManager;
    private TextView ticker;
    private boolean animating;

    @Override
    public void onCreate() {
        super.onCreate();
        windowManager = (WindowManager) getSystemService(WINDOW_SERVICE);
        createNotificationChannel();
        Notification notification = new Notification.Builder(this, CHANNEL_ID)
                .setSmallIcon(android.R.drawable.ic_dialog_info)
                .setContentTitle("PavoTTS")
                .setContentText("La cinta de chat está activa")
                .setOngoing(true)
                .build();
        startForeground(41, notification);
        createTicker();
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        if (intent != null && ACTION_MESSAGE.equals(intent.getAction())) {
            String message = intent.getStringExtra(EXTRA_MESSAGE);
            if (message != null && !message.trim().isEmpty()) {
                if (messages.size() >= 100)
                    messages.poll();
                messages.offer(message);
                showNext();
            }
        }
        return START_STICKY;
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
                WindowManager.LayoutParams.MATCH_PARENT,
                WindowManager.LayoutParams.WRAP_CONTENT,
                Build.VERSION.SDK_INT >= Build.VERSION_CODES.O
                        ? WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY
                        : WindowManager.LayoutParams.TYPE_PHONE,
                WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE
                        | WindowManager.LayoutParams.FLAG_NOT_TOUCH_MODAL
                        | WindowManager.LayoutParams.FLAG_LAYOUT_IN_SCREEN,
                PixelFormat.TRANSLUCENT);
        params.gravity = Gravity.TOP | Gravity.CENTER_HORIZONTAL;
        params.y = dp(36);
        try {
            windowManager.addView(ticker, params);
            ticker.setVisibility(TextView.GONE);
        } catch (RuntimeException ignored) {
            stopSelf();
        }
    }

    private void showNext() {
        if (animating || ticker == null || messages.isEmpty())
            return;
        String next = messages.poll();
        ticker.setText(next);
        ticker.measure(
                android.view.View.MeasureSpec.makeMeasureSpec(0, android.view.View.MeasureSpec.UNSPECIFIED),
                android.view.View.MeasureSpec.makeMeasureSpec(dp(64), android.view.View.MeasureSpec.AT_MOST));
        int textWidth = Math.max(dp(80), ticker.getMeasuredWidth());
        int screenWidth = getResources().getDisplayMetrics().widthPixels;
        WindowManager.LayoutParams params = (WindowManager.LayoutParams) ticker.getLayoutParams();
        params.width = textWidth;
        try {
            windowManager.updateViewLayout(ticker, params);
        } catch (RuntimeException ignored) {
            animating = false;
            stopSelf();
            return;
        }
        ticker.setTranslationX(screenWidth);
        ticker.setVisibility(TextView.VISIBLE);
        animating = true;
        float density = getResources().getDisplayMetrics().density;
        float speedPixelsPerSecond = 42f * density;
        long duration = Math.max(3500L, (long) ((screenWidth + textWidth) / speedPixelsPerSecond * 1000f));
        ticker.animate().cancel();
        ticker.animate()
                .translationX(-textWidth)
                .setDuration(duration)
                .setInterpolator(new LinearInterpolator())
                .withEndAction(() -> {
                    ticker.setVisibility(TextView.GONE);
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
                    CHANNEL_ID, "Superposición de chat", NotificationManager.IMPORTANCE_LOW);
            getSystemService(NotificationManager.class).createNotificationChannel(channel);
        }
    }

    @Override
    public void onDestroy() {
        handler.removeCallbacksAndMessages(null);
        if (ticker != null) {
            ticker.animate().cancel();
            try {
                windowManager.removeView(ticker);
            } catch (RuntimeException ignored) {}
            ticker = null;
        }
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) {
        return null;
    }
}