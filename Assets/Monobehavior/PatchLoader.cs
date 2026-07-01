using UnityEngine;

public class PatchLoader : MonoBehaviour
{
    [SerializeField] private LibPdInstance patch;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        float time = Mathf.Sin(Time.time) *0.5f + 0.5f;
        patch.SendMidiCc(0, 1, (int)(time * 127));
    }
}
