using UnityEngine;
using System;

public class MidiDevToPatch : MonoBehaviour
{
    [SerializeField] LibPdInstance patch;
    [SerializeField] MidiInput midiInput;
    Action<int, int, int> onNoteOnCallback;
    Action<int, int, int> onNoteOffCallback;
    Action<int, int, int> onControlChangeCallback;

    void Awake()
    {
        onNoteOnCallback = (channel, note, velocity) =>
            patch.SendMidiNoteOn(channel, note, velocity);
        onNoteOffCallback = (channel, note, velocity) =>
            patch.SendMidiNoteOn(channel, note, 0);
        onControlChangeCallback = (channel, control, value) =>
            patch.SendMidiCc(channel, control, value);
    }

    void OnEnable()
    {
        if (midiInput != null)
        {
            midiInput.onNoteOn        += onNoteOnCallback;
            midiInput.onNoteOff       += onNoteOffCallback;
            midiInput.onControlChange += onControlChangeCallback;
        }
    }

    void OnDisable()
    {
        midiInput.onNoteOn        -= onNoteOnCallback;
        midiInput.onNoteOff       -= onNoteOffCallback;
        midiInput.onControlChange -= onControlChangeCallback;
    }
}
