using UnityEngine;

public class GlobalLfos : MonoBehaviour
{
    public static GlobalLfos Instance { get; private set; }

    private float time = 0.0f;

    [System.Serializable]
    public struct LfoDefinition
    {
        public string name;
        public float frequency;
        public float amplitude;
        [Range(0f, 1f)] public float phase;
        public float shape;
    }

    [SerializeField] private LfoDefinition[] lfos;

    private float[] values;

    public int Count => values != null ? values.Length : 0;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
        EnsureValueBuffer();
    }

    void OnValidate()
    {
        // Keeps buffer size in sync when you add/remove elements in the Inspector (Edit mode)
        if (lfos != null && (values == null || values.Length != lfos.Length))
            values = new float[lfos.Length];
    }

    void Update()
    {
        if (lfos == null || values == null) return;

        time += Time.deltaTime;
        for (int i = 0; i < lfos.Length; i++)
        {
            var d = lfos[i];
            values[i] = ControlFunctions.Lfo(time, d.frequency, d.amplitude, d.phase, d.shape);
        }
    }

    public float Get(int index)
    {
        if (values == null || index < 0 || index >= values.Length)
            return 0f;
        return values[index];
    }

    public float Get(string lfoName)
    {
        if (lfos == null || values == null || string.IsNullOrEmpty(lfoName))
            return 0f;

        for (int i = 0; i < lfos.Length; i++)
        {
            if (lfos[i].name == lfoName)
                return values[i];
        }
        return 0f;
    }

    void EnsureValueBuffer()
    {
        int n = lfos != null ? lfos.Length : 0;
        values = new float[n];
    }
}