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
        public Vector3 previousPosition;
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
    private float particleReleaseAccumulator = 0f;
    // Spatial grid for fast neighbor search
    private Dictionary<Vector3Int, List<int>> spatialGrid = new Dictionary<Vector3Int, List<int>>();
    private float gridSize;

    void Start()
    {
        //  التعديل المتوافق مع جميع نسخ يونيتي لمنع خطأ الـ Vector3
        if (paintCanvas == null)
            paintCanvas = GameObject.FindAnyObjectByType<PaintCanvas>();

        PrecomputeKernels();
        gridSize = smoothingRadius;
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

    void BuildSpatialGrid()
    {
        spatialGrid.Clear();

        for (int i = 0; i < particles.Count; i++)
        {
            Vector3Int cell = GetGridCell(particles[i].position);

            if (!spatialGrid.ContainsKey(cell))
                spatialGrid[cell] = new List<int>();

            spatialGrid[cell].Add(i);
        }
    }


    Vector3Int GetGridCell(Vector3 position)
    {
        return new Vector3Int(
            Mathf.FloorToInt(position.x / gridSize),
            Mathf.FloorToInt(position.y / gridSize),
            Mathf.FloorToInt(position.z / gridSize)
        );
    }


    IEnumerable<int> GetNearbyParticles(Vector3 position)
    {
        Vector3Int center = GetGridCell(position);

        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector3Int cell = new Vector3Int(
                        center.x + x,
                        center.y + y,
                        center.z + z
                    );

                    if (spatialGrid.TryGetValue(cell, out List<int> list))
                    {
                        foreach (int index in list)
                            yield return index;
                    }
                }
            }
        }
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
                        previousPosition = bucket.TransformPoint(lp),
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
    public void SetLiquidColor(Color color)
    {
        liquidColor = color;

        if (particleMat != null)
        {
            particleMat.color = color;
        }
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
        BuildSpatialGrid();

        int n = particles.Count;

        for (int i = 0; i < n; i++)
        {
            if (particles[i].isFree)
                continue;

            float rho = 0f;

            foreach (int j in GetNearbyParticles(particles[i].position))
            {
                if (particles[j].isFree)
                    continue;

                float d = Vector3.Distance(
                    particles[i].position,
                    particles[j].position
                );

                if (d < h)
                {
                    float diff = h2 - d * d;

                    rho += particleMass *
                           poly6C *
                           diff * diff *
                           diff;
                }
            }

            particles[i].density = Mathf.Max(rho, 0.5f);

            particles[i].pressure =
                pressureStiffness *
                (particles[i].density - restDensity);
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

            foreach (int j in GetNearbyParticles(p.position))
            {
                if (i == j || particles[j].isFree)
                    continue;

                Vector3 rv = p.position - particles[j].position;
                float d = rv.magnitude;

                if (d >= h || d < 0.0001f)
                    continue;


                float avgP =
                    (p.pressure + particles[j].pressure) /
                    (2f * Mathf.Max(particles[j].density, 0.5f));


                fP += -(particleMass * avgP) *
                    (spikyC * (h - d) * (h - d) / d) * rv;


                fV += viscosity * particleMass *
                    ((particles[j].velocity - p.velocity) /
                    Mathf.Max(particles[j].density, 0.5f)) *
                    (viscC * (h - d));
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

        float canvasWorldY = paintCanvas.transform.position.y;
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            var p = particles[i];
            if (p.isFree)
            {
                p.age += dt;
                p.velocity.y -= 9.81f * dt;
                p.previousPosition = p.position;
                p.position += p.velocity * dt;
                if (p.position.y <= canvasWorldY)
                {
                    paintCanvas.RegisterImpact(
                        p.position,
                        p.velocity.magnitude
                    );

                    ResetToBucket(p);
                    particles[i] = p;
                    continue;
                }
                if (p.age > 3f)
                {
                    ResetToBucket(p);
                }
            }
            particles[i] = p;
        }
    }

    void CheckHoleRelease()
    {
        if (pendulum == null || holePoint == null)
            return;

        if (pendulum.CurrentPaintMass <= 0.01f)
            return;

        //--------------------------------------------------
        // Compute release rate from physics
        //--------------------------------------------------

        float flowRate =
            Mathf.Clamp01(
                pendulum.CurrentPaintMass /
                pendulum.initialPaintMass);

        flowRate *= Mathf.Lerp(
            2f,
            20f,
            holeRadius / 0.06f);

        particleReleaseAccumulator +=
            flowRate * Time.fixedDeltaTime;

        //--------------------------------------------------
        // Release particles
        //--------------------------------------------------

        while (particleReleaseAccumulator >= 1f)
        {
            particleReleaseAccumulator -= 1f;

            int bestIndex = -1;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < particles.Count; i++)
            {
                if (particles[i].isFree)
                    continue;

                float d = Vector3.Distance(
                    particles[i].position,
                    holePoint.position);

                if (d < bestDistance)
                {
                    bestDistance = d;
                    bestIndex = i;
                }
            }

            if (bestIndex == -1)
                return;

            FluidParticle p = particles[bestIndex];

            p.isFree = true;
            p.age = 0f;

            p.position = holePoint.position;

            float fluidHeight =
                Mathf.Max(bucketHeight * 0.5f, 0.05f);

            float exitVelocity =
                Mathf.Sqrt(2f * 9.81f * fluidHeight);

            Vector3 exitDirection = holePoint.forward;

            p.velocity =
                exitDirection * exitVelocity +
                bucketVelocity;

            particles[bestIndex] = p;
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
        p.previousPosition = p.position;
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