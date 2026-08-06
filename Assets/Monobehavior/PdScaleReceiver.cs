using UnityEngine;

public class PdScaleReceiver : MonoBehaviour
{
    [Header("Pure Data Settings")]
    [SerializeField] private LibPdInstance pdInstance;
    [SerializeField] private string receiverName = "Scale_to_Unity";

    [Header("Scaling Range")]
    [SerializeField] private float minInput = 0f;
    [SerializeField] private float maxInput = 100f;
    [SerializeField] private float minYScale = 1f;
    [SerializeField] private float maxYScale = 5f;

    [Header("Smoothing")]
    [SerializeField] private float smoothSpeed = 10f;

    private float targetScaleY;

private bool isBound = false;

void Start()
{
    targetScaleY = transform.localScale.y;

    if (pdInstance != null)
    {
        try
        {
            // Versuche das Symbol in LibPd zu binden
            // Try to bind the symbol in LibPd
            pdInstance.Bind(receiverName);
            isBound = true;
        }
        catch (System.ArgumentException)
        {
            // Das Symbol ist bereits gebunden (z. B. von einer anderen Instanz)
            // The symbol is already bound (e.g. by another instance)
            Debug.LogWarning($"[PdScaleReceiver] '{receiverName}' war bereits an LibPdInstance gebunden.");
        }

        // Das UnityEvent hören wir auf jeden Fall ab
        // Listen to the UnityEvent regardless
        pdInstance.pureDataEvents.Float.AddListener(OnReceiveFloat);
    }
    else
    {
        Debug.LogError("[PdScaleReceiver] Bitte LibPdInstance im Inspector zuweisen!");
    }
}

void OnDestroy()
{
    if (pdInstance != null)
    {
        // Nur entbinden, wenn wir es auch erfolgreich gebunden hatten
        // Only unbind if we successfully bound it
        if (isBound)
        {
            pdInstance.UnBind(receiverName);
            isBound = false;
        }
        
        pdInstance.pureDataEvents.Float.RemoveListener(OnReceiveFloat);
    }
}

    void Update()
    {
        Vector3 currentScale = transform.localScale;
        float newY = Mathf.Lerp(currentScale.y, targetScaleY, Time.deltaTime * smoothSpeed);
        transform.localScale = new Vector3(currentScale.x, newY, currentScale.z);
    }

    private void OnReceiveFloat(string receiver, float value)
    {
        if (receiver == receiverName)
        {
            float normalizedValue = Mathf.InverseLerp(minInput, maxInput, value);
            targetScaleY = Mathf.Lerp(minYScale, maxYScale, normalizedValue);
        }
    }
}