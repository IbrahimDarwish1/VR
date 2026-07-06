using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// FluidDemoBox — النسخة المحمية بالكامل ضد انهيار الشيدرز والـ NaN
/// </summary>
public class FluidDemoBox : MonoBehaviour
{
    [Header("أبعاد الصندوق")]
    public Vector3 boxSize   = new Vector3(1.2f, 1.5f, 1.2f);
    [Range(0.1f, 0.85f)]
    public float fillRatio   = 0.55f;

    [Header("إعدادات SPH")]
    public int   particleCount      = 100000;
    public float smoothingRadius    = 0.12f;
    public float restDensity        = 600f;       
    public float pressureStiffness  = 30f;        
    public float viscosity          = 1f;         
    public float particleMass       = 0.02f;
    public float gravity            = 9.81f;
    [Range(0f, 0.6f)]
    public float wallFriction       = 0.3f;

    [Header("التفاعل")]
    public bool  allowManualMove    = true;
    public float moveSensitivity    = 0.012f;
    public bool  autoShakeDemo      = true;
    public float shakeAmplitude     = 0.7f;       
    public float shakeSpeed         = 2.5f;       

    [Header("العرض والماتيريال")]
    public Material customParticleMaterial;       
    public Color liquidColor        = new Color(0.15f, 0.35f, 0.9f, 0.88f);
    public float particleRadius     = 0.028f;
    public Color wireColor          = new Color(1f, 1f, 1f, 0.55f);

    private struct Particle
    {
        public Vector3 pos, vel;
        public float   rho, pressure;
    }

    private List<Particle> parts = new List<Particle>();
    private float h, h2, p6C, spC, vC;
    private Vector3 lastPos, initPos, boxVel;
    private Mesh     spMesh;
    private Material spMat;
    private LineRenderer wire;

    void Start()
    {
        initPos = lastPos = transform.position;
        KernelSetup();
        SpawnParticles();
        SetupVisuals();
        SetupWireframe();
    }

    void KernelSetup()
    {
        h   = Mathf.Max(smoothingRadius, 0.01f);
        h2  = h * h;
        p6C = 315f / (64f * Mathf.PI * Mathf.Pow(h, 9f));
        spC = -45f / (Mathf.PI * Mathf.Pow(h, 6f));
        vC  =  45f / (Mathf.PI * Mathf.Pow(h, 6f));
    }

    void SpawnParticles()
    {
        parts.Clear();
        float fillH = boxSize.y * fillRatio;
        float sp    = h * 0.88f;
        int   rows  = Mathf.FloorToInt(fillH / sp);
        int   cols  = Mathf.FloorToInt(boxSize.x / sp);
        int   deps  = Mathf.FloorToInt(boxSize.z / sp);
        int   count = 0;

        for (int iy = 0; iy < rows && count < particleCount; iy++)
        for (int ix = 0; ix < cols && count < particleCount; ix++)
        for (int iz = 0; iz < deps && count < particleCount; iz++)
        {
            Vector3 lp = new Vector3(
                (ix + 0.5f) * sp - boxSize.x * 0.5f,
                (iy + 0.5f) * sp - boxSize.y * 0.5f,
                (iz + 0.5f) * sp - boxSize.z * 0.5f);
            lp += Random.insideUnitSphere * sp * 0.08f;
            parts.Add(new Particle { pos=transform.TransformPoint(lp), vel=Vector3.zero, rho=restDensity });
            count++;
        }
    }

