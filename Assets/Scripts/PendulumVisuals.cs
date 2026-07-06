using UnityEngine;

public class PendulumVisuals : MonoBehaviour
{
    [Header("References")]
    public Transform upcenter;       // نقطة التعليق العلوية
    public Transform ball;           // الكرة (الدلو)
    public Transform ropeContainer;  // حاوية الحبل

    [Header("Visual Settings")]
    [Tooltip("التحكم في سماكة الحبل")]
    public float ropeThickness = 0.12f; // قمنا بزيادتها من 0.05 إلى 0.12 لتصبح أوضح

    void LateUpdate()
    {
        if (upcenter == null || ball == null || ropeContainer == null) return;

        // 1. حساب المتجه والمسافة بين نقطة التعليق والدلو
        Vector3 direction = ball.position - upcenter.position;
        float distance = direction.magnitude;

        // 2. وضع الحبل في المنتصف تماماً بين النقطتين
        ropeContainer.position = upcenter.position + (direction / 2f);

        // 3. توجيه الحبل ليتطلع نحو الدلو دائماً
        ropeContainer.up = direction.normalized;

        // 4. ضبط السماكة بناءً على المتغير الجديد والطول بناءً على المسافة الحالية
        ropeContainer.localScale = new Vector3(ropeThickness, distance / 2f, ropeThickness);
    }
}