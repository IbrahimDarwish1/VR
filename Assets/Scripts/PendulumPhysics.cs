using UnityEngine;

public class PendulumPhysics : MonoBehaviour
{
    [Header("References")]
    public Transform upcenter;
    public Transform ball;

    [Header("Rope Settings")]
    [Tooltip("إذا كانت مفعّلة، يُحسب طول الحبل تلقائياً من المسافة الحالية بين upcenter و ball في المشهد عند بدء التشغيل (بدل القيمة المكتوبة أدناه). يُستحسن تركها مفعّلة في البداية، ثم تعطيلها عند التحكم بالقيمة من واجهة المستخدم.")]
    public bool useSceneRopeLength = true;
    public float ropeLength = 5.0f;       // طول الحبل (L)

    [Header("Dynamic Inputs (For UI Control)")]
    public float fluidViscosity = 0.001f; // لزوجة السائل
    public float holeRadius = 0.008f;     // نصف قطر الفتحة (r)
    public Vector3 initialVelocity = new Vector3(0f, 0f, 4f); // السرعة الابتدائية للإطلاق الجانبي (V0)

    [Header("Initial Angle Settings")]
    [Range(10f, 60f)]
    public float initialAngleDegrees = 30f; // زاوية الإطلاق الابتدائية عن الشاقول بالدرجات

    [Header("Base Environment Constants")]
    public float g = 9.81f;
    public float dt = 0.01f;
    public float airDragCoeff = 0.05f;

    [Header("Bucket & Paint Settings")]
    public float emptyBucketMass = 0.5f;
    public float initialPaintMass = 2.5f;
    public float paintDensity = 1200f;
    public float bucketRadius = 0.15f;

    [Header("Canvas Collision (التصادم مع اللوحة)")]
    [Tooltip("ارتفاع سطح اللوحة على المحور Y - يجب أن يطابق قيمة canvasY في PaintStream")]
    public float canvasY = 0f;

    private float currentPaintMass;
    private float currentTotalMass;
    private float bucketArea;
    private float holeArea;

    private Vector3 currentPos;
    private Vector3 previousPos;

    public Vector3 CurrentVelocity { get; private set; }
    public float CurrentPaintMass => currentPaintMass;
    public bool IsStuckOnCanvas { get; private set; } = false;

    void Start()
    {

        if (upcenter == null || ball == null)
        {
            Debug.LogError(
                $"[PendulumPhysics] على الكائن '{gameObject.name}': " +
                $"upcenter = {(upcenter == null ? "غير معيّن (NULL)" : "موجود")}, " +
                $"ball = {(ball == null ? "غير معيّن (NULL)" : "موجود")}. " 
            );
            return;
        }

        if (useSceneRopeLength)
        {
            ropeLength = Vector3.Distance(upcenter.position, ball.position);
            if (ropeLength <= 0.01f)
            {
                Debug.LogWarning(
                    $"[PendulumPhysics] على الكائن '{gameObject.name}': " +
                    "المسافة بين upcenter و ball قريبة من الصفر في المشهد. " 
                );
            }
        }

        InitializePendulumValues();
    }

    private void InitializePendulumValues()
    {
        if (upcenter == null || ball == null) return;

        // إعادة تعيين حالة الالتصاق باللوحة عند بدء تجربة جديدة
        IsStuckOnCanvas = false;

        currentPaintMass = initialPaintMass;
        currentTotalMass = emptyBucketMass + currentPaintMass;

        // حساب الإزاحة الفيزيائية الابتدائية باستخدام حساب المثلثات (الزاوية الابتدائية عن الشاقول)
        float angleRad = initialAngleDegrees * Mathf.Deg2Rad;

        // حساب مركبات الإزاحة (الدلو مائل في الفضاء بناءً على طول الحبل والزاوية المحددة)
        float offsetX = ropeLength * Mathf.Sin(angleRad);
        float offsetY = -ropeLength * Mathf.Cos(angleRad);
        float offsetZ = 0f; // نبدأ الإزاحة على محور X ونترك محور Z للسرعة الابتدائية ليدور حلزونياً

        Vector3 startingOffset = new Vector3(offsetX, offsetY, offsetZ);

        currentPos = upcenter.position + startingOffset;
        ball.position = currentPos;

        // حساب الموقع السابق بدقة اعتماداً على السرعة الابتدائية المحددة لمنع السكون
        previousPos = currentPos - (initialVelocity * dt);
        CurrentVelocity = initialVelocity;
    }

