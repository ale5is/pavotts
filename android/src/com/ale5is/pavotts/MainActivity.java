package com.ale5is.pavotts;

import android.os.Bundle;
import org.qtproject.qt.android.bindings.QtActivity;

public class MainActivity extends QtActivity {
    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
    }

    @Override
    protected void onResume() {
        super.onResume();
        OverlayBridge.onActivityResumed(this);
    }
}