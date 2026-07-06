using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// PaintCanvas — 
///
/// المسؤوليات:
/// 1. RegisterImpact(): تُستدعى من FluidSPH3D عند ارتطام جزيء حقيقي
///    → رسم فوري (Stamp) + Apply فوري + توليد جزيئات SPH-2D للانتشار
/// 2. Update(): تحديث جزيئات الانتشار (SPH-2D) وطبعها على اللوحة
/// 3. WorldToPixel(): تحويل صحيح يعمل مع أي Scale
///
/// التوزيع الغاوسي:
/// دالة Stamp() تستخدم Smoothstep (مكافئة للغاوسي في التأثير البصري)
/// f = (1 - dist/R)² * (3 - 2*(1-dist/R)) → تدرج ناعم من المركز للحافة
/// </summary>
public class PaintCanvas : MonoBehaviour
{
    [Header("المراجع")]
    public Transform       ball;
    public PendulumPhysics pendulumPhysics;

    [Header("إعدادات اللوحة")]
    public int   textureSize = 1024;
    public Color paintColor  = Color.red;
    [Range(3, 20)]
    public int brushSize = 9;

    [Header("إصلاح الاتجاه - اضبط إذا كانت الأنماط معكوسة")]
    public bool flipX = false;
    public bool flipZ = false;

    [Header("SPH الانتشار (بعد الارتطام)")]
    public bool  enableSpread          = true;
    public float spreadSmoothRadius    = 22f;
    public float spreadRestDensity     = 1.0f;
    public float spreadPressureStiff   = 55f;
    public float spreadViscosity       = 0.22f;
    public int   maxSpreadParticles    = 500;
    public float spreadLifetime        = 2.0f;

    private struct SpreadP
    {
        public Vector2 pos, vel;
        public float   rho, pressure, age;
        public Color   color;
    }

    private List<SpreadP> spread = new List<SpreadP>();
    private Texture2D     tex;
    private MeshRenderer  rend;
    private float h, h2, p6C, spC, vC;
    private int   impactsReceived = 0;
    private bool  firstDraw       = true;

