# Android Branch: 48 kHz Audio Migration

Apply these two targeted changes to the Android branch. Do **not** pull
`LibPdInstance.cs` or `LibPdPluginProbe.cs` wholesale — only apply the
specific edits described below.

For full background on why this matters see `iOS-Audio-Notes.md`
(section: "App crash on iOS review device").

---

## Change 1 — `Assets/StaticScripts/LibPdPluginProbe.cs`

Add a new private static method `ForceAudioSampleRate` and its
`RuntimeInitializeOnLoadMethod` attribute **before** the existing
`ProbeAfterSceneLoad` method.

Also update the class-level doc comment while you're there.

### Find this block (the existing ProbeAfterSceneLoad):

```csharp
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ProbeAfterSceneLoad()
```

### Insert immediately before it:

```csharp
    /// Force FMOD to initialise at 48000 Hz before any scene or audio loads.
    /// Unity/FMOD defaults to 24000 Hz on iOS and Android, which causes a 2:1
    /// sample-rate mismatch with the hardware (locked to 48000 Hz on modern
    /// devices). That mismatch degrades audio quality (Nyquist 12 kHz instead
    /// of 24 kHz) and can cause buffer-size mismatches that crash
    /// libpd_process_float. See iOS-Audio-Notes.md for full details.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ForceAudioSampleRate()
    {
        AudioConfiguration cfg = AudioSettings.GetConfiguration();
        if (cfg.sampleRate != 48000)
        {
            cfg.sampleRate = 48000;
            AudioSettings.Reset(cfg);
        }
    }

```

No other changes to this file are needed.

---

## Change 2 — `Assets/Monobehavior/LibPdInstance.cs`

Replace the body of `OnAudioFilterRead` to compute ticks from the actual
buffer length instead of the cached `numTicks` field. This prevents a
native buffer overflow if the audio subsystem changes the buffer size at
runtime (e.g. after an audio session reconfiguration).

### Find this exact block:

```csharp
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!pdFail && !patchFail && loaded)
        {
            SetInstanceIfNeeded();
            libpd_process_float(numTicks, data, data);
        }
    }
```

### Replace with:

```csharp
    void OnAudioFilterRead(float[] data, int channels)
    {
        if (!pdFail && !patchFail && loaded)
        {
            SetInstanceIfNeeded();
            // Use the actual buffer length rather than the cached numTicks so that
            // if the hardware or session reconfiguration changes the buffer size at
            // runtime, libpd_process_float never reads/writes past the end of data.
            int safeTicks = (data.Length / channels) / 64;
            if (safeTicks > 0)
                libpd_process_float(safeTicks, data, data);
        }
    }
```

No other changes to this file are needed.

---

## Verification

After applying both changes, run an Android build and check logcat for:

```
[LibPdPluginProbe] native plugin loaded OK (blocksize=64)
```

If you have access to `AudioSettings.outputSampleRate` at runtime (e.g.
via a temporary `Debug.Log` in any `Start()` method), it should now
return `48000` instead of `24000`.

---

## What is NOT needed on Android

- `SynthaesthesiaAudioSession.mm` — iOS-only native plugin, not compiled on Android.
- Any changes to `MenuSceneController.cs` or `IntroSceneController.cs` —
  those fixes were unrelated UI null-safety guards, apply them separately
  only if the Android branch has the same bugs.
