package com.winremote.app;

import android.app.Activity;
import android.app.DownloadManager;
import android.content.SharedPreferences;
import android.graphics.Color;
import android.graphics.Typeface;
import android.graphics.drawable.GradientDrawable;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.view.Gravity;
import android.view.View;
import android.view.WindowInsets;
import android.view.WindowInsetsController;
import android.view.WindowManager;
import android.webkit.CookieManager;
import android.webkit.DownloadListener;
import android.webkit.URLUtil;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.widget.Button;
import android.widget.EditText;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.TextView;

import java.io.BufferedReader;
import java.io.InputStreamReader;
import java.net.HttpURLConnection;
import java.net.URL;

/**
 * 远程桥客户端（简洁轻柔版 v4）：
 * 浅灰底 + 小Logo + 浅蓝按钮，克制留白，不堆动画
 * 任何异常都不闪退：统一兜底显示提示
 */
public class MainActivity extends Activity {

    private static final String PREFS = "winremote";
    private static final String KEY_IP = "base_ip";
    private static final String KEY_PIN = "base_pin";
    private static final String DEFAULT_IP = "192.168.1.100:8899";  // 改成你自己电脑的IP（见电脑主窗口）
    private static final int REQ_FILE = 1001;   // 网页选择文件（上传）请求码

    // 轻柔色板
    private static final int C_BG = 0xFFF8F9FA;        // 页面浅灰
    private static final int C_FIELD = 0xFFF1F3F4;     // 输入框浅灰底
    private static final int C_TEXT = 0xFF202124;      // 主文字
    private static final int C_SUB = 0xFF80868B;       // 次要文字
    private static final int C_BTN = 0xFFE8F0FE;       // 按钮浅蓝底
    private static final int C_BTN_TX = 0xFF1A73E8;    // 按钮深蓝字
    private static final int C_BTN2 = 0xFFF1F3F4;      // 次按钮浅灰底
    private static final int C_BTN2_TX = 0xFF5F6368;   // 次按钮深灰字
    private static final int C_GREEN = 0xFF188038;     // 成功
    private static final int C_RED = 0xFFD93025;       // 失败

    private LinearLayout startPage;
    private WebView web;
    private TextView statusText;
    private EditText ipEdit;
    private EditText pinEdit;
    private Button connectBtn;
    private Button webBtn;
    private Button browserBtn;
    private SharedPreferences sp;
    private String baseAddr = "";
    private String authCookie = "";
    private ValueCallback<android.net.Uri[]> filePathCallback;   // 网页"选择文件"回调

    @Override
    protected void attachBaseContext(android.content.Context newBase) {
        super.attachBaseContext(newBase);
        // 最早注册全局兜底：任何未捕获异常都不闪退
        Thread.setDefaultUncaughtExceptionHandler(new Thread.UncaughtExceptionHandler() {
            @Override
            public void uncaughtException(Thread t, Throwable e) {
                try {
                    e.printStackTrace();
                } catch (Throwable ignored) {
                }
                try {
                    android.content.Intent i = new android.content.Intent(MainActivity.this, MainActivity.class);
                    i.addFlags(android.content.Intent.FLAG_ACTIVITY_NEW_TASK | android.content.Intent.FLAG_ACTIVITY_CLEAR_TASK);
                    MainActivity.this.startActivity(i);
                } catch (Throwable ignored) {
                }
            }
        });
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        try {
            sp = getSharedPreferences(PREFS, MODE_PRIVATE);
            enterImmersive();
            getWindow().addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
            buildStartPage();
            setContentView(startPage);
            autoConnect();
        } catch (Throwable e) {
            e.printStackTrace();
            showError("启动失败：" + e.getClass().getSimpleName());
        }
    }

