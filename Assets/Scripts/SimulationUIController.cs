using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// يربط لوحة التحكم (UI Canvas) بمكونات المحاكاة المحدثة بنظام SPH 3D
/// </summary>
public class SimulationUIController : MonoBehaviour
{
    [Header("References - مكونات المحاكاة")]
    public PendulumPhysics pendulum;
    public PaintCanvas paintCanvas;
    public PaintStream paintStream;
    public FluidSPH3D fluidSPH3D; // مرجع محرك السائل ثلاثي الأبعاد الجديد

    [Header("Sliders - Live (تأثير فوري)")]
    [Tooltip("الجاذبية g - تؤثر فوراً")]
    public Slider gravitySlider;
    [Tooltip("مقاومة الهواء (Air Drag) - تؤثر فوراً")]
    public Slider dragSlider;

    [Header("Sliders - Need Apply Button (تحتاج زر التطبيق)")]
    [Tooltip("طول الحبل - شرط ابتدائي، يحتاج زر التطبيق")]
    public Slider ropeLengthSlider;
    [Tooltip("لزوجة السائل - شرط ابتدائي، يحتاج زر التطبيق")]
    public Slider viscositySlider;
    [Tooltip("نصف قطر فتحة الدلو - شرط ابتدائي، يحتاج زر التطبيق")]
    public Slider holeRadiusSlider;
    [Tooltip("الالسرعة الابتدائية (مكوّن Z) عند إطلاق الدلو")]
    public Slider initialVelocitySlider;
    [Tooltip("الزاوية الابتدائية بالدرجات")]
    public Slider initialAngleSlider;
    [Tooltip("زر تطبيق القيم الابتدائية وإعادة بدء التجربة")]
    public Button applyAndResetButton;

    [Header("لوحة اختيار الألوان")]
    public Button redButton;
    public Button blueButton;
    public Button greenButton;
    public Button yellowButton;

    [Header("نصوص عرض القيم (TextMeshPro)")]
    public TextMeshProUGUI ropeLengthLabel;
    public TextMeshProUGUI gravityLabel;
    public TextMeshProUGUI dragLabel;
    public TextMeshProUGUI viscosityLabel;
    public TextMeshProUGUI holeRadiusLabel;
    public TextMeshProUGUI initialVelocityLabel;
    public TextMeshProUGUI initialAngleLabel;

    void Start()
    {
        InitializeSliderValues();
        BindLiveSliders();
        BindPendingSliders();
        BindColorButtons();
    }

    private void InitializeSliderValues()
    {
        if (pendulum == null) return;

        if (ropeLengthSlider != null) ropeLengthSlider.value = pendulum.ropeLength;
        if (gravitySlider != null) gravitySlider.value = pendulum.g;
        if (dragSlider != null) dragSlider.value = pendulum.airDragCoeff;
        if (viscositySlider != null) viscositySlider.value = pendulum.fluidViscosity;
        if (holeRadiusSlider != null) holeRadiusSlider.value = pendulum.holeRadius;
        if (initialVelocitySlider != null) initialVelocitySlider.value = pendulum.initialVelocity.z;
        if (initialAngleSlider != null) initialAngleSlider.value = pendulum.initialAngleDegrees;

        UpdateAllLabels();
    }

    private void BindLiveSliders()
    {
        if (gravitySlider != null)
            gravitySlider.onValueChanged.AddListener(OnGravityChanged);

        if (dragSlider != null)
            dragSlider.onValueChanged.AddListener(OnDragChanged);
    }

    public void OnGravityChanged(float value)
    {
        if (pendulum != null) pendulum.g = value;
        if (gravityLabel != null) gravityLabel.text = $"Gravity: {value:0.00} m/s2";
    }

    public void OnDragChanged(float value)
    {
        if (pendulum != null) pendulum.airDragCoeff = value;
        if (dragLabel != null) dragLabel.text = $"Air Drag: {value:0.000}";
    }

