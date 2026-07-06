using UnityEngine;
using TMPro;

public class BucketPhysicsMonitor : MonoBehaviour
{
    [Header("UI Elements")]
    public TextMeshProUGUI speedText;
    public TextMeshProUGUI angleText;
    public TextMeshProUGUI dragText;
    public TextMeshProUGUI densityText;

    [Header("Physics Reference")]
    // هنا نستخدم اسم السكربت الخاص بك تماماً
    public PendulumPhysics pendulumScript; 

    void Update()
    {
        if (pendulumScript != null)
        {
            // حساب الزاوية بناءً على موضع الدلو بالنسبة للمركز
            Vector3 direction = pendulumScript.transform.position - pendulumScript.upcenter.position;
            float angle = Vector3.Angle(Vector3.down, direction);

            // تحديث النصوص بالقيم الحقيقية من كود الفيزياء
            speedText.text = $"Speed: {pendulumScript.CurrentVelocity.magnitude:F2} m/s";
            angleText.text = $"Angle: {angle:F2}°";
            dragText.text = $"Air Drag: {pendulumScript.airDragCoeff:F2}";
            densityText.text = $"Paint Density: {pendulumScript.paintDensity:F2}";
        }
    }
}