    void FixedUpdate()
    {
        if (upcenter == null || ball == null) return;

        // إذا كان الدلو قد التصق باللوحة سابقاً، نوقف كل حساب فيزيائي إضافي للحركة
        // (يبقى الموضع ثابتاً تماماً - سلوك Inelastic Collision كما في الدراسة المرجعية)
        if (IsStuckOnCanvas)
        {
            return;
        }

        // الحفاظ على استقرار توجيه الفتحة لأسفل
        ball.rotation = Quaternion.identity;

        // إعادة حساب المساحات في كل إطار (بدل مرة واحدة عند البدء) لدعم

        // تغيير نصف قطر الدلو/الفتحة فورياً من واجهة المستخدم (Sliders)
        bucketArea = Mathf.PI * bucketRadius * bucketRadius;
        holeArea = Mathf.PI * holeRadius * holeRadius;

        // تطبيق قانون توريشيلي لتدفق السائل
        if (currentPaintMass > 0)
        {
            float paintVolume = currentPaintMass / paintDensity;
            float fluidHeight = paintVolume / bucketArea;

            float exitVelocity = Mathf.Sqrt(2f * g * fluidHeight) * (1.0f - fluidViscosity * 10f);
            exitVelocity = Mathf.Max(exitVelocity, 0f);

            float volumeDischarged = holeArea * exitVelocity * dt;

            currentPaintMass -= volumeDischarged * paintDensity;
            if (currentPaintMass < 0) currentPaintMass = 0;
        }

        currentTotalMass = emptyBucketMass + currentPaintMass;

        // حساب السرعة الحركية اللحظية من مواقع فيرليه
        CurrentVelocity = (currentPos - previousPos) / dt;

        // مصفوفة القوى (الجاذبية ومقاومة الوسط)
        Vector3 gravityForce = new Vector3(0, -currentTotalMass * g, 0);
        Vector3 dragForce = -(airDragCoeff + (fluidViscosity * 0.1f)) * CurrentVelocity;
        Vector3 totalExternalForce = gravityForce + dragForce;

        Vector3 acceleration = totalExternalForce / currentTotalMass;

        // تكامل فيرليه الأساسي للحساب التنبؤي
        Vector3 nextPos = (2f * currentPos) - previousPos + (acceleration * dt * dt);

        // التقييد  لطول الحبل الثابت (Constrained Verlet) لضمان حركة دائرية ناعمة
        Vector3 toNext = nextPos - upcenter.position;
        Vector3 constrainedNextPos = upcenter.position + toNext.normalized * ropeLength;

        // تحديث مصفوفة المواقع للإطار القادم
        previousPos = currentPos;
        currentPos = constrainedNextPos;

        // فحص الاصطدام مع اللوحة: إذا وصل الدلو إلى ارتفاع سطح اللوحة (canvasY) أو تجاوزه،
        // نثبّت موضعه عند سطح اللوحة تماماً ونعتبره ملتصقاً (سلوك السوائل اللزجة - Inelastic Collision)
        if (currentPos.y <= canvasY)
        {
            currentPos.y = canvasY;
            ball.position = currentPos;
            IsStuckOnCanvas = true;
            return;
        }

        // نقل الإحداثيات إلى عنصر العرض الرسومي
        ball.position = currentPos;
    }

    public void ResetPendulum(float newLength, float newViscosity, float newHoleRadius, float newSpeedZ, float newAngle)
    {
        ropeLength = newLength;
        fluidViscosity = newViscosity;
        holeRadius = newHoleRadius;
        initialVelocity = new Vector3(0f, 0f, newSpeedZ);
        initialAngleDegrees = newAngle;

        // عند إعادة الضبط من واجهة المستخدم نستخدم القيمة المُمرَّرة وليس قياس المشهد من جديد
        useSceneRopeLength = false;

        InitializePendulumValues();
    }
}