using System;
using System.Collections.Generic;
using UnityEngine;
using RtMidi;

public class MidiInput : MonoBehaviour
{
    public event Action<int, int, int> onNoteOn;
    public event Action<int, int, int> onNoteOff; // Auf 3 Parameter angepasst (channel, note, velocity)
    public event Action<int, int, int> onControlChange;

    [Header("Options")]
    [SerializeField] private bool logMessages = true;

    private MidiIn _probe;
    private readonly List<(MidiIn dev, string name)> _ports = new();
    private readonly byte[] _buffer = new byte[32];

    void OnEnable()
    {
        InitializeMidi();
    }

    void OnDisable()
    {
        CloseAllPorts();
    }

    void OnDestroy()
    {
        CloseAllPorts();
    }

    private void InitializeMidi()
    {
        CloseAllPorts();

        try
        {
            _probe = MidiIn.Create();
            _probe.ErrorReceived = (t, m) => Debug.LogWarning($"[MIDI Probe] {t}: {m}");

            int portCount = _probe.PortCount;
            if (logMessages) Debug.Log($"[MIDI] Gefundene MIDI-Ports: {portCount}");

            for (int i = 0; i < portCount; i++)
            {
                string name = _probe.GetPortName(i);

                // Virtuelle / System-Ports ignorieren
                if (name.StartsWith("RtMidi") || name.StartsWith("Midi Through"))
                {
                    continue;
                }

                var dev = MidiIn.Create();
                string portName = name;
                dev.ErrorReceived = (t, m) => Debug.LogWarning($"[MIDI:{portName}] {t}: {m}");
                
                dev.OpenPort(i);
                _ports.Add((dev, name));

                if (logMessages) Debug.Log($"[MIDI] Erfolgreich geöffnet: Port {i} ({name})");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[MIDI] Fehler beim Initialisieren: {e.Message}");
        }
    }

    void Update()
    {
        // Alle geöffneten Ports in jedem Frame abfragen
        for (int i = 0; i < _ports.Count; i++)
        {
            Poll(_ports[i].dev, _ports[i].name);
        }
    }

    private void Poll(MidiIn dev, string name)
    {
        if (dev == null) return;

        // Solange Nachrichten im Puffer sind, diese verarbeiten
        while (true)
        {
            var msg = dev.GetMessage(_buffer, out _);
            if (msg.Length == 0) break;

            Dispatch(msg, name);
        }
    }

    private void Dispatch(ReadOnlySpan<byte> msg, string name)
    {
        if (msg.Length < 1) return;

        byte status = (byte)(msg[0] >> 4);
        int channel = (msg[0] & 0x0F) + 1; // Kanäle 1-16 (1-basiert für bessere Lesbarkeit)
        int d1 = msg.Length > 1 ? msg[1] : 0; // Note oder CC Num.
        int d2 = msg.Length > 2 ? msg[2] : 0; // Velocity oder CC Wert

        switch (status)
        {
            case 0x9: // Note On
                if (d2 > 0)
                {
                    if (logMessages) Debug.Log($"[MIDI:{name}] Ch:{channel} | NOTE ON: {d1} | Vel:{d2}");
                    onNoteOn?.Invoke(channel, d1, d2);
                }
                else // Note On mit Velocity 0 gilt im MIDI-Standard als Note Off!
                {
                    if (logMessages) Debug.Log($"[MIDI:{name}] Ch:{channel} | NOTE OFF (Vel 0): {d1}");
                    onNoteOff?.Invoke(channel, d1, 0);
                }
                break;

            case 0x8: // Note Off
                if (logMessages) Debug.Log($"[MIDI:{name}] Ch:{channel} | NOTE OFF: {d1} | Vel:{d2}");
                onNoteOff?.Invoke(channel, d1, d2);
                break;

            case 0xB: // Control Change (CC)
                if (logMessages) Debug.Log($"[MIDI:{name}] Ch:{channel} | CC: {d1} = {d2}");
                onControlChange?.Invoke(channel, d1, d2);
                break;
        }
    }

    private void CloseAllPorts()
    {
        foreach (var p in _ports)
        {
            p.dev?.Dispose();
        }
        _ports.Clear();

        if (_probe != null)
        {
            _probe.Dispose();
            _probe = null;
        }
    }

    // Falls du im Spiel die Ports manuell neu scannen willst:
    public void RescanPorts()
    {
        InitializeMidi();
    }
}