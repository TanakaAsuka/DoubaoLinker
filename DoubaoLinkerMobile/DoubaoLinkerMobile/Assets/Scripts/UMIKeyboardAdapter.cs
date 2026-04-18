using UnityEngine;

public class InputKeyboardAdapter : MonoBehaviour
{
    public RectTransform bottomPanel;
    public Canvas canvas;

    [SerializeField] float pollInterval = 0.2f;

    float velocity;
    float cachedTargetY;
    float pollTimer;

#if UNITY_ANDROID && !UNITY_EDITOR
    AndroidJavaObject decorView;
    AndroidJavaObject rootView;
    AndroidJavaObject visibleRect;
#endif

    void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        using (var unity = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
        using (var activity = unity.GetStatic<AndroidJavaObject>("currentActivity"))
        using (var window = activity.Call<AndroidJavaObject>("getWindow"))
        {
            decorView = window.Call<AndroidJavaObject>("getDecorView");
            rootView = decorView.Call<AndroidJavaObject>("getRootView");
            visibleRect = new AndroidJavaObject("android.graphics.Rect");
        }
#endif
    }

    void OnDestroy()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        visibleRect?.Dispose();
        rootView?.Dispose();
        decorView?.Dispose();
#endif
    }

    void Update()
    {
        pollTimer -= Time.deltaTime;
        if (pollTimer <= 0f)
        {
            pollTimer = pollInterval;
            int kbHeight = GetKeyboardHeight();
            cachedTargetY = kbHeight > 0 ? ToCanvasY(kbHeight) : 0f;
        }

        float y = Mathf.SmoothDamp(
            bottomPanel.anchoredPosition.y,
            cachedTargetY,
            ref velocity,
            0.08f
        );

        bottomPanel.anchoredPosition = new Vector2(bottomPanel.anchoredPosition.x, y);
    }

    int GetKeyboardHeight()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (decorView == null) return 0;

        decorView.Call("getWindowVisibleDisplayFrame", visibleRect);
        int visibleHeight = visibleRect.Call<int>("height");
        int totalHeight = rootView.Call<int>("getHeight");
        int diff = totalHeight - visibleHeight;

        return diff > totalHeight * 0.15f ? diff : 0;
#else
        return 0;
#endif
    }

    float ToCanvasY(int keyboardHeight)
    {
        float canvasHeight = ((RectTransform)canvas.transform).sizeDelta.y;
        return keyboardHeight * (canvasHeight / Screen.height);
    }
}
