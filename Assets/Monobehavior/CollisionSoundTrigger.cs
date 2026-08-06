using System;
using UnityEngine;

public class CollisionSoundTrigger : MonoBehaviour
{
    private LibPdInstance pdInstance;

    [Header("Midi Input Script Object")]
    [SerializeField] private MidiInput midiInput;
    private Rigidbody2D rb;
    private AudioSource audioSource;
    private float restY;
    private bool jumpRequested;
    private Action<int, int, int> onControlChangeCallback;

    [SerializeField] private int midiChannel = 1;
    [SerializeField] private int midiCCNumber = 10;
    [SerializeField] private int velocity = 127;

    [Header("Collision Sound File")]
    [SerializeField] private AudioClip collisionClip;
    [SerializeField] [Range(0f, 1f)] private float collisionVolume = 1f;

    [Header("Jump")]
    [SerializeField] private float jumpForce = 10f;
    [SerializeField] private bool jumpWithSpace = true;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null && jumpWithSpace)
        {
            rb = gameObject.AddComponent<Rigidbody2D>();
            rb.freezeRotation = true;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        // 2D-Kollider sicherstellen (altes Prefab hatte nur 3D SphereCollider)
        if (GetComponent<Collider2D>() == null)
        {
            var circle = gameObject.AddComponent<CircleCollider2D>();
            circle.radius = 0.5f;
        }

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null && collisionClip != null)
            audioSource = gameObject.AddComponent<AudioSource>();
        if (audioSource != null)
            audioSource.playOnAwake = false;

        onControlChangeCallback = OnControlChange;
    }

    void OnEnable()
    {
        if (midiInput != null)
            midiInput.onControlChange += onControlChangeCallback;
    }

    void OnDisable()
    {
        if (midiInput != null)
            midiInput.onControlChange -= onControlChangeCallback;
    }

    void Start()
    {
        restY = transform.position.y;

        GameObject audioManager = GameObject.Find("PD_Audio_Manager");
        if (audioManager == null)
            audioManager = GameObject.Find("Patch");

        if (audioManager != null)
            pdInstance = audioManager.GetComponent<LibPdInstance>();

        if (pdInstance == null)
            pdInstance = FindFirstObjectByType<LibPdInstance>();
    }

    void Update()
    {
        if (!jumpWithSpace)
            return;

        if (Input.GetKeyDown(KeyCode.Space) || Input.GetButtonDown("Jump"))
            jumpRequested = true;
    }

    void OnControlChange(int channel, int control, int value)
    {
        // MidiInput channels are 0–15; pad hit usually sends value > 0
        if (channel == midiChannel && control == midiCCNumber && value > 0)
        {
            jumpRequested = true;
        }
    }

    void FixedUpdate()
    {
        if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic)
            return;

        if (jumpRequested)
        {
            jumpRequested = false;
            rb.WakeUp();
            // Geändert auf klassisches 'velocity' für maximale Kompatibilität
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            Debug.Log($"{name}: Jump!");
        }

        // Nur beim Fallen clampen — nicht während des Sprungs
        if (rb.linearVelocity.y <= 0f && rb.position.y < restY)
        {
            rb.position = new Vector2(rb.position.x, restY);
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag("Player"))
            return;

        if (collisionClip != null)
        {
            audioSource.PlayOneShot(collisionClip, collisionVolume);
        }

        if (pdInstance != null)
            pdInstance.SendMidiCc(midiChannel, midiCCNumber, velocity);
    }
}