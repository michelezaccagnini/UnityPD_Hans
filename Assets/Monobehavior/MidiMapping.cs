using UnityEngine;
using System;
using System.Collections.Generic;

public class MidiMapping : MonoBehaviour
{
    [Header("Referenzen")]
    [SerializeField] private PlayerMove_B player;
    [SerializeField] private MidiInput midiInput;

    [Header("Pure Data Settings")]
    [SerializeField] private string collisionPdReceiver = "sahOn";

    // --- STRUCTS FÜR DEN INSPECTOR ---

    [System.Serializable]
    public struct PadMapping
    {
        public string name;
        [Tooltip("Kanal von 0 bis 15 (0 = Channel 1, 9 = Channel 10)")]
        public int midiChannel; 
        [Tooltip("Die genaue Notennummer des Pads (siehe Console Log)")]
        public int midiNote;
        public float jumpForce;
        public string pdReceiverName; // Optional: Sendet Bang an PD
    }

    [System.Serializable]
    public struct KnobMapping
    {
        public string name;
        [Tooltip("Kanal von 0 bis 15 (0 = Channel 1)")]
        public int midiChannel;
        [Tooltip("Control Change Nummer (CC)")]
        public int ccNumber;
        public KnobAction action;
        public string pdReceiverName;

        public enum KnobAction
        {
            None,
            ControlGlobalSpeed,
            SendToPureDataOnly
        }
    }

    // --- LISTE FÜR INSPECTOR ---

    [Header("Pad / Dynamic Jump Mappings")]
    [SerializeField] private List<PadMapping> padMappings = new List<PadMapping>()
    {
        new PadMapping { name = "Pad 1 (Jump)", midiChannel = 0, midiNote = 40, jumpForce = 10f, pdReceiverName = "sahOn" },
        new PadMapping { name = "Pad 2 (High Jump)", midiChannel = 0, midiNote = 41, jumpForce = 15f, pdReceiverName = "sahOn" }
    };

    [Header("Knob / Drehregler Mappings")]
    [SerializeField] private List<KnobMapping> knobMappings = new List<KnobMapping>()
    {
        new KnobMapping { name = "Knob 1 (Speed)", midiChannel = 0, ccNumber = 60, action = KnobMapping.KnobAction.ControlGlobalSpeed, pdReceiverName = "" }
    };

    [Header("Keyboard / Speed Mapping (Tasten)")]
    [Tooltip("0 = Channel 1 (Standard für Keyboard-Tasten)")]
    [SerializeField] private int speedMidiChannel = 0; 
    [SerializeField] private float baseNoteOffset = 48f; // C3 als Referenz
    [SerializeField] private bool enableKeyboardSpeedControl = true;

    [Header("Debugging")]
    [Tooltip("Gibt alle eingehenden MIDI-Signale in der Konsole aus.")]
    [SerializeField] private bool showDebugLogs = true;

    // Pure Data Instanz
    private LibPdInstance pdInstance;

    // Callbacks
    private Action<int, int, int> onNoteOnCallback;
    private Action<int, int, int> onControlChangeCallback;

    private void Awake()
    {
        onNoteOnCallback = OnNoteOnCallback;
        onControlChangeCallback = OnControlChangeCallback;
    }

    private void OnEnable()
    {
        if (midiInput != null)
        {
            midiInput.onNoteOn += onNoteOnCallback;
            midiInput.onControlChange += onControlChangeCallback;
        }

        if (player != null)
        {
            player.OnSphereCollision += HandlePlayerCollision;
        }
    }

    private void OnDisable()
    {
        if (midiInput != null)
        {
            midiInput.onNoteOn -= onNoteOnCallback;
            midiInput.onControlChange -= onControlChangeCallback;
        }

        if (player != null)
        {
            player.OnSphereCollision -= HandlePlayerCollision;
        }
    }

    private void Start()
    {
        pdInstance = FindFirstObjectByType<LibPdInstance>();
        if (pdInstance == null)
        {
            Debug.LogWarning("[MidiMapping] LibPdInstance wurde in der Szene nicht gefunden.");
        }
    }

    // --- MIDI NOTE ON HANDLER ---

    private void OnNoteOnCallback(int channel, int note, int velocity)
    {
        if (velocity <= 0) return;

        if (showDebugLogs)
        {
            Debug.Log($"[MIDI NOTE] Channel: {channel} | Note: {note} | Velocity: {velocity}");
        }

        // SCHRITT 1: Prüfen, ob die gedrückte Note zu EINEM DER PADS gehört
        PadMapping matchedPad = default;
        bool isPadNote = false;

        foreach (var pad in padMappings)
        {
            if (pad.midiChannel == channel && pad.midiNote == note)
            {
                isPadNote = true;
                matchedPad = pad;
                break;
            }
        }

        // FALL A: Es ist ein PAD -> Nur Sprung ausführen, NIEMALS Speed verändern!
        if (isPadNote)
        {
            TriggerPadAction(matchedPad);
            return; // Beendet die Methode hier sofort!
        }

        // FALL B: Es ist KEIN Pad, sondern eine Tastatur-Taste -> Speed anpassen
        if (enableKeyboardSpeedControl && channel == speedMidiChannel)
        {
            float noteValue = (float)note - baseNoteOffset;
            float newSpeed = Mathf.Clamp(0.1f + (noteValue * 0.1f), 0.1f, 3f);
            
            if (player != null)
            {
                player.SetGlobalSpeed(newSpeed);
            }
        }
    }

    // --- MIDI CONTROL CHANGE HANDLER ---

    private void OnControlChangeCallback(int channel, int control, int value)
    {
        if (showDebugLogs)
        {
            Debug.Log($"[MIDI CC / KNOB] Channel: {channel} | CC: {control} | Value: {value}");
        }

        foreach (var knob in knobMappings)
        {
            if (knob.midiChannel == channel && knob.ccNumber == control)
            {
                if (knob.action == KnobMapping.KnobAction.ControlGlobalSpeed && player != null)
                {
                    float speed = 0.1f + ((float)value / 127f) * 2.9f;
                    player.SetGlobalSpeed(speed);
                }

                if (pdInstance != null && !string.IsNullOrEmpty(knob.pdReceiverName))
                {
                    pdInstance.SendFloat(knob.pdReceiverName, (float)value);
                }
            }
        }
    }

    // --- HILFSFUNKTIONEN ---

    private void TriggerPadAction(PadMapping pad)
    {
        if (player != null)
        {
            player.PerformJump(pad.jumpForce);
        }

        if (pdInstance != null && !string.IsNullOrEmpty(pad.pdReceiverName))
        {
            pdInstance.SendBang(pad.pdReceiverName);
        }
    }

    private void HandlePlayerCollision()
    {
        if (pdInstance != null && !string.IsNullOrEmpty(collisionPdReceiver))
        {
            pdInstance.SendBang(collisionPdReceiver);
            Debug.Log($"[MidiMapping] Kollisions-Bang an PD-Receiver '{collisionPdReceiver}' gesendet.");
        }
    }
}
