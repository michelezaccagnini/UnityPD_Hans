using UnityEngine;

public class SimpleLfoAnim : MonoBehaviour
{ 
    [SerializeField] string lfoName;

    void Update()
    {
        float value = GlobalLfos.Instance.Get(lfoName);
        transform.localPosition = new Vector3(0, value, 0);
    }
}