    void SetupVisuals()
    {
        try
        {
            var t = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            spMesh = t.GetComponent<MeshFilter>().sharedMesh;
            Destroy(t);
            
            if (customParticleMaterial != null)
            {
                spMat = customParticleMaterial;
            }
            else
            {
                Shader s = Shader.Find("Universal Render Pipeline/Unlit") 
                        ?? Shader.Find("Universal Render Pipeline/Lit") 
                        ?? Shader.Find("Unlit/Color") 
                        ?? Shader.Find("Hidden/Internal-Colored");
                
                spMat = new Material(s);
                spMat.color = liquidColor;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("تحذير في الماتيريال الافتراضي: " + ex.Message);
        }
    }

    void SetupWireframe()
    {
        try
        {
            var go = new GameObject("Wire_Box");
            go.transform.SetParent(transform, false);
            wire = go.AddComponent<LineRenderer>();
            
            Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default") ?? Shader.Find("Hidden/Internal-Colored");
            if (s != null) wire.material = new Material(s);
            
            wire.startColor = wire.endColor = wireColor;
            wire.startWidth = wire.endWidth = 0.01f;
            wire.useWorldSpace = false;
            
            Vector3 e = boxSize * 0.5f;
            var pts = new Vector3[] {
                new(-e.x,-e.y,-e.z), new(e.x,-e.y,-e.z), new(e.x,-e.y,e.z), new(-e.x,-e.y,e.z), new(-e.x,-e.y,-e.z),
                new(-e.x,e.y,-e.z),  new(e.x,e.y,-e.z),  new(e.x,-e.y,-e.z), new(e.x,e.y,-e.z),
                new(e.x,e.y,e.z),    new(e.x,-e.y,e.z),   new(e.x,e.y,e.z),
                new(-e.x,e.y,e.z),   new(-e.x,-e.y,e.z),  new(-e.x,e.y,e.z),  new(-e.x,e.y,-e.z)
            };
            wire.positionCount = pts.Length;
            wire.SetPositions(pts);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("تعذر رسم الشبكة السلكية مؤقتاً لكن المحاكاة ستستمر: " + ex.Message);
        }
    }

    void Update()
    {
        if (autoShakeDemo) AutoShake();
        RenderParticles();
    }

    void AutoShake()
    {
        float t = Time.time * shakeSpeed;
        transform.position = initPos + new Vector3(
            Mathf.Sin(t)        * shakeAmplitude,
            0f,
            Mathf.Cos(t * 0.8f) * shakeAmplitude * 0.5f);
    }

    void FixedUpdate()
    {
        if (parts.Count == 0) return;
        float dt = Time.fixedDeltaTime;
        boxVel    = (transform.position - lastPos) / dt;
        lastPos   = transform.position;

        ComputeRho();
        ComputeForces(dt);
        FixNaN(); 
    }

    float  W6(float d)         { if(d>=h) return 0f; float q=h2-d*d; return p6C*q*q*q; }
    Vector3 Gsp(Vector3 r,float d){ if(d>=h||d<.0001f) return Vector3.zero; float q=h-d; return (spC*q*q/d)*r; }
    float  Lv(float d)         { if(d>=h) return 0f; return vC*(h-d); }

    void ComputeRho()
    {
        int n = parts.Count;
        for (int i=0; i<n; i++)
        {
            float rho=0f;
            for (int j=0; j<n; j++) rho += particleMass*W6(Vector3.Distance(parts[i].pos,parts[j].pos));
            var p=parts[i]; 
            p.rho=Mathf.Max(rho, 1f); 
            p.pressure = Mathf.Max(0f, pressureStiffness * (p.rho - restDensity)); 
            parts[i]=p;
        }
    }

    void ComputeForces(float dt)
    {
        int n = parts.Count;
        Vector3 grav = Vector3.down * gravity;
        for (int i=0; i<n; i++)
        {
            var pi=parts[i];
            Vector3 fp=Vector3.zero, fv=Vector3.zero;
            for (int j=0; j<n; j++)
            {
                if(i==j) continue;
                var pj=parts[j];
                Vector3 r_vec=pi.pos-pj.pos; float d=r_vec.magnitude;
                if(d>=h||d<.0001f) continue;
                float avgP = (pi.pressure+pj.pressure)/(2f*Mathf.Max(pj.rho,1f));
                fp += -(particleMass*avgP)*Gsp(r_vec,d);
                fv += viscosity*particleMass*((pj.vel-pi.vel)/Mathf.Max(pj.rho,1f))*Lv(d);
            }
            
            Vector3 inertia = -boxVel * particleMass * 2.5f;
            Vector3 total   = fp + fv + grav*particleMass + inertia;
            
            if (total.magnitude>50f) total=total.normalized*50f;

            Vector3 acc = total / Mathf.Max(pi.rho, 1f);
            pi.vel += acc * dt;
            pi.vel *= 0.95f; 
            if (pi.vel.magnitude>5f) pi.vel=pi.vel.normalized*5f;
            pi.pos += pi.vel * dt;
            pi.pos = Clamp(pi.pos, ref pi.vel);
            parts[i]=pi;
        }
    }

    void FixNaN()
    {
        for (int i = 0; i < parts.Count; i++)
        {
            var p = parts[i];
            bool isBad = float.IsNaN(p.pos.x) || float.IsInfinity(p.pos.x) || 
                         float.IsNaN(p.vel.x) || float.IsInfinity(p.vel.x);
            if (isBad)
            {
                Vector3 lp = new Vector3(
                    Random.Range(-boxSize.x * 0.2f, boxSize.x * 0.2f),
                    Random.Range(-boxSize.y * 0.2f, boxSize.y * 0.2f),
                    Random.Range(-boxSize.z * 0.2f, boxSize.z * 0.2f)
                );
                p.pos = transform.TransformPoint(lp);
                p.vel = Vector3.zero;
                p.rho = restDensity;
                p.pressure = 0f;
                parts[i] = p;
            }
        }
    }

    Vector3 Clamp(Vector3 wp, ref Vector3 vel)
    {
        Vector3 lp=transform.InverseTransformPoint(wp);
        Vector3 lv=transform.InverseTransformDirection(vel);
        Vector3 e =boxSize*0.5f*0.94f;
        
        if(lp.x < -e.x){ lp.x = -e.x; lv.x = -lv.x * wallFriction; }
        if(lp.x >  e.x){ lp.x =  e.x; lv.x = -lv.x * wallFriction; }
        if(lp.y < -e.y){ lp.y = -e.y; lv.y = -lv.y * wallFriction; }
        if(lp.y >  e.y){ lp.y =  e.y; lv.y = -lv.y * wallFriction; }
        if(lp.z < -e.z){ lp.z = -e.z; lv.z = -lv.z * wallFriction; }
        if(lp.z >  e.z){ lp.z =  e.z; lv.z = -lv.z * wallFriction; }
        
        vel=transform.TransformDirection(lv);
        return transform.TransformPoint(lp);
    }

    void RenderParticles()
    {
        if (spMesh==null||parts.Count==0||spMat==null) return;
        
        foreach(var p in parts)
        {
            if(float.IsNaN(p.pos.x)) continue;
            Matrix4x4 matrix = Matrix4x4.TRS(p.pos, Quaternion.identity, Vector3.one * particleRadius * 2f);
            Graphics.DrawMesh(spMesh, matrix, spMat, 0);
        }
    }

    public void Impulse(float force)
    {
        Vector3 imp=new Vector3(Random.Range(-force,force),0,Random.Range(-force,force));
        for(int i=0;i<parts.Count;i++){var p=parts[i];p.vel+=imp;parts[i]=p;}
    }
}