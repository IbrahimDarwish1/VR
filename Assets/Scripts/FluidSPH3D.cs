using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// FluidSPH3D —
/// </summary>
public class FluidSPH3D : MonoBehaviour
{
    [Header("المراجع")]
    public Transform bucket;
    public Transform holePoint;
    public PendulumPhysics pendulum;
    public PaintCanvas paintCanvas;

    [Header("إعدادات SPH")]
    public int totalParticleCount = 120;
    public float smoothingRadius = 0.09f;
    public float restDensity = 600f;
    public float pressureStiffness = 30f;
    public float viscosity = 6f;
    public float particleMass = 0.02f;

    [Header("هندسة الدلو")]
    public float bucketRadius = 0.16f;
    public float bucketHeight = 0.35f;
    public float holeRadius = 0.06f;
    [Range(0f, 0.4f)]
    public float wallFriction = 0.2f;

    [Header("الأداء والاستقرار")]
    [Range(1, 6)]
    public int sphSubSteps = 3;
    public float maxForce = 40f;

    [Header("العرض البصري")]
    public bool showParticles = true;
    public Color liquidColor = new Color(0.85f, 0.1f, 0.1f, 0.9f);
    public float particleVisualRadius = 0.013f;

    public class FluidParticle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float density;
        public float pressure;
        public bool isFree;
        public float age;
    }

    private List<FluidParticle> particles = new List<FluidParticle>();
    private float h, h2, poly6C, spikyC, viscC;
    private Mesh particleMesh;
    private Material particleMat;
    private Vector3 lastBucketPos;
    private Vector3 bucketVelocity;
    private Vector3 bucketAcceleration;
    private Vector3 lastBucketVelocity;
    private int impactCount = 0;

    void Start()
    {
        //  التعديل المتوافق مع جميع نسخ يونيتي لمنع خطأ الـ Vector3
        if (paintCanvas == null)
            paintCanvas = GameObject.FindAnyObjectByType<PaintCanvas>();

        PrecomputeKernels();
        SpawnParticlesGrid();
        SetupVisuals();

        lastBucketPos = bucket != null ? bucket.position : Vector3.zero;
        Debug.Log($"[FluidSPH3D] تهيئة: {particles.Count} جزيئة. h={h:F3}, stiffness={pressureStiffness}");
    }

    void PrecomputeKernels()
    {
        h = smoothingRadius;
        h2 = h * h;
        float h6 = Mathf.Pow(h, 6f);
        float h9 = Mathf.Pow(h, 9f);
        poly6C = 315f / (64f * Mathf.PI * h9);
        spikyC = -45f / (Mathf.PI * h6);
        viscC = 45f / (Mathf.PI * h6);
    }

    void SpawnParticlesGrid()
    {
        particles.Clear();
        float spacing = smoothingRadius * 0.85f;
        int spawned = 0;

        for (int ix = -10; ix <= 10 && spawned < totalParticleCount; ix++)
            for (int iy = -10; iy <= 10 && spawned < totalParticleCount; iy++)
                for (int iz = -10; iz <= 10 && spawned < totalParticleCount; iz++)
                {
                    Vector3 lp = new Vector3(ix * spacing, iy * spacing * 0.65f, iz * spacing);
                    if (new Vector2(lp.x, lp.z).magnitude > bucketRadius * 0.8f) continue;
                    if (lp.y > bucketHeight * 0.38f || lp.y < -bucketHeight * 0.38f) continue;

                    particles.Add(new FluidParticle
                    {
                        position = bucket.TransformPoint(lp),
                        velocity = Vector3.zero,
                        density = restDensity,
                        pressure = 0f,
                        isFree = false,
                        age = 0f
                    });
                    spawned++;
                }
    }

    void SetupVisuals()
    {
        var tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        particleMesh = tmp.GetComponent<MeshFilter>().sharedMesh;
        Destroy(tmp);

        Shader s = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        particleMat = new Material(s);
        particleMat.color = liquidColor;
        particleMat.enableInstancing = true;
    }

    void FixedUpdate()
    {
        if (bucket == null) return;

        float fullDt = Time.fixedDeltaTime;
        Vector3 newBucketVelocity = (bucket.position - lastBucketPos) / fullDt;
        bucketAcceleration = (newBucketVelocity - bucketVelocity) / fullDt;

        lastBucketVelocity = bucketVelocity;
        bucketVelocity = newBucketVelocity;
        lastBucketPos = bucket.position;

        float sphDt = fullDt / sphSubSteps;
        for (int step = 0; step < sphSubSteps; step++)
        {
            ComputeDensityPressure();
            ApplySPHForces(sphDt);
        }

        if (pendulum != null)
        {
            Vector3 fluidForce = ComputeBucketFluidForce();
            Vector3 fluidTorque = ComputeBucketFluidTorque();

            pendulum.AddFluidForce(fluidForce);
            pendulum.AddFluidTorque(fluidTorque);
        }

        UpdateFreeParticles(fullDt);
        CheckHoleRelease();
        FixNaN();
    }

    void ComputeDensityPressure()
    {
        int n = particles.Count;
        for (int i = 0; i < n; i++)
        {
            if (particles[i].isFree) continue;
            float rho = 0f;
            for (int j = 0; j < n; j++)
            {
                if (particles[j].isFree) continue;
                float d = Vector3.Distance(particles[i].position, particles[j].position);
                if (d < h)
                {
                    float diff = h2 - d * d;
                    rho += particleMass * poly6C * diff * diff * diff;
                }
            }
            particles[i].density = Mathf.Max(rho, 0.5f);
            particles[i].pressure = pressureStiffness * (particles[i].density - restDensity);
        }
    }

    void ApplySPHForces(float dt)
    {
        int n = particles.Count;
        Vector3 grav = Vector3.down * 9.81f;

        for (int i = 0; i < n; i++)
        {
            var p = particles[i];
            if (p.isFree) continue;

            Vector3 fP = Vector3.zero, fV = Vector3.zero;

            for (int j = 0; j < n; j++)
            {
                if (i == j || particles[j].isFree) continue;
                Vector3 rv = p.position - particles[j].position;
                float d = rv.magnitude;
                if (d >= h || d < 0.0001f) continue;

                float avgP = (p.pressure + particles[j].pressure) / (2f * Mathf.Max(particles[j].density, 0.5f));
                fP += -(particleMass * avgP) * (spikyC * (h - d) * (h - d) / d) * rv;

                fV += viscosity * particleMass * ((particles[j].velocity - p.velocity) / Mathf.Max(particles[j].density, 0.5f)) * (viscC * (h - d));
            }
            Vector3 inertia = -bucketAcceleration * particleMass * (pressureStiffness / 200f);
            Vector3 total = fP + fV + grav * particleMass + inertia;

            if (total.magnitude > maxForce)
                total = total.normalized * maxForce;

            Vector3 acc = total / Mathf.Max(p.density, 0.5f);
            p.velocity += acc * dt;
            p.velocity *= 0.97f;

            p.position += p.velocity * dt;
            p.position = ConstrainToBucket(p.position, ref p.velocity);
            particles[i] = p;
        }
    }

    void UpdateFreeParticles(float dt)
    {
        if (paintCanvas == null) return;

        // إطلاق الشعاع من الدلو لمطابقة الخيط البصري تماماً
        Vector3 rayOrigin = bucket.position;

        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 30f))
        {
            if (hit.collider.gameObject == paintCanvas.gameObject || hit.collider.GetComponent<PaintCanvas>() != null)
            {
                // ريشة رسم ممتازة وموزونة لمنع التقطع
                paintCanvas.RegisterImpact(hit.point, 12.0f);
                impactCount++;
            }
        }

        float canvasWorldY = paintCanvas.transform.position.y;
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];
            if (p.isFree)
            {
                p.age += dt;
                p.velocity.y -= 9.81f * dt;
                p.position += p.velocity * dt;
                if (p.position.y <= canvasWorldY - 1f || p.age > 3f)
                {
                    ResetToBucket(p);
                }
            }
            particles[i] = p;
        }
    }

    void CheckHoleRelease()
    {
        //  تم تصحيح الاستدعاء هنا ليطابق المتغير pendulum تماماً وحل مشكلة الـ Context
        bool hasPaint = (pendulum == null) || (pendulum.CurrentPaintMass > 0.01f);
        if (!hasPaint) return;

        float bucketSpeed = bucketVelocity.magnitude;
        float releaseChance = Mathf.Clamp01(0.02f + bucketSpeed * 0.03f);

        for (int i = 0; i < particles.Count; i++)
        {
            var p = particles[i];
            if (p.isFree) continue;

            Vector3 local = bucket.InverseTransformPoint(p.position);
            float distH = new Vector2(local.x, local.z).magnitude;

            bool nearHole = local.y <= -bucketHeight * 0.40f && distH <= holeRadius;
            if (!nearHole) continue;

            if (Random.value < releaseChance)
            {
                p.isFree = true;
                p.age = 0f;
                if (holePoint != null)
                {
                    p.position = holePoint.position;
                }
                float fluidH = Mathf.Max(bucketHeight * 0.5f, 0.05f);
                float exitV = Mathf.Sqrt(2f * 9.81f * fluidH) * 0.4f;
                Vector3 exitDirection = Vector3.down;

                if (holePoint != null)
                {
                    exitDirection = holePoint.forward;
                }

                p.velocity = exitDirection * exitV
                             + bucketVelocity * 0.8f;

                particles[i] = p;
            }
        }
    }

    Vector3 ConstrainToBucket(Vector3 wp, ref Vector3 vel)
    {
        Vector3 lp = bucket.InverseTransformPoint(wp);
        Vector3 lv = bucket.InverseTransformDirection(vel);
        float hy = bucketHeight * 0.5f;

        if (lp.y > hy) { lp.y = hy; lv.y = Mathf.Min(lv.y, 0) * wallFriction; }
        if (lp.y < -hy) { lp.y = -hy; lv.y = Mathf.Max(lv.y, 0) * wallFriction; }

        Vector2 flat = new Vector2(lp.x, lp.z);
        if (flat.magnitude > bucketRadius * 0.93f)
        {
            flat = flat.normalized * bucketRadius * 0.93f;
            lp.x = flat.x; lp.z = flat.y;
            Vector2 fv = new Vector2(lv.x, lv.z);
            fv = Vector2.Reflect(fv, -flat.normalized) * wallFriction;
            lv.x = fv.x; lv.z = fv.y;
        }

        vel = bucket.TransformDirection(lv);
        return bucket.TransformPoint(lp);
    }

    void ResetToBucket(FluidParticle p)
    {
        float r = bucketRadius * Mathf.Sqrt(Random.value) * 0.55f;
        float t = Random.value * Mathf.PI * 2f;
        float y = Random.Range(-bucketHeight * 0.25f, bucketHeight * 0.2f);
        p.position = bucket.TransformPoint(new Vector3(r * Mathf.Cos(t), y, r * Mathf.Sin(t)));
        p.velocity = Vector3.zero;
        p.isFree = false;
        p.age = 0f;
        p.density = restDensity;
        p.pressure = 0f;
    }

    void FixNaN()
    {
        for (int i = 0; i < particles.Count; i++)
        {
            var p = particles[i];
            bool bad = float.IsNaN(p.position.x) || float.IsInfinity(p.position.x)
                    || float.IsNaN(p.velocity.x) || float.IsInfinity(p.velocity.x);
            if (bad) { ResetToBucket(p); particles[i] = p; }
        }
    }

    void Update()
    {
        if (!showParticles || particleMesh == null) return;

        var mats = new List<Matrix4x4>(Mathf.Min(particles.Count, 1023));
        foreach (var p in particles)
        {
            if (mats.Count >= 1023) break;
            if (float.IsNaN(p.position.x) || float.IsInfinity(p.position.x)) continue;
            mats.Add(Matrix4x4.TRS(p.position, Quaternion.identity, Vector3.one * particleVisualRadius * 2f));
        }
        if (mats.Count > 0)
            Graphics.DrawMeshInstanced(particleMesh, 0, particleMat, mats);
    }
    Vector3 ComputeBucketFluidForce()
    {
        Vector3 force = Vector3.zero;
        float influenceRadius = smoothingRadius * 1.2f;

        for (int i = 0; i < particles.Count; i++)
        {
            var p = particles[i];
            if (p.isFree) continue;

            Vector3 toBucket = bucket.position - p.position;
            float d = toBucket.magnitude;

            if (d < influenceRadius && d > 0.0001f)
            {
                float densityFactor = p.density / restDensity;
                float pressureFactor = Mathf.Max(0, p.pressure);

                // smooth falloff (important for stability)
                float falloff = 1f - (d / influenceRadius);
                falloff = falloff * falloff;

                Vector3 dir = toBucket / d;

                force += dir * pressureFactor * densityFactor * falloff * particleMass;
            }
        }

        return force;
    }

    Vector3 ComputeBucketFluidTorque()
    {
        Vector3 torque = Vector3.zero;

        if (bucket == null)
            return torque;


        Vector3 center = bucket.position;

        for (int i = 0; i < particles.Count; i++)
        {
            FluidParticle p = particles[i];

            if (p.isFree)
                continue;


            Vector3 forceDir = (center - p.position).normalized;

            float pressureForce =
                Mathf.Max(0, p.pressure) * particleMass;


            Vector3 force = forceDir * pressureForce;


            // lever arm
            Vector3 r = p.position - center;


            torque += Vector3.Cross(r, force);
        }


        return torque;
    }
}