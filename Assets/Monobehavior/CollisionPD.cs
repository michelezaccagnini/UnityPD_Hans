using UnityEngine;
using System;

public class CollisionPD : MonoBehaviour
{
    public event Action OnSphereCollision;

    [Header("Pure Data")]
    [SerializeField] private LibPdInstance pdInstance;
    [SerializeField] private string ReceiverPdGo = "sahOn";

    [Header("Global Control")]
    [Range(0.1f, 3f)]
    public float globalSpeedMultiplier = 1f;

    [Tooltip("Geschwindigkeit, mit der sich das Tempo an den Zielwert annähert")]
    [SerializeField] private float speedChangeSmoothness = 3f;

    [Header("Movement Settings")]
    [SerializeField] private float baseMoveSpeed = 5f;
    [SerializeField] private float leftBoundary = -5f;
    [SerializeField] private float rightBoundary = 5f;

    [Header("Animation Settings")]
    [SerializeField] private float baseAnimationSpeed = 1f;

    [Header("MIDI Input Source")] 
    [SerializeField] private MidiInput midiInput;
    [SerializeField] private string ReceiverFromKey = "sahOnKey";

    [Header("Pad Settings (Jump / Bang)")]
    [Tooltip("MIDI-Kanal speziell für die Pads")]
    [SerializeField] private int padMidiChannel = 1;
    [Tooltip("Welche Note löst den Sprung/Bang aus?")]
    [SerializeField] private int targetPadNote = 36; 

    [Header("Keyboard Range (Tempo per Tasten)")]
    [Tooltip("MIDI-Kanal speziell für die Keyboard-Tasten")]
    [SerializeField] private int keyMidiChannel = 1;
    [Tooltip("Tiefste Taste = Langsamstes Tempo")]
    [SerializeField] private int targetLowKeyNote = 48; 
    [Tooltip("Höchste Taste = Schnellstes Tempo")]
    [SerializeField] private int targetHighKeyNote = 72;

    [Header("Knob Settings (Tempo per Drehregler)")]
    [Tooltip("MIDI-Kanal speziell für die Knobs")]
    [SerializeField] private int knobMidiChannel = 1;
    [Tooltip("Welches CC-Steuerelement soll das Tempo steuern?")]
    [SerializeField] private int targetCcControl = 1;

    [Header("Speed Limits")]
    [Tooltip("Mindestgeschwindigkeit (Wert 0 beim Knob / Tiefste Note)")]
    [SerializeField] private float minSpeed = 0.2f;
    [Tooltip("Maximalgeschwindigkeit (Wert 127 beim Knob / Höchste Note)")]
    [SerializeField] private float maxSpeed = 3.0f;

    [Header("Live MIDI Display (Nur zum Ablesen)")]
    [SerializeField] private int lastReceivedChannel;
    [SerializeField] private int lastReceivedNote;
    [SerializeField] private int lastReceivedVelocity;
    [SerializeField] private int lastReceivedCC;
    [SerializeField] private int lastReceivedCCValue;

    [Header("Jump")] 
    [SerializeField] private float jumpForce = 10f;       
    [SerializeField] private bool jumpWithSpace = true;    

    // Komponenten & Internes
    private Rigidbody2D rb;               
    private SpriteRenderer spriteRenderer;
    private Animator animator;

    private float restY;                  
    private bool jumpRequested;           
    private bool pdPadBangRequested;
    private int direction = 1;

    // Für fließenden Geschwindigkeitsübergang
    private float targetSpeedMultiplier = 1f;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.freezeRotation = true;

