using UnityEngine;

public class PaintStream : MonoBehaviour
{
    private LineRenderer lineRenderer;

    [Header("Settings")]
    public float canvasY = 0f;
    public Color streamColor = Color.red; // لون خيط الطلاء الساقط

    [Header("Optional - ربط بكتلة الطلاء المتبقية")]
    [Tooltip(" لو تم ربطه سيُخفى خيط الطلاء تلقائياً عندما تصل الكتلة المتبقية إلى صفر")]
    public PendulumPhysics pendulum;

    void Start()
    {
        // lineRenderer = GetComponent<LineRenderer>();

        // // إجبار الـ Line Renderer على تبني اللون المحدد برمجياً
        // if (lineRenderer != null)
        // {
        //     lineRenderer.startColor = streamColor;
        //     lineRenderer.endColor = streamColor;
        // }
    }

    void Update()
    {
        // if (lineRenderer == null) return;

        // // إخفاء خيط الطلاء عند نفاذ الكتلة (إن وُجد ربط مع PendulumPhysics)
        // if (pendulum != null)
        // {
        //     bool hasPaint = pendulum.CurrentPaintMass > 0f;
        //     if (lineRenderer.enabled != hasPaint)
        //         lineRenderer.enabled = hasPaint;

        //     if (!hasPaint) return;
        // }

        // lineRenderer.SetPosition(0, transform.position);
        // Vector3 groundPosition = new Vector3(transform.position.x, canvasY, transform.position.z);
        // lineRenderer.SetPosition(1, groundPosition);
    }

    /// <summary>
    /// تغيير لون خيط الطلاء فورياً من واجهة المستخدم (لوحة الألوان)
    /// </summary>
    public void SetColor(Color color)
    {
        // streamColor = color;
        // if (lineRenderer != null)
        // {
        //     lineRenderer.startColor = color;
        //     lineRenderer.endColor = color;
        // }
    }
}