    private void BindPendingSliders()
    {
        if (ropeLengthSlider != null)
            ropeLengthSlider.onValueChanged.AddListener(v =>
            {
                if (ropeLengthLabel != null) ropeLengthLabel.text = $"Rope Length: {v:0.00} m";
            });

        if (viscositySlider != null)
            viscositySlider.onValueChanged.AddListener(v =>
            {
                if (viscosityLabel != null) viscosityLabel.text = $"Viscosity: {v:0.000}";
            });

        if (holeRadiusSlider != null)
            holeRadiusSlider.onValueChanged.AddListener(v =>
            {
                if (holeRadiusLabel != null) holeRadiusLabel.text = $"Hole Radius: {v:0.003} m";
            });

        if (initialVelocitySlider != null)
            initialVelocitySlider.onValueChanged.AddListener(v =>
            {
                if (initialVelocityLabel != null) initialVelocityLabel.text = $"Initial Velocity: {v:0.00} m/s";
            });

        if (initialAngleSlider != null)
            initialAngleSlider.onValueChanged.AddListener(v =>
            {
                if (initialAngleLabel != null) initialAngleLabel.text = $"Initial Angle: {v:0.0} deg";
            });

        if (applyAndResetButton != null)
            applyAndResetButton.onClick.AddListener(OnApplyAndReset);
    }

    public void OnApplyAndReset()
    {
        if (pendulum == null) return;

        float newLength = ropeLengthSlider != null ? ropeLengthSlider.value : pendulum.ropeLength;
        float newViscosity = viscositySlider != null ? viscositySlider.value : pendulum.fluidViscosity;
        float newHoleRadius = holeRadiusSlider != null ? holeRadiusSlider.value : pendulum.holeRadius;
        float newSpeedZ = initialVelocitySlider != null ? initialVelocitySlider.value : pendulum.initialVelocity.z;
        float newAngle = initialAngleSlider != null ? initialAngleSlider.value : pendulum.initialAngleDegrees;

        pendulum.ResetPendulum(newLength, newViscosity, newHoleRadius, newSpeedZ, newAngle);

        if (paintCanvas != null) paintCanvas.ClearCanvas();

        // إعادة تصفير وتوليد الجزيئات ثلاثية الأبعاد فوراً داخل الدلو عند الضغط على الزر
        if (fluidSPH3D != null)
        {
            fluidSPH3D.SendMessage("Start", SendMessageOptions.DontRequireReceiver);
        }

        UpdateAllLabels();
    }

    private void BindColorButtons()
    {
        if (redButton != null) redButton.onClick.AddListener(() => SetPaintColor(Color.red));
        if (blueButton != null) blueButton.onClick.AddListener(() => SetPaintColor(Color.blue));
        if (greenButton != null) greenButton.onClick.AddListener(() => SetPaintColor(Color.green));
        if (yellowButton != null) yellowButton.onClick.AddListener(() => SetPaintColor(Color.yellow));
    }

    public void SetPaintColor(Color color)
    {
        if (paintCanvas != null) paintCanvas.paintColor = color;
        if (paintStream != null) paintStream.SetColor(color);
    }

    private void UpdateAllLabels()
    {
        if (pendulum == null) return;

        if (ropeLengthLabel != null) ropeLengthLabel.text = $"Rope Length: {pendulum.ropeLength:0.00} m";
        if (gravityLabel != null) gravityLabel.text = $"Gravity: {pendulum.g:0.00} m/s2";
        if (dragLabel != null) dragLabel.text = $"Air Drag: {pendulum.airDragCoeff:0.000}";
        if (viscosityLabel != null) viscosityLabel.text = $"Viscosity: {pendulum.fluidViscosity:0.000}";
        if (holeRadiusLabel != null) holeRadiusLabel.text = $"Hole Radius: {pendulum.holeRadius:0.003} m";
        if (initialVelocityLabel != null) initialVelocityLabel.text = $"Initial Velocity: {pendulum.initialVelocity.z:0.00} m/s";
        if (initialAngleLabel != null) initialAngleLabel.text = $"Initial Angle: {pendulum.initialAngleDegrees:0.0} deg";
    }
}