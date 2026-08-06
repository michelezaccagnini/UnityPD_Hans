using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class WalkingCollision : MonoBehaviour
{
    private BoxCollider2D bc1;
    private BoxCollider2D bc2;
    private BoxCollider2D bc3;
    
    private SpriteRenderer sr;
    private MeshRenderer mr; // Neu: Für 3D-Objekte wie deine Sphere

    [SerializeField] private int midiCCNumber = 71;
    [SerializeField] GameObject gm2; // Das ist deine "Sphere"
    [SerializeField] GameObject gm3; // Das ist dein Dreieck
    private bool was_hit = false;

    private LibPdInstance pdInstance;                 
    [SerializeField] private int midiChannel = 1;     
    [SerializeField] private int velocity = 127;      

    // random color Generator
    Vector3 hash31(float p)
    {
        Vector3 pp = new Vector3(0.1031f, 0.1030f, 0.0973f);
        Vector3 p3 = new Vector3(p, p, p);
        p3 = Vector3.Scale(p3, pp);
        p3 = new Vector3(p3.x % 1f, p3.y % 1f, p3.z % 1f);
        Vector3 p3a = new Vector3(p3.y, p3.z, p3.x) + new Vector3(33.33f, 33.33f, 33.33f);
        float d = Vector3.Dot(p3, p3a);
        p3 = p3 + new Vector3(d, d, d);
        p3 = Vector3.Scale(new Vector3(p3.x, p3.x, p3.y) + new Vector3(p3.y, p3.z, p3.z), new Vector3(p3.z, p3.y, p3.x));
        p3 = new Vector3(p3.x % 1f, p3.y % 1f, p3.z % 1f);
        return p3;
    }

    void Awake()
    {
        bc1 = GetComponent<BoxCollider2D>();
        
        if (gm2 != null) bc2 = gm2.GetComponent<BoxCollider2D>();
        if (gm3 != null) bc3 = gm3.GetComponent<BoxCollider2D>();
        
        // WICHTIG: Wir holen uns den Renderer von dem Objekt, das gefärbt werden soll (gm2 / Sphere)
        if (gm2 != null)
        {
            sr = gm2.GetComponent<SpriteRenderer>();
            mr = gm2.GetComponent<MeshRenderer>();
        }
    }

    void Start()
    {
        pdInstance = FindFirstObjectByType<LibPdInstance>();
        
        if (pdInstance == null)
        {
            Debug.LogError("WalkingCollision: LibPdInstance wurde nicht gefunden!");
        }
    }

    void Update()
    {
        // 1. Kollision mit der Sphere (gm2)
        if (bc1.bounds.Intersects(bc2.bounds) && !was_hit) 
        {
            Vector3 col = hash31(Time.time);
            Color targetColor = new Color(col.x, col.y, col.z, 1f);

            // Sichere Zuweisung: Erst prüfen, ob die Komponente existiert!
            if (sr != null) 
            {
                sr.color = targetColor;
            }
            else if (mr != null) 
            {
                // Für 3D-Objekte ändern wir die Farbe des Materials
                mr.material.color = targetColor;
            }

            was_hit = true;
        }
        else if (bc1.bounds.Intersects(bc2.bounds) && was_hit) 
        {
            was_hit = true;
        }
        else 
        {
            was_hit = false;
        }

        // 2. Kollision mit dem Dreieck (gm3) -> Löst Pure Data aus!
        if (bc1.bounds.Intersects(bc3.bounds))
        {
            // Optional: Wenn sich das Dreieck beim Treffen auch grün färben soll,
            // müsste man hier analog den Renderer von gm3 holen. Aktuell färbt es nichts,
            // damit es nicht abstürzt.

            // Sende den Befehl via MIDI CC exklusiv an Pure Data
            if (pdInstance != null)
            {
                pdInstance.SendMidiCc(midiChannel, midiCCNumber, velocity);
            }
        }
    }
}