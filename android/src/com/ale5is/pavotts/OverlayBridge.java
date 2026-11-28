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

    private OverlayBridge() {}

    public static void setEnabled(Context context, boolean enabled) {
        context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .edit().putBoolean(ENABLED, enabled).apply();
        if (!enabled) {
            context.stopService(new Intent(context, ChatOverlayService.class));
            return;
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && !Settings.canDrawOverlays(context)) {
            Intent permission = new Intent(Settings.ACTION_MANAGE_OVERLAY_PERMISSION,
                    Uri.parse("package:" + context.getPackageName()));
            permission.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            context.startActivity(permission);
            return;
        }
        start(context);
    }

    public static void onActivityResumed(Activity activity) {
        if (!activity.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .getBoolean(ENABLED, false))
            return;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && !Settings.canDrawOverlays(activity))
            return;
        start(activity);
    }

    public static void update(Context context, String message) {
        if (!context.getSharedPreferences(PREFS, Context.MODE_PRIVATE)
                .getBoolean(ENABLED, false))
            return;
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M && !Settings.canDrawOverlays(context))
            return;
        Intent intent = new Intent(context, ChatOverlayService.class);
        intent.setAction(ChatOverlayService.ACTION_MESSAGE);
        intent.putExtra(ChatOverlayService.EXTRA_MESSAGE, message);
        startService(context, intent);
    }

    private static void start(Context context) {
        Intent intent = new Intent(context, ChatOverlayService.class);
        startService(context, intent);
    }

    private static void startService(Context context, Intent intent) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O)
            context.startForegroundService(intent);
        else
            context.startService(intent);
    }
}