    private void showError(String msg) {
        try {
            TextView tv = new TextView(this);
            tv.setText(msg);
            tv.setTextColor(C_TEXT);
            tv.setTextSize(15);
            tv.setGravity(Gravity.CENTER);
            tv.setPadding(dp(24), dp(24), dp(24), dp(24));
            LinearLayout root = new LinearLayout(this);
            root.setGravity(Gravity.CENTER);
            root.setBackgroundColor(C_BG);
            root.addView(tv);
            setContentView(root);
        } catch (Throwable ignored) {
        }
    }

    // ---- 全屏沉浸 ----
    private void enterImmersive() {
        try {
            if (Build.VERSION.SDK_INT >= 30) {
                getWindow().getInsetsController().hide(
                        WindowInsets.Type.statusBars() | WindowInsets.Type.navigationBars());
                getWindow().getInsetsController().setSystemBarsBehavior(
                        WindowInsetsController.BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE);
            } else {
                getWindow().getDecorView().setSystemUiVisibility(
                        View.SYSTEM_UI_FLAG_IMMERSIVE_STICKY
                                | View.SYSTEM_UI_FLAG_FULLSCREEN
                                | View.SYSTEM_UI_FLAG_HIDE_NAVIGATION);
            }
        } catch (Throwable ignored) {
        }
    }

    // ---- 圆角工具 ----
    private GradientDrawable round(int color, int radiusDp) {
        GradientDrawable d = new GradientDrawable();
        d.setColor(color);
        d.setCornerRadius(dp(radiusDp));
        return d;
    }

