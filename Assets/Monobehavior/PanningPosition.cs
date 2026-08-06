using UnityEngine;

public class PanningPosition : MonoBehaviour
{
     // Referenz auf die Pure Data Schnittstelle (LibPD)
    [Header("Pure Data")]
    [SerializeField] private LibPdInstance pdInstance;

    [SerializeField] private string PanningReceiver = "panning";

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (pdInstance == null)
            pdInstance = GetComponentInChildren<LibPdInstance>();
        if (pdInstance == null)
            Debug.LogError($"{name}: LibPdInstance not assigned and not found on prefab!");
    }

    // Update is called once per frame
    void Update()
    {
        if (pdInstance != null)
        {

            float normalizedPosition = Mathf.Clamp((transform.position.x + 10f) / 20.0f, 0f, 1f);
            pdInstance.SendFloat(PanningReceiver, normalizedPosition);
        }
    }
}