    void Start()
    {
        rend = GetComponent<MeshRenderer>();
        tex  = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);
        tex.filterMode = FilterMode.Bilinear;
        tex.wrapMode   = TextureWrapMode.Clamp;
        ClearCanvas();
        rend.material.mainTexture = tex;
        KernelSetup();
        Debug.Log($"[PaintCanvas] جاهز. Y={transform.position.y:F2}, Scale={transform.lossyScale}");
    }

    void KernelSetup()
    {
        h   = spreadSmoothRadius;
        h2  = h * h;
        p6C = 315f / (64f * Mathf.PI * Mathf.Pow(h, 9f));
        spC = -45f / (Mathf.PI * Mathf.Pow(h, 6f));
        vC  =  45f / (Mathf.PI * Mathf.Pow(h, 6f));
    }

    float  W6(float d)        { if(d>=h) return 0f; float q=h2-d*d; return p6C*q*q*q; }
    Vector2 Gsp(Vector2 r, float d){ if(d>=h||d<.001f) return Vector2.zero; float q=h-d; return (spC*q*q/d)*r; }
    float  Lv(float d)        { if(d>=h) return 0f; return vC*(h-d); }

    // ══════════════════════════════════════════════════════════════
    // RegisterImpact: المدخل الوحيد للرسم — يُستدعى من FluidSPH3D
    // عند ارتطام جزيء سائل حقيقي (كان يخضع لـ SPH داخل الدلو) باللوحة
    // ══════════════════════════════════════════════════════════════
    public void RegisterImpact(Vector3 worldPos, float speed)
    {
        Vector2 px = W2P(worldPos);
        if (float.IsNaN(px.x)) return;
        if (px.x < 0 || px.x >= textureSize || px.y < 0 || px.y >= textureSize) return;

        impactsReceived++;

        if (firstDraw)
        {
            firstDraw = false;
            Debug.Log($"[PaintCanvas] ✓ أول رسم! px=({px.x:F0},{px.y:F0}), speed={speed:F2}");
        }

        // ── رسم فوري بالتوزيع الغاوسي (Smoothstep) ──
        Stamp((int)px.x, (int)px.y, paintColor, 1f, brushSize);
        tex.Apply(false);

        // ── توليد جزيئات الانتشار SPH-2D ──
        if (!enableSpread || spread.Count >= maxSpreadParticles) return;

        // سرعة عالية → خط نحيف (جزيئات أقل)
        // سرعة منخفضة → بقعة سميكة (جزيئات أكثر)
        int n = Mathf.Clamp(Mathf.RoundToInt(10f / Mathf.Max(speed * 0.3f, 0.5f)), 2, 14);
        for (int i = 0; i < n && spread.Count < maxSpreadParticles; i++)
        {
            float s = spreadSmoothRadius * 0.5f;
            spread.Add(new SpreadP
            {
                pos      = px + new Vector2(Random.Range(-s,s), Random.Range(-s,s)),
                vel      = Random.insideUnitCircle * Mathf.Min(speed * 4f, 18f),
                rho      = spreadRestDensity,
                pressure = 0f,
                age      = 0f,
                color    = paintColor
            });
        }
    }

    void Update()
    {
        if (tex == null) return;
        if (!Mathf.Approximately(h, spreadSmoothRadius)) KernelSetup();

        if (enableSpread && spread.Count > 0)
        {
            StepSpread();
            bool any = false;
            foreach (var p in spread)
            {
                float a = (1f - p.age / spreadLifetime) * 0.38f;
                Stamp((int)p.pos.x, (int)p.pos.y, p.color, a, brushSize / 2);
                any = true;
            }
            if (any) tex.Apply(false);
        }
    }

    // ══════════════════════════════════════════════════════════════
    // WorldToPixel: الإصلاح الصحيح للإحداثيات
    // InverseTransformPoint → local.x ∈ [-0.5, +0.5] مع أي Scale
    // ══════════════════════════════════════════════════════════════
    public Vector2 W2P(Vector3 world)
    {
        // 1. تحويل النقطة من العالم الحقيقي إلى إحداثيات محلية تابعة للوحة تماماً
        // هذا الأمر يلغي تماماً أي تأثير لـ Position أو Rotation أو Scale خاص باللوحة في الـ Inspector
        Vector3 localPos = transform.InverseTransformPoint(world);

        // 2. الـ Plane الافتراضي في يونيتي يمتد محلياً من -5 إلى +5 في محوري X و Z (حجمه الإجمالي 10 وحدات)
        // سنحول النقطة المحلية لتصبح نسبة مئوية ناعمة بين [0, 1]
        float u = (localPos.x / 10f) + 0.5f;
        float v = (localPos.z / 10f) + 0.5f;

        // 3. إصلاح الاتجاهات إذا كانت مقلوبة من الـ Inspector
        if (flipX) u = 1f - u;
        if (flipZ) v = 1f - v;

        // 4. تحويل النسبة المئوية بدقة إلى بكسلات حقيقية على الـ Texture (مثلاً من 0 إلى 1023)
        int px = Mathf.Clamp(Mathf.RoundToInt(u * textureSize), 0, textureSize - 1);
        int py = Mathf.Clamp(Mathf.RoundToInt(v * textureSize), 0, textureSize - 1);

        return new Vector2(px, py);
    }

    // ═══════════════════════════════════════
    // SPH-2D للانتشار بعد الارتطام
    // ═══════════════════════════════════════
    void StepSpread()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.016f);
        int n = spread.Count;

        // مرحلة الكثافة
        for (int i = 0; i < n; i++)
        {
            var pi = spread[i];
            float rho = 0f;
            for (int j = 0; j < n; j++)
                rho += W6(Vector2.Distance(pi.pos, spread[j].pos));
            pi.rho      = Mathf.Max(rho, 0.001f);
            pi.pressure = spreadPressureStiff * Mathf.Max(pi.rho - spreadRestDensity, 0f);
            spread[i]   = pi;
        }

        // مرحلة القوى والحركة
        var rem = new List<int>();
        for (int i = 0; i < n; i++)
        {
            var pi = spread[i];
            pi.age += dt;
            if (pi.age > spreadLifetime) { rem.Add(i); continue; }

            Vector2 fp = Vector2.zero, fv = Vector2.zero;
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                var pj = spread[j];
                Vector2 rv = pi.pos - pj.pos;
                float   d  = rv.magnitude;
                if (d >= h || d < 0.001f) continue;
                float ap = (pi.pressure + pj.pressure) / (2f * Mathf.Max(pj.rho, 0.001f));
                fp += ap * Gsp(rv, d);
                fv += spreadViscosity * ((pj.vel - pi.vel) / Mathf.Max(pj.rho, 0.001f)) * Lv(d);
            }

            Vector2 acc = (fp + fv) / Mathf.Max(pi.rho, 0.001f);
            pi.vel  = (pi.vel + acc * dt) * 0.90f;
            pi.pos += pi.vel * dt;
            pi.pos.x = Mathf.Clamp(pi.pos.x, 0, textureSize-1);
            pi.pos.y = Mathf.Clamp(pi.pos.y, 0, textureSize-1);
            spread[i] = pi;
        }
        for (int i = rem.Count-1; i >= 0; i--) spread.RemoveAt(rem[i]);
    }

    // ══════════════════════════════════════════════════════════════
    // Stamp: فرشاة بتوزيع غاوسي (Smoothstep)
    // f(d) = (1 - d/R)² · (3 - 2(1 - d/R)) → تدرج ناعم مركزي
    // ══════════════════════════════════════════════════════════════
    void Stamp(int cx, int cy, Color col, float strength, int radius)
    {
        for (int dx = -radius; dx <= radius; dx++)
        for (int dy = -radius; dy <= radius; dy++)
        {
            float d = Mathf.Sqrt(dx*dx + dy*dy);
            if (d > radius) continue;
            int px = cx+dx, py = cy+dy;
            if (px<0||px>=textureSize||py<0||py>=textureSize) continue;

            float t = 1f - d / radius;                    // [0,1] من الحافة للمركز
            float f = t * t * (3f - 2f * t);              // Smoothstep ≈ Gaussian
            Color cur = tex.GetPixel(px, py);
            tex.SetPixel(px, py, Color.Lerp(cur, new Color(col.r,col.g,col.b,1f), strength * f));
        }
    }

    public void ClearCanvas()
    {
        if (tex == null) return;
        var blank = new Color[textureSize * textureSize];
        for (int i=0;i<blank.Length;i++) blank[i]=Color.white;
        tex.SetPixels(blank);
        tex.Apply(false);
        spread.Clear();
        firstDraw = true;
        impactsReceived = 0;
    }

    public void SetPaintColor(Color c) => paintColor = c;
    public int  GetImpactsReceived()   => impactsReceived;
}
