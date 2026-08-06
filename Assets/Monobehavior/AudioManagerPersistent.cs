using UnityEngine;

public class AudioManagerPersistent : MonoBehaviour
{
    private static AudioManagerPersistent instance;

    void Awake()
    {
        // Sicherstellen, dass es wirklich nur EINE Instanz im ganzen Spiel gibt (Singleton-Muster)
        if (instance == null)
        {
            instance = this;
            // Verhindert, dass dieses Objekt beim Szenenwechsel gelöscht wird
            DontDestroyOnLoad(gameObject); 
        }
        else
        {
            // Falls wir in eine Szene zurückkehren, die das Objekt nochmal erstellen will: Zerstören!
            Destroy(gameObject);
        }
    }
}