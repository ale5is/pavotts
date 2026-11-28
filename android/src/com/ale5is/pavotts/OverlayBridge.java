package com.ale5is.pavotts;

import android.app.Activity;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;

public final class OverlayBridge {
    private static final String PREFS = "pavotts_overlay";
    private static final String ENABLED = "enabled";
    private static final String AUDIO_ENABLED = "audio_enabled";
    private static final String TICKER_ENABLED = "ticker_enabled";

    private OverlayBridge() {}

    public static void setEnabled(Context context, boolean enabled) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .edit().putBoolean(ENABLED, enabled).apply();
        if (enabled && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M
                && !Settings.canDrawOverlays(context)) {
            Intent permission = new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                    Uri.parse("package:" + context.getPackageName()));
            permission.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            context.startActivity(permission);
            reconcile(context);
            return;
        }
        reconcile(context);
    }

    public static void setAudioEnabled(Context context, boolean enabled) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .edit().putBoolean(AUDIO_ENABLED, enabled).apply();
        reconcile(context);
    }

    public static void setTickerEnabled(Context context, boolean enabled) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .edit().putBoolean(TICKER_ENABLED, enabled).apply();
        if (enabled && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M
                && !Settings.canDrawOverlays(context)
                && !context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                        .getBoolean(ENABLED, false)) {
            Intent permission = new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                    Uri.parse("package:" + context.getPackageName()));
            permission.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            context.startActivity(permission);
            reconcile(context);
            return;
        }
        reconcile(context);
    }

    public static void onActivityResumed(Activity activity) {
        boolean overlay = activity.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .getBoolean(ENABLED, false);
        boolean audio = activity.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .getBoolean(AUDIO_ENABLED, false);
        boolean ticker = activity.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .getBoolean(TICKER_ENABLED, false);
        if ((overlay || ticker) && Build.VERSION.SDK_INT >= Build.VERSION_CODES.M
                && !Settings.canDrawOverlays(activity))
            return;
        if (overlay || ticker || audio)
            reconcile(activity);
    }

    public static void update(Context context, String message) {
        android.content.SharedPreferences prefs = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
        if (!prefs.getBoolean(ENABLED, false) && !prefs.getBoolean(TICKER_ENABLED, false))
            return;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && !Settings.canDrawOverlays(context))
            return;
        Intent intent = new Intent(context, ChatOverlayService.class);
        intent.setAction(ChatOverlayService.ACTION_MESSAGE);
        intent.putExtra(ChatOverlayService.EXTRA_MESSAGE, message);
        startService(context, intent);
    }

    private static void reconcile(Context context) {
        boolean overlay = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .getBoolean(ENABLED, false);
        boolean audio = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .getBoolean(AUDIO_ENABLED, false);
        boolean ticker = context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .getBoolean(TICKER_ENABLED, false);
        if (!overlay && !ticker && !audio) {
            context.stopService(new Intent(context, ChatOverlayService.class));
            return;
        }
        startService(context, new Intent(context, ChatOverlayService.class));
    }

    private static void startService(Context context, Intent intent) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O)
            context.startForegroundService(intent);
        else
            context.startService(intent);
    }
}