        spriteRenderer = GetComponent<SpriteRenderer>();
        animator = GetComponent<Animator>();
    }

    void OnEnable()
    {
        if (midiInput != null)
        {
            midiInput.onControlChange += OnControlChangeCallback;
            midiInput.onNoteOn += OnNoteOnCallback;
            Debug.Log($"[CollisionPD] MIDI-Events erfolgreich auf {gameObject.name} registriert.");
        }
        else
        {
            Debug.LogWarning($"[CollisionPD] {name}: MidiInput ist im Inspector NICHT zugewiesen!");
        }
    }

    void OnDisable()
    {
        if (midiInput != null)
        {
            midiInput.onControlChange -= OnControlChangeCallback;
            midiInput.onNoteOn -= OnNoteOnCallback;
        }
    }

    void Start()
    {
        restY = transform.position.y;
        targetSpeedMultiplier = globalSpeedMultiplier;

        if (pdInstance == null)
            pdInstance = GetComponentInChildren<LibPdInstance>();

        if (pdInstance == null)
            Debug.LogError($"[CollisionPD] {name}: LibPdInstance fehlt!");
    }

    void Update()
    {
        // Sanfter Übergang zur neuen Zielgeschwindigkeit
        globalSpeedMultiplier = Mathf.MoveTowards(
            globalSpeedMultiplier, 
            targetSpeedMultiplier, 
            Time.deltaTime * speedChangeSmoothness
        );

        if (animator != null)
        {
            animator.speed = baseAnimationSpeed * globalSpeedMultiplier;
        }

        if (jumpWithSpace && (Input.GetKeyDown(KeyCode.Space) || Input.GetButtonDown("Jump")))
        {
            jumpRequested = true;
        }

        if (pdPadBangRequested)
        {
            pdPadBangRequested = false;
            if (pdInstance != null)
            {
                pdInstance.SendBang(ReceiverFromKey);
                Debug.Log("[Unity -> PD] Bang für Pad gedrückt gesendet.");
            }
        }

        if (transform.position.x >= rightBoundary && direction == 1)
        {
            FlipDirection(-1);
        }
        else if (transform.position.x <= leftBoundary && direction == -1)
        {
            FlipDirection(1);
        }
    }

    void FixedUpdate()
    {
        if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) return;

        float currentSpeed = baseMoveSpeed * globalSpeedMultiplier;
        Vector2 movement = new Vector2(direction * currentSpeed * Time.fixedDeltaTime, 0f);
        rb.position += movement;

        if (jumpRequested)
        {
            jumpRequested = false;
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
        }

        if (rb.linearVelocity.y <= 0f && rb.position.y < restY)
        {
            rb.position = new Vector2(rb.position.x, restY);
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        }
    }

    public void SetGlobalSpeed(float multiplier)
    {
        targetSpeedMultiplier = Mathf.Clamp(multiplier, minSpeed, maxSpeed);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Sphere"))
        {
            FlipDirection(direction * -1);
            OnSphereCollision?.Invoke();

            if (pdInstance != null)
            {
                pdInstance.SendBang(ReceiverPdGo); 
                Debug.Log("[Unity -> PD] Impuls für sahOn gesendet.");
            }
        }
    }

    private void FlipDirection(int newDirection)
    {
        direction = newDirection;
        if (spriteRenderer != null)
        {
            spriteRenderer.flipX = (direction < 0);
        }
    }

    private void OnControlChangeCallback(int channel, int control, int value)
    {
        // 1. Für Live-Anzeige im Inspector speichern
        lastReceivedChannel = channel;
        lastReceivedCC = control;
        lastReceivedCCValue = value;

        Debug.Log($"[MIDI CC EMPFANGEN] Kanal: {channel} | CC: {control} | Wert: {value}");

        // 2. Abfragen auf den Knobs-Kanal beschränken
        if (channel == knobMidiChannel && control == targetCcControl)
        {
            float t = value / 127f;
            float calculatedSpeed = Mathf.Lerp(minSpeed, maxSpeed, t);

            SetGlobalSpeed(calculatedSpeed);
            Debug.Log($"[AKTION] Knob CC {control} (Kanal {channel}, Wert: {value}) -> Tempo auf {calculatedSpeed:F2} gesetzt.");
        }
    }

    private void OnNoteOnCallback(int channel, int note, int velocity)
    {
        // 1. Für Live-Anzeige im Inspector speichern
        lastReceivedChannel = channel;
        lastReceivedNote = note;
        lastReceivedVelocity = velocity;

        Debug.Log($"[MIDI NOTE EMPFANGEN] Kanal: {channel} | Note: {note} | Velocity: {velocity}");

        // 2. Pad-Verarbeitung (auf padMidiChannel)
        if (channel == padMidiChannel && note == targetPadNote)
        {
            jumpRequested = true;
            pdPadBangRequested = true;
            Debug.Log($"[AKTION] Pad-Note ({note}) auf Kanal {channel} erkannt -> Sprung!");
        }
        // 3. Keys-Verarbeitung (auf keyMidiChannel)
        else if (channel == keyMidiChannel && note >= targetLowKeyNote && note <= targetHighKeyNote)
        {
            float t = Mathf.InverseLerp(targetLowKeyNote, targetHighKeyNote, note);
            float calculatedSpeed = Mathf.Lerp(minSpeed, maxSpeed, t);

            SetGlobalSpeed(calculatedSpeed);
            Debug.Log($"[AKTION] Note {note} (Kanal {channel}) gespielt -> Tempo auf {calculatedSpeed:F2} gesetzt.");
        }
    }
}