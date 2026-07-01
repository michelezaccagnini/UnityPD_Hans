using UnityEngine;

public class TestSendCC : MonoBehaviour
{
    [SerializeField] private LibPdInstance patch;

    [Range(0.1f, 5.0f)]
    [SerializeField] private float frequency = 1.0f;

    private int pitch = 64;
    private int vol = 80;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if(Input.GetKeyDown(KeyCode.Space))
        {
            pitch = Random.Range(50, 80);
            vol = Random.Range(80, 127);
        }
        patch.SendMidiNoteOn(0, pitch, vol);
    }
}