    // ---- 启动页（极简轻柔） ----
    private void buildStartPage() {
        startPage = new LinearLayout(this);
        startPage.setOrientation(LinearLayout.VERTICAL);
        startPage.setGravity(Gravity.CENTER_HORIZONTAL);
        startPage.setBackgroundColor(C_BG);
        startPage.setPadding(dp(36), dp(88), dp(36), dp(40));

        // 小 Logo
        ImageView logo = new ImageView(this);
        try {
            logo.setImageResource(R.mipmap.ic_launcher);
        } catch (Throwable ignored) {
        }
        startPage.addView(logo, new LinearLayout.LayoutParams(dp(44), dp(44)));

        // 标题（克制字号）
        TextView title = new TextView(this);
        title.setText("远程桥");
        title.setTextColor(C_TEXT);
        title.setTextSize(20);
        title.setGravity(Gravity.CENTER);
        startPage.addView(title, lpMargin(0, dp(12), 0, 0));

        TextView sub = new TextView(this);
        sub.setText("手机遥控你的电脑");
        sub.setTextColor(C_SUB);
        sub.setTextSize(12);
        sub.setGravity(Gravity.CENTER);
        startPage.addView(sub, lpMargin(0, dp(4), 0, 0));

        // 状态文字
        statusText = new TextView(this);
        statusText.setText("正在连接电脑…");
        statusText.setTextColor(C_SUB);
        statusText.setTextSize(13);
        statusText.setGravity(Gravity.CENTER);
        startPage.addView(statusText, lpMargin(0, dp(26), 0, 0));

        // 地址输入（浅灰圆角，无边框）
        ipEdit = new EditText(this);
        ipEdit.setText(sp.getString(KEY_IP, DEFAULT_IP));
        ipEdit.setTextColor(C_TEXT);
        ipEdit.setHintTextColor(0xFF9AA0A6);
        ipEdit.setTextSize(14);
        ipEdit.setSingleLine(true);
        ipEdit.setGravity(Gravity.CENTER);
        ipEdit.setBackground(round(C_FIELD, 10));
        ipEdit.setPadding(dp(12), dp(12), dp(12), dp(12));
        startPage.addView(ipEdit, lpMargin(0, dp(14), 0, 0));

        // 密码输入（留空 = 无密码）
        pinEdit = new EditText(this);
        pinEdit.setHint("访问密码（留空 = 无密码）");
        pinEdit.setText(sp.getString(KEY_PIN, ""));
        pinEdit.setTextColor(C_TEXT);
        pinEdit.setHintTextColor(0xFF9AA0A6);
        pinEdit.setTextSize(14);
        pinEdit.setSingleLine(true);
        pinEdit.setGravity(Gravity.CENTER);
        pinEdit.setInputType(android.text.InputType.TYPE_CLASS_NUMBER
                | android.text.InputType.TYPE_NUMBER_VARIATION_PASSWORD);
        pinEdit.setBackground(round(C_FIELD, 10));
        pinEdit.setPadding(dp(12), dp(12), dp(12), dp(12));
        startPage.addView(pinEdit, lpMargin(0, dp(10), 0, 0));

        // 主按钮（浅蓝底深蓝字，轻柔）
        connectBtn = makeSoftBtn("连接电脑", C_BTN, C_BTN_TX, 14);
        connectBtn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                connect();
            }
        });
        startPage.addView(connectBtn, lpMargin(0, dp(10), 0, 0));

        // 连接成功后才出现（默认隐藏）
        webBtn = makeSoftBtn("进入控制台 · App 内打开", C_BTN, C_BTN_TX, 14);
        webBtn.setVisibility(View.GONE);
        webBtn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (!baseAddr.isEmpty()) switchToWeb("http://" + baseAddr);
            }
        });
        startPage.addView(webBtn, lpMargin(0, dp(6), 0, 0));

        browserBtn = makeSoftBtn("用系统浏览器打开", C_BTN2, C_BTN2_TX, 13);
        browserBtn.setVisibility(View.GONE);
        browserBtn.setOnClickListener(new View.OnClickListener() {
            @Override
            public void onClick(View v) {
                if (!baseAddr.isEmpty()) openBrowser("http://" + baseAddr + "/remote");
            }
        });
        startPage.addView(browserBtn, lpMargin(0, dp(6), 0, 0));

        // 底部提示（小字）
        TextView tip = new TextView(this);
        tip.setText("手机 WiFi 需和电脑连同一个路由器\n地址格式：192.168.x.x:8899（看电脑主窗口）");
        tip.setTextColor(0xFF9AA0A6);
        tip.setTextSize(11);
        tip.setGravity(Gravity.CENTER);
        tip.setLineSpacing(dp(2), 1f);
        startPage.addView(tip, lpMargin(0, dp(28), 0, 0));
    }

    private Button makeSoftBtn(String text, int bg, int fg, float size) {
        Button b = new Button(this);
        b.setText(text);
        b.setTextColor(fg);
        b.setTextSize(size);
        b.setBackground(round(bg, 10));
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, dp(46));
        lp.setMargins(0, 0, 0, 0);
        b.setLayoutParams(lp);
        return b;
    }

    private LinearLayout.LayoutParams lpMargin(int l, int t, int r, int b) {
        LinearLayout.LayoutParams lp = new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
        lp.setMargins(l, t, r, b);
        return lp;
    }

    private int dp(int v) {
        return (int) (v * getResources().getDisplayMetrics().density + 0.5f);
    }

    private void autoConnect() {
        String saved = sp.getString(KEY_IP, DEFAULT_IP);
        ipEdit.setText(saved);
        connect();
    }

    private void connect() {
        String base = ipEdit.getText().toString().trim().replace("http://", "").replace("/", "");
        if (base.isEmpty()) {
            statusText.setText("请输入电脑地址");
            return;
        }
        // 只填了 IP 没填端口时，自动补默认端口
        if (!base.contains(":")) {
            base = base + ":8899";
            ipEdit.setText(base);
        }
        final String pin = pinEdit.getText().toString().trim();
        sp.edit().putString(KEY_IP, base).putString(KEY_PIN, pin).apply();
        baseAddr = base;
        statusText.setTextColor(C_SUB);
        statusText.setText("正在连接 " + base + " …");
        connectBtn.setEnabled(false);
        final String fBase = base;
        final String fPin = pin;
        new Thread(new Runnable() {
            @Override
            public void run() {
                final boolean ok = pingWithAuth(fBase, fPin);
                runOnUiThread(new Runnable() {
                    @Override
                    public void run() {
                        connectBtn.setEnabled(true);
                        if (ok) {
                            statusText.setTextColor(C_GREEN);
                            statusText.setText("已连上电脑，请选择打开方式：");
                            webBtn.setVisibility(View.VISIBLE);
                            browserBtn.setVisibility(View.VISIBLE);
                        } else {
                            statusText.setTextColor(C_RED);
                            statusText.setText("连不上电脑，请检查：\n1. 手机 WiFi 和电脑是不是同一个路由器\n2. 电脑上 RemoteBridge 是否在运行\n3. 地址是否填对（看电脑主窗口的内网IP）\n4. 密码是否填对（电脑端可点“设置”查看/修改）");
                            webBtn.setVisibility(View.GONE);
                            browserBtn.setVisibility(View.GONE);
                        }
                    }
                });
            }
        }).start();
    }

    private void openBrowser(String url) {
        try {
            android.content.Intent i = new android.content.Intent(
                    android.content.Intent.ACTION_VIEW, android.net.Uri.parse(url));
            startActivity(i);
        } catch (Throwable e) {
            showError("无法打开浏览器，请手动访问\n" + url);
        }
    }

    /** 连接电脑：留空密码=无密码直连；填了密码先登录拿凭证（cookie），网页控制台免再输 */
    private boolean pingWithAuth(String base, String pin) {
        try {
            if (pin.isEmpty()) {
                // 无密码：直接检查
                HttpURLConnection c = (HttpURLConnection) new URL("http://" + base + "/api/check").openConnection();
                c.setConnectTimeout(4000);
                c.setReadTimeout(4000);
                c.setRequestMethod("GET");
                int code = c.getResponseCode();
                if (code == 200) {
                    BufferedReader r = new BufferedReader(new InputStreamReader(c.getInputStream()));
                    String line = r.readLine();
                    r.close();
                    c.disconnect();
                    return line != null && line.contains("\"ok\":true");
                }
                c.disconnect();
                return false;
            }
            // 有密码：先登录拿 rb_sess cookie
            HttpURLConnection c = (HttpURLConnection) new URL("http://" + base + "/api/pin?pin=" + pin).openConnection();
            c.setConnectTimeout(4000);
            c.setReadTimeout(4000);
            c.setRequestMethod("GET");
            int code = c.getResponseCode();
            String setCookie = c.getHeaderField("Set-Cookie");
            c.disconnect();
            if (code != 200) return false;
            String token = extractCookie(setCookie);
            if (!token.isEmpty()) {
                authCookie = token;
                saveCookieForWeb(base, token);
            }
            return true;
        } catch (Exception e) {
            return false;
        }
    }

    /** 从 Set-Cookie 里取出 rb_sess=xxx; 部分 */
    private String extractCookie(String setCookie) {
        if (setCookie == null) return "";
        int i = setCookie.indexOf("rb_sess=");
        if (i < 0) return "";
        int j = setCookie.indexOf(";", i);
        if (j < 0) j = setCookie.length();
        return setCookie.substring(i, j).trim();
    }

    /** 把登录凭证写进 CookieManager，内嵌控制台自动带上 */
    private void saveCookieForWeb(String base, String token) {
        try {
            android.webkit.CookieManager cm = android.webkit.CookieManager.getInstance();
            cm.setAcceptCookie(true);
            cm.setCookie("http://" + base, token + "; path=/");
            if (android.os.Build.VERSION.SDK_INT >= 21) cm.flush();
        } catch (Throwable ignored) {
        }
    }

    private boolean ping(String url) {
        try {
            HttpURLConnection c = (HttpURLConnection) new URL(url).openConnection();
            c.setConnectTimeout(4000);
            c.setReadTimeout(4000);
            c.setRequestMethod("GET");
            int code = c.getResponseCode();
            if (code == 200) {
                BufferedReader r = new BufferedReader(new InputStreamReader(c.getInputStream()));
                String line = r.readLine();
                r.close();
                c.disconnect();
                return line != null && line.contains("\"ok\":true");
            }
            c.disconnect();
            return false;
        } catch (Exception e) {
            return false;
        }
    }

    // ---- 全屏控制台 ----
    private void switchToWeb(String base) {
        try {
            web = new WebView(this);
            WebSettings s = web.getSettings();
            s.setJavaScriptEnabled(true);
            s.setDomStorageEnabled(true);
            s.setCacheMode(WebSettings.LOAD_NO_CACHE);
            s.setMediaPlaybackRequiresUserGesture(false);
            if (Build.VERSION.SDK_INT >= 21) {
                s.setMixedContentMode(WebSettings.MIXED_CONTENT_ALWAYS_ALLOW);
            }
            web.setWebViewClient(new WebViewClient());
            web.setWebChromeClient(new WebChromeClient() {
                // 网页点"上传/选择文件"时，弹出手机文件选择器
                @Override
                public boolean onShowFileChooser(WebView wv, ValueCallback<android.net.Uri[]> filePath,
                                                 WebChromeClient.FileChooserParams params) {
                    if (filePathCallback != null) {
                        filePathCallback.onReceiveValue(null);
                    }
                    filePathCallback = filePath;
                    try {
                        android.content.Intent i = new android.content.Intent(
                                android.content.Intent.ACTION_GET_CONTENT);
                        i.addCategory(android.content.Intent.CATEGORY_OPENABLE);
                        i.setType("*/*");
                        startActivityForResult(
                                android.content.Intent.createChooser(i, "选择要上传到电脑的文件"), REQ_FILE);
                        return true;
                    } catch (Throwable e) {
                        filePathCallback = null;
                        return false;
                    }
                }
            });
            // 网页点"下载"时，用系统下载管理器保存到手机"下载"目录
            web.setDownloadListener(new DownloadListener() {
                @Override
                public void onDownloadStart(String url, String userAgent, String contentDisposition,
                                            String mimetype, long contentLength) {
                    try {
                        DownloadManager dm = (DownloadManager) getSystemService(DOWNLOAD_SERVICE);
                        DownloadManager.Request req = new DownloadManager.Request(android.net.Uri.parse(url));
                        req.setNotificationVisibility(
                                DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
                        String name = URLUtil.guessFileName(url, contentDisposition, mimetype);
                        req.setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, name);
                        String ck = CookieManager.getInstance().getCookie(url);
                        if (ck != null) req.addRequestHeader("Cookie", ck);
                        dm.enqueue(req);
                    } catch (Throwable ignored) {
                    }
                }
            });
            setContentView(web);
            web.loadUrl(base + "/remote");
        } catch (Throwable e) {
            e.printStackTrace();
            showError("当前设备不支持内嵌网页，请用浏览器打开\n" + base + "/remote");
        }
    }

    @Override
    public void onBackPressed() {
        finish();
    }

    // 网页"选择文件"的回调结果：把选中的手机文件交给网页上传
    @Override
    protected void onActivityResult(int requestCode, int resultCode, android.content.Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode == REQ_FILE) {
            if (filePathCallback == null) return;
            android.net.Uri[] result = null;
            if (resultCode == RESULT_OK && data != null) {
                android.net.Uri u = data.getData();
                if (u != null) result = new android.net.Uri[]{u};
            }
            filePathCallback.onReceiveValue(result);
            filePathCallback = null;
        }
    }

    @Override
    public void onWindowFocusChanged(boolean hasFocus) {
        super.onWindowFocusChanged(hasFocus);
        if (hasFocus) enterImmersive();
    }
}
