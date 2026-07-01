// LibPdInstance.cs - Unity integration of libpd, supporting multiple instances.
// -----------------------------------------------------------------------------
// Copyright (c) 2019 Niall Moody modified by Michele Zaccagnini
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.
// -----------------------------------------------------------------------------


using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine.Events;
using System.Collections.Generic;
using System.Collections;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using AOT;
using System.IO;
using UnityEngine.Networking;

#region UnityEvent types
[System.Serializable]
public class StringEvent : UnityEvent<string> {}

[System.Serializable]
public class StringFloatEvent : UnityEvent<string, float> {}

[System.Serializable]
public class StringStringEvent : UnityEvent<string, string> {}

[System.Serializable]
public class StringObjArrEvent : UnityEvent<string, object[]> {}

[System.Serializable]
public class StringStringObjArrEvent : UnityEvent<string, string, object[]> {}

[System.Serializable]
public class IntIntIntEvent : UnityEvent<int, int, int> {}

[System.Serializable]
public class IntIntEvent : UnityEvent<int, int> {}
#endregion

/// <summary>
/// Unity Component for running a Pure Data patch. Uses libpd's multiple
/// instance support, so you can run multiple LibPdInstances in your scene.
///
/// Pd patches should be stored in Assets/StreamingAssets/PdAssets and assigned
/// to LibPdInstance in the inspector (type the patch name minus .pd into the
/// Patch text box).
/// </summary>
[RequireComponent(typeof(AudioSource))]
public class LibPdInstance : MonoBehaviour
{
    #region member variables

    [HideInInspector]
    public string patchName;
    [HideInInspector]
    public string patchDir;

    private IPatchBuilder patchBuilder;

#if UNITY_EDITOR
    // Drag-and-drop workaround: Unity doesn't allow StreamingAssets in the
    // inspector directly, so we use DefaultAsset and extract the path in OnValidate.
    public UnityEditor.DefaultAsset patch;
#endif

    public bool pipePrintToConsole = false;
    private static bool pipePrintToConsoleStatic = false;

    IntPtr patchPointer;
    private IntPtr instance;
    private int numTicks;

    [Tooltip("Android only: extra files in the same StreamingAssets folder as the patch (e.g. abs_braidswaves1.wav for soundfiler). Filenames only; copied next to the extracted .pd before libpd opens the patch.")]
    [SerializeField]
    private string[] androidExtraStreamingAssets;

    private Dictionary<string, IntPtr> bindings;

    // activeInstances is only accessed from the main thread (OnEnable, OnDisable,
    // output delegates called from Update). Never access it from the audio thread.
    private static List<LibPdInstance> activeInstances = new List<LibPdInstance>();

    private bool pdFail = false;
    private bool patchFail = false;
    public bool loaded = false;

    private static int numInstances = 0;

    // libpd's main instance must be initialised once (libpd_init) before any
    // per-component instance is created. pdinstance_new() reads main-instance
    // template state, so creating an instance first segfaults.
    private static bool libpdInitialised = false;

    // Set to the instance currently draining its queued message buffer so the
    // static libpd output callbacks dispatch only to that instance. Pd 0.54+
    // stores the queued ring buffers/hooks per instance, so each instance drains
    // and dispatches its own messages.
    private static LibPdInstance currentlyDraining;

    public bool IsReadyForCc { get; set; }
    public bool IsLoaded => loaded;
    public bool IsPatchFailed => patchFail;
    public bool IsPdFailed => pdFail;

    #region libpd imports

#if UNITY_IOS
    private const string DLL_NAME = "__Internal";
#else
    private const string DLL_NAME = "libpd";
#endif

    [DllImport(DLL_NAME)]
    private static extern int libpd_init();

    [DllImport(DLL_NAME)]
    private static extern int libpd_queued_init();

    [DllImport(DLL_NAME)]
    private static extern void libpd_queued_release();

    [DllImport(DLL_NAME)]
    private static extern void libpd_queued_receive_pd_messages();

    [DllImport(DLL_NAME)]
    private static extern void libpd_queued_receive_midi_messages();

    [DllImport(DLL_NAME)]
    private static extern void libpd_clear_search_path();

    [DllImport(DLL_NAME)]
    private static extern void libpd_add_to_search_path([In] [MarshalAs(UnmanagedType.LPStr)] string s);

    [DllImport(DLL_NAME)]
    private static extern IntPtr libpd_new_instance();

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_instance(IntPtr instance);

    [DllImport(DLL_NAME)]
    private static extern void libpd_free_instance(IntPtr instance);

    [DllImport(DLL_NAME)]
    private static extern int libpd_init_audio(int inChans, int outChans, int sampleRate);

    [DllImport(DLL_NAME)]
    private static extern IntPtr libpd_openfile([In] [MarshalAs(UnmanagedType.LPStr)] string basename,
                                                [In] [MarshalAs(UnmanagedType.LPStr)] string dirname);

    [DllImport(DLL_NAME)]
    private static extern void libpd_closefile(IntPtr p);

    [DllImport(DLL_NAME)]
    private static extern int libpd_getdollarzero(IntPtr p);

    [DllImport(DLL_NAME)]
    private static extern int libpd_process_float(int ticks, [In] float[] inBuffer, [Out] float[] outBuffer);

    [DllImport(DLL_NAME)]
    private static extern int libpd_blocksize();

    [DllImport(DLL_NAME)]
    private static extern int libpd_start_message(int max_length);

    [DllImport(DLL_NAME)]
    private static extern void libpd_add_float(float x);

    [DllImport(DLL_NAME)]
    private static extern void libpd_add_symbol([In] [MarshalAs(UnmanagedType.LPStr)] string sym);

    [DllImport(DLL_NAME)]
    private static extern int libpd_finish_list([In] [MarshalAs(UnmanagedType.LPStr)] string recv);

    [DllImport(DLL_NAME)]
    private static extern int libpd_finish_message([In] [MarshalAs(UnmanagedType.LPStr)] string recv,
                                                   [In] [MarshalAs(UnmanagedType.LPStr)] string msg);

    [DllImport(DLL_NAME)]
    private static extern int libpd_bang([In] [MarshalAs(UnmanagedType.LPStr)] string recv);

    [DllImport(DLL_NAME)]
    private static extern int libpd_float([In] [MarshalAs(UnmanagedType.LPStr)] string recv, float x);

    [DllImport(DLL_NAME)]
    private static extern int libpd_symbol([In] [MarshalAs(UnmanagedType.LPStr)] string recv,
                                           [In] [MarshalAs(UnmanagedType.LPStr)] string sym);

    [DllImport(DLL_NAME)]
    private static extern int libpd_exists([In] [MarshalAs(UnmanagedType.LPStr)] string obj);

    [DllImport(DLL_NAME)]
    private static extern IntPtr libpd_bind([In] [MarshalAs(UnmanagedType.LPStr)] string symbol);

    [DllImport(DLL_NAME)]
    private static extern void libpd_unbind(IntPtr binding);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_verbose(int verbose);

    [DllImport(DLL_NAME)]
    private static extern int libpd_is_float(IntPtr atom);

    [DllImport(DLL_NAME)]
    private static extern int libpd_is_symbol(IntPtr atom);

    [DllImport(DLL_NAME)]
    private static extern float libpd_get_float(IntPtr atom);

    [DllImport(DLL_NAME)]
    private static extern IntPtr libpd_get_symbol(IntPtr atom);

    [DllImport(DLL_NAME)]
    private static extern IntPtr libpd_next_atom(IntPtr atom);

    [DllImport(DLL_NAME)]
    private static extern int libpd_noteon(int channel, int pitch, int velocity);

    [DllImport(DLL_NAME)]
    private static extern int libpd_controlchange(int channel, int controller, int value);

    [DllImport(DLL_NAME)]
    private static extern int libpd_programchange(int channel, int program);

    [DllImport(DLL_NAME)]
    private static extern int libpd_pitchbend(int channel, int value);

    [DllImport(DLL_NAME)]
    private static extern int libpd_aftertouch(int channel, int value);

    [DllImport(DLL_NAME)]
    private static extern int libpd_polyaftertouch(int channel, int pitch, int value);

    [DllImport(DLL_NAME)]
    private static extern int libpd_midibyte(int port, int value);

    [DllImport(DLL_NAME)]
    private static extern int libpd_sysex(int port, int value);

    [DllImport(DLL_NAME)]
    private static extern int libpd_sysrealtime(int port, int value);

    [DllImport(DLL_NAME)]
    private static extern int libpd_arraysize([In] [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(DLL_NAME)]
    private static extern int libpd_read_array([Out] float[] dest,
                                               [In] [MarshalAs(UnmanagedType.LPStr)] string src,
                                               int offset,
                                               int n);

    [DllImport(DLL_NAME)]
    private static extern int libpd_write_array([In] [MarshalAs(UnmanagedType.LPStr)] string dest,
                                                int offset,
                                                [In] float[] src,
                                                int n);

    #endregion

    #region libpd delegate/callback declarations

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdPrintHook([In] [MarshalAs(UnmanagedType.LPStr)] string message);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_printhook(LibPdPrintHook hook);

    private static LibPdPrintHook printHook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdBangHook([In] [MarshalAs(UnmanagedType.LPStr)] string symbol);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_banghook(LibPdBangHook hook);

    private LibPdBangHook bangHook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdFloatHook([In] [MarshalAs(UnmanagedType.LPStr)] string symbol, float val);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_floathook(LibPdFloatHook hook);

    private LibPdFloatHook floatHook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdSymbolHook([In] [MarshalAs(UnmanagedType.LPStr)] string symbol,
                                         [In] [MarshalAs(UnmanagedType.LPStr)] string val);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_symbolhook(LibPdSymbolHook hook);

    private LibPdSymbolHook symbolHook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdListHook([In] [MarshalAs(UnmanagedType.LPStr)] string source,
                                       int argc,
                                       IntPtr argv);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_listhook(LibPdListHook hook);

    private LibPdListHook listHook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdMessageHook([In] [MarshalAs(UnmanagedType.LPStr)] string source,
                                          [In] [MarshalAs(UnmanagedType.LPStr)] string symbol,
                                          int argc,
                                          IntPtr argv);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_messagehook(LibPdMessageHook hook);

    private LibPdMessageHook messageHook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdMidiNoteOnHook(int channel, int pitch, int velocity);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_noteonhook(LibPdMidiNoteOnHook hook);

    private LibPdMidiNoteOnHook noteOnHook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdMidiControlChangeHook(int channel, int controller, int value);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_controlchangehook(LibPdMidiControlChangeHook hook);

    private LibPdMidiControlChangeHook controlChangeHook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdMidiProgramChangeHook(int channel, int program);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_programchangehook(LibPdMidiProgramChangeHook hook);

    private LibPdMidiProgramChangeHook programChangeHook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdMidiPitchBendHook(int channel, int value);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_pitchbendhook(LibPdMidiPitchBendHook hook);

    private LibPdMidiPitchBendHook pitchBendHook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdMidiAftertouchHook(int channel, int value);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_aftertouchhook(LibPdMidiAftertouchHook hook);

    private LibPdMidiAftertouchHook aftertouchHook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdMidiPolyAftertouchHook(int channel, int pitch, int value);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_polyaftertouchhook(LibPdMidiPolyAftertouchHook hook);

    private LibPdMidiPolyAftertouchHook polyAftertouchHook;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LibPdMidiByteHook(int channel, int value);

    [DllImport(DLL_NAME)]
    private static extern void libpd_set_queued_midibytehook(LibPdMidiByteHook hook);

    private LibPdMidiByteHook midiByteHook;

    #endregion

    #endregion

    #region events

    [System.Serializable]
    public struct PureDataEvents
    {
        public StringEvent Bang;
        public StringFloatEvent Float;
        public StringStringEvent Symbol;
        public StringObjArrEvent List;
        public StringStringObjArrEvent Message;
    }

    [Header("libpd → Unity Events")]
    public PureDataEvents pureDataEvents;

    [System.Serializable]
    public struct MidiEvents
    {
        public IntIntIntEvent MidiNoteOn;
        public IntIntIntEvent MidiControlChange;
        public IntIntEvent MidiProgramChange;
        public IntIntEvent MidiPitchBend;
        public IntIntEvent MidiAftertouch;
        public IntIntIntEvent MidiPolyAftertouch;
        public IntIntEvent MidiByte;
    }

    public MidiEvents midiEvents;

    #endregion

    #region MonoBehaviour methods

    private void Awake()
    {
        enabled = false;
        patchBuilder = GetComponent<IPatchBuilder>();
        if (patchBuilder != null)
        {
            patchBuilder.OnPatchBuilt += HandlePatchSetup;
            // Safety fallback: if OnPatchBuilt never fires (e.g. on device), init anyway.
            StartCoroutine(FallbackInitIfNoPatchBuilt());
        }
        else
        {
            UnityEngine.Debug.LogWarning($"{gameObject.name}: No IPatchBuilder found. Initialising libpd immediately.");
            HandlePatchSetup();
        }
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    // On Android, StreamingAssets lives inside the APK and cannot be opened as a
    // filesystem path. We download the patch via UnityWebRequest and write it to
    // persistentDataPath before opening it with libpd.
    private IEnumerator AndroidCopyAndOpenPatch(string streamingDir, string patchBaseName)
    {
        string persistentDir = Path.Combine(Application.persistentDataPath, patchDir.TrimStart('/'));
        if (!Directory.Exists(persistentDir))
            Directory.CreateDirectory(persistentDir);

        string persistentPd = Path.Combine(persistentDir, patchBaseName + ".pd");

        using (UnityWebRequest req = UnityWebRequest.Get(streamingDir + patchBaseName + ".pd"))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                UnityEngine.Debug.LogError($"{gameObject.name}: Failed to download patch: {req.error}");
                patchFail = true;
                yield break;
            }

            try
            {
                File.WriteAllBytes(persistentPd, req.downloadHandler.data);
                if (!File.Exists(persistentPd))
                {
                    UnityEngine.Debug.LogError($"{gameObject.name}: Patch file was not created at {persistentPd}");
                    patchFail = true;
                    yield break;
                }
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"{gameObject.name}: Exception writing patch file: {ex.Message}");
                patchFail = true;
                yield break;
            }
        }

        if (androidExtraStreamingAssets != null)
        {
            foreach (string raw in androidExtraStreamingAssets)
            {
                if (string.IsNullOrWhiteSpace(raw))
                    continue;

                string rel = raw.Trim().Replace('\\', '/');
                if (rel.IndexOf("..", StringComparison.Ordinal) >= 0)
                {
                    UnityEngine.Debug.LogError($"{gameObject.name}: Invalid android extra StreamingAsset path (no '..'): {raw}");
                    patchFail = true;
                    yield break;
                }

                string fileName = Path.GetFileName(rel);
                if (string.IsNullOrEmpty(fileName))
                    continue;

                string destFile = Path.Combine(persistentDir, fileName);
                using (UnityWebRequest req = UnityWebRequest.Get(streamingDir + fileName))
                {
                    yield return req.SendWebRequest();

                    if (req.result != UnityWebRequest.Result.Success)
                    {
                        UnityEngine.Debug.LogError($"{gameObject.name}: Failed to download StreamingAsset '{fileName}': {req.error}");
                        patchFail = true;
                        yield break;
                    }

                    try
                    {
                        File.WriteAllBytes(destFile, req.downloadHandler.data);
                        if (!File.Exists(destFile))
                        {
                            UnityEngine.Debug.LogError($"{gameObject.name}: Extra StreamingAsset was not written: {destFile}");
                            patchFail = true;
                            yield break;
                        }
                    }
                    catch (System.Exception ex)
                    {
                        UnityEngine.Debug.LogError($"{gameObject.name}: Exception writing '{fileName}': {ex.Message}");
                        patchFail = true;
                        yield break;
                    }
                }
            }
        }

        string openDir = persistentDir.Replace('\\', '/');
        if (!openDir.EndsWith("/"))
            openDir += "/";

        // This coroutine yields across frames, during which another instance may
        // have become the current pd instance on this thread, so re-assert ours
        // before opening the patch.
        SetInstanceIfNeeded();
        libpd_add_to_search_path(openDir);
        patchPointer = libpd_openfile(patchBaseName + ".pd", openDir);

        if (patchPointer == IntPtr.Zero)
        {
            UnityEngine.Debug.LogError($"{gameObject.name}: Could not open patch at {persistentPd}");
            patchFail = true;
            yield break;
        }

        libpd_start_message(1);
        libpd_add_float(1.0f);
        libpd_finish_message("pd", "dsp");

        if (!patchFail && !pdFail)
        {
            loaded = true;
            ++numInstances;
            if (!activeInstances.Contains(this)) activeInstances.Add(this);
            enabled = true;

            var audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.loop = true;
                audioSource.playOnAwake = true;
                audioSource.mute = false;
                if (!audioSource.isPlaying)
                    audioSource.Play();
            }
        }
    }
#endif

    private IEnumerator FallbackInitIfNoPatchBuilt()
    {
        double start = Time.realtimeSinceStartupAsDouble;
        while (!loaded && !pdFail && !patchFail && (Time.realtimeSinceStartupAsDouble - start) < 5.0)
            yield return null;

        if (!loaded && !pdFail && !patchFail)
        {
            UnityEngine.Debug.LogWarning($"{gameObject.name}: Fallback init triggered (no OnPatchBuilt event received).");
            HandlePatchSetup();
        }
    }

    void HandlePatchSetup()
    {
        pipePrintToConsoleStatic = pipePrintToConsole;

        // Initialise libpd's main instance once before creating any instance.
        // libpd_new_instance() -> pdinstance_new() reads main-instance template
        // state, so it must exist first or we segfault inside text_template_init.
        if (!libpdInitialised)
        {
            libpd_init();
            libpdInitialised = true;
        }

        int bufferSize;
        int noOfBuffers;
        AudioSettings.GetDSPBufferSize(out bufferSize, out noOfBuffers);
        numTicks = bufferSize / libpd_blocksize();

        // Create this component's own pd instance and make it current *before*
        // any per-instance libpd setup. As of Pd 0.54+ the queued ring buffers
        // and hooks are stored per instance, so libpd_queued_init() and the
        // queued hooks must be registered while this instance is the current one.
        instance = libpd_new_instance();
        if (instance == IntPtr.Zero)
        {
            UnityEngine.Debug.LogError($"{gameObject.name}: libpd_new_instance failed. Is libpd built with multi-instance support (-DPDINSTANCE -DPDTHREADS)?");
            pdFail = true;
            return;
        }

        SetInstanceIfNeeded();

        int initErr = libpd_queued_init();
        if (initErr != 0)
        {
            UnityEngine.Debug.LogWarning("Warning; libpd_queued_init() returned " + initErr);
            UnityEngine.Debug.LogWarning("(Editor repeat runs are expected to return non-zero; not a problem)");
        }
        SetupQueuedHooks();

        int requestedNumSpeakers = GetNumSpeakers(AudioSettings.speakerMode);
        int availableNumSpeakers = GetNumSpeakers(AudioSettings.driverCapabilities);
        if (requestedNumSpeakers > availableNumSpeakers)
        {
            UnityEngine.Debug.LogWarning($"LibPdInstance Warning: Soundcard does not support {AudioSettings.speakerMode}. Using {AudioSettings.driverCapabilities} instead.");
            requestedNumSpeakers = availableNumSpeakers;
        }

        int err = libpd_init_audio(requestedNumSpeakers, requestedNumSpeakers, AudioSettings.outputSampleRate);
        if (err != 0)
        {
            pdFail = true;
            UnityEngine.Debug.LogError($"{gameObject.name}: Could not initialise Pure Data audio. Error = {err}");
            return;
        }

        if (string.IsNullOrEmpty(patchName))
        {
            UnityEngine.Debug.LogError($"{gameObject.name}: No patch assigned.");
            patchFail = true;
            return;
        }

        bindings = new Dictionary<string, IntPtr>();

        string openDir = Application.streamingAssetsPath + patchDir;

#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(AndroidCopyAndOpenPatch(openDir, patchName));
        return;
#else
        libpd_add_to_search_path(openDir);
        patchPointer = libpd_openfile(patchName + ".pd", openDir);
#endif

        if (patchPointer == IntPtr.Zero)
        {
            UnityEngine.Debug.LogError($"{gameObject.name}: Could not open patch. Directory: {Application.streamingAssetsPath + patchDir} Patch: {patchName + ".pd"}");
            patchFail = true;
            return;
        }

        libpd_start_message(1);
        libpd_add_float(1.0f);
        libpd_finish_message("pd", "dsp");

        if (!patchFail && !pdFail)
        {
            loaded = true;
            ++numInstances;
            if (!activeInstances.Contains(this))
                activeInstances.Add(this);
            enabled = true;

            var audioSource = GetComponent<AudioSource>();
            if (audioSource != null)
            {
                audioSource.loop = true;
                audioSource.playOnAwake = true;
                audioSource.mute = false;
                if (!audioSource.isPlaying)
                    audioSource.Play();
            }
        }
    }

    // Registers this instance's queued hooks. Must be called while this instance
    // is the current pd instance (Pd 0.54+ stores the queued hooks per instance).
    private void SetupQueuedHooks()
    {
        if (printHook == null)
            printHook = new LibPdPrintHook(PrintOutput);
        libpd_set_queued_printhook(printHook);

        bangHook = new LibPdBangHook(BangOutput);
        libpd_set_queued_banghook(bangHook);
        floatHook = new LibPdFloatHook(FloatOutput);
        libpd_set_queued_floathook(floatHook);
        symbolHook = new LibPdSymbolHook(SymbolOutput);
        libpd_set_queued_symbolhook(symbolHook);
        listHook = new LibPdListHook(ListOutput);
        libpd_set_queued_listhook(listHook);
        messageHook = new LibPdMessageHook(MessageOutput);
        libpd_set_queued_messagehook(messageHook);
        noteOnHook = new LibPdMidiNoteOnHook(MidiNoteOnOutput);
        libpd_set_queued_noteonhook(noteOnHook);
        controlChangeHook = new LibPdMidiControlChangeHook(MidiControlChangeOutput);
        libpd_set_queued_controlchangehook(controlChangeHook);
        programChangeHook = new LibPdMidiProgramChangeHook(MidiProgramChangeOutput);
        libpd_set_queued_programchangehook(programChangeHook);
        pitchBendHook = new LibPdMidiPitchBendHook(MidiPitchBendOutput);
        libpd_set_queued_pitchbendhook(pitchBendHook);
        aftertouchHook = new LibPdMidiAftertouchHook(MidiAftertouchOutput);
        libpd_set_queued_aftertouchhook(aftertouchHook);
        polyAftertouchHook = new LibPdMidiPolyAftertouchHook(MidiPolyAftertouchOutput);
        libpd_set_queued_polyaftertouchhook(polyAftertouchHook);
        midiByteHook = new LibPdMidiByteHook(MidiByteOutput);
        libpd_set_queued_midibytehook(midiByteHook);
    }

    void OnDisable()
    {
        activeInstances.Remove(this);
    }

    void OnDestroy()
    {
        if (patchBuilder != null)
            patchBuilder.OnPatchBuilt -= HandlePatchSetup;

        if (currentlyDraining == this)
            currentlyDraining = null;

        if (!pdFail && !patchFail && loaded)
        {
            SetInstanceIfNeeded();
            libpd_start_message(1);
            libpd_add_float(0.0f);
            libpd_finish_message("pd", "dsp");

            foreach (var ptr in bindings.Values)
                libpd_unbind(ptr);
            bindings.Clear();

            libpd_closefile(patchPointer);

            // Pd 0.54+ stores the queued ring buffers per instance, so release
            // them for this instance (while it is current) before freeing it.
            libpd_queued_release();
            libpd_free_instance(instance);
        }

        --numInstances;
    }

    // Dispatches this instance's queued Pd→Unity events on the main thread.
    // Events from libpd arrive on the audio thread and are queued; this drains
    // them once per frame. Pd 0.54+ keeps a separate queue per instance, so each
    // instance selects itself and drains its own buffer. currentlyDraining routes
    // the static output callbacks below to this instance only.
    public void Update()
    {
        if (!loaded)
            return;

        SetInstanceIfNeeded();
        currentlyDraining = this;
        libpd_queued_receive_pd_messages();
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        libpd_queued_receive_midi_messages();
#endif
        currentlyDraining = null;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!EditorApplication.isPlaying && patch != null)
        {
            string lastName = patchName;
            patchName = patch.name;
            if ((lastName != patchName) || string.IsNullOrEmpty(patchDir) || patchDir.Contains("StreamingAssets"))
            {
                patchDir = AssetDatabase.GetAssetPath(patch.GetInstanceID());
                patchDir = patchDir.Substring(patchDir.IndexOf("Assets/StreamingAssets") + 22);
                patchDir = patchDir.Substring(0, patchDir.LastIndexOf('/') + 1);
            }
        }
    }
#endif

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

    #endregion

    #region public methods

    public int GetDollarZero()
    {
        return libpd_getdollarzero(patchPointer);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void Bind(string symbol)
    {
        SetInstanceIfNeeded();
        IntPtr ptr = libpd_bind(symbol);
        bindings.Add(symbol, ptr);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void UnBind(string symbol)
    {
        if (bindings.ContainsKey(symbol))
        {
            SetInstanceIfNeeded();
            libpd_unbind(bindings[symbol]);
            bindings.Remove(symbol);
        }
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendBang(string receiver)
    {
        SetInstanceIfNeeded();
        if (libpd_bang(receiver) == -1)
            UnityEngine.Debug.LogWarning(gameObject.name + "::SendBang(): Could not find " + receiver + " object.");
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendFloat(string receiver, float val)
    {
        SetInstanceIfNeeded();
        if (libpd_float(receiver, val) == -1)
            UnityEngine.Debug.LogWarning(gameObject.name + "::SendFloat(): Could not find " + receiver + " object.");
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendSymbol(string receiver, string symbol)
    {
        SetInstanceIfNeeded();
        if (libpd_symbol(receiver, symbol) != 0)
            UnityEngine.Debug.LogWarning(gameObject.name + "::SendSymbol(): Could not find " + receiver + " object.");
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendList(string receiver, params object[] args)
    {
        SetInstanceIfNeeded();
        ProcessArgs(args);
        if (libpd_finish_list(receiver) != 0)
            UnityEngine.Debug.LogWarning(gameObject.name + "::SendList(): Could not send list. receiver = " + receiver);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendMessage(string destination, string symbol, params object[] args)
    {
        SetInstanceIfNeeded();
        ProcessArgs(args);
        if (libpd_finish_message(destination, symbol) != 0)
            UnityEngine.Debug.LogWarning(gameObject.name + "::SendMessage(): Could not send message. destination = " + destination + " symbol = " + symbol);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendMidiNoteOn(int channel, int pitch, int velocity)
    {
        if (!loaded || pdFail || patchFail)
            return;

        if (channel < 0 || channel > 15 || pitch < 0 || pitch > 127 || velocity < 0 || velocity > 127)
        {
            UnityEngine.Debug.LogWarning($"{gameObject.name}::SendMidiNoteOn(): Invalid parameter(s). channel = {channel}, pitch = {pitch}, velocity = {velocity}. Expected: channel [0-15], pitch [0-127], velocity [0-127].");
            return;
        }

        SetInstanceIfNeeded();
        if (libpd_noteon(channel, pitch, velocity) != 0)
            UnityEngine.Debug.LogWarning($"{gameObject.name}::SendMidiNoteOn(): libpd_noteon failed. channel = {channel}, pitch = {pitch}, velocity = {velocity}");
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendMidiCc(int channel, int controller, int value)
    {
        if (!loaded || pdFail || patchFail)
        {
            UnityEngine.Debug.LogWarning($"{gameObject.name}::SendMidiCc(): Cannot send MIDI CC; libpd instance not ready or patch failed to load. channel = {channel}, controller = {controller}, value = {value}.");
            return;
        }

        if (channel < 0 || channel > 15 || controller < 0 || controller > 127 || value < 0 || value > 127)
        {
            UnityEngine.Debug.LogWarning($"{gameObject.name}::SendMidiCc(): Invalid parameter(s). channel = {channel}, controller = {controller}, value = {value}. Expected: channel [0-15], controller [0-127], value [0-127].");
            return;
        }

        SetInstanceIfNeeded();
        if (libpd_controlchange(channel, controller, value) != 0)
            UnityEngine.Debug.LogWarning($"{gameObject.name}::SendMidiCc(): libpd_controlchange failed. channel = {channel}, controller = {controller}, value = {value}");
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendMidiProgramChange(int channel, int value)
    {
        SetInstanceIfNeeded();
        if (libpd_programchange(channel, value) != 0)
            UnityEngine.Debug.LogWarning(gameObject.name + "::SendMidiProgramChange(): input parameter(s) out of range. channel = " + channel + " value = " + value);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendMidiPitchBend(int channel, int value)
    {
        SetInstanceIfNeeded();
        if (libpd_pitchbend(channel, value) != 0)
            UnityEngine.Debug.LogWarning(gameObject.name + "::SendMidiPitchBend(): input parameter(s) out of range. channel = " + channel + " value = " + value);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendMidiAftertouch(int channel, int value)
    {
        SetInstanceIfNeeded();
        if (libpd_aftertouch(channel, value) != 0)
            UnityEngine.Debug.LogWarning(gameObject.name + "::SendMidiAftertouch(): input parameter(s) out of range. channel = " + channel + " value = " + value);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendMidiPolyAftertouch(int channel, int pitch, int value)
    {
        SetInstanceIfNeeded();
        if (libpd_polyaftertouch(channel, pitch, value) != 0)
            UnityEngine.Debug.LogWarning(gameObject.name + "::SendMidiPolyAftertouch(): input parameter(s) out of range. channel = " + channel + " pitch = " + pitch + " value = " + value);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendMidiByte(int port, int value)
    {
        SetInstanceIfNeeded();
        if (libpd_midibyte(port, value) != 0)
            UnityEngine.Debug.LogWarning(gameObject.name + "::SendMidiByte(): input parameter(s) out of range. port = " + port + " value = " + value);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendMidiSysex(int port, int value)
    {
        SetInstanceIfNeeded();
        if (libpd_sysex(port, value) != 0)
            UnityEngine.Debug.LogWarning(gameObject.name + "::SendMidiSysex(): input parameter(s) out of range. port = " + port + " value = " + value);
    }

    [MethodImpl(MethodImplOptions.Synchronized)]
    public void SendMidiSysRealtime(int port, int value)
    {
        SetInstanceIfNeeded();
        if (libpd_sysrealtime(port, value) != 0)
            UnityEngine.Debug.LogWarning(gameObject.name + "::SendMidiSysRealtime(): input parameter(s) out of range. port = " + port + " value = " + value);
    }

    public int ArraySize(string name)
    {
        SetInstanceIfNeeded();
        return libpd_arraysize(name);
    }

    public void ReadArray(float[] dest, string src, int offset, int count)
    {
        SetInstanceIfNeeded();
        if (libpd_read_array(dest, src, offset, count) < 0)
            UnityEngine.Debug.LogWarning(gameObject.name + "::ReadArray(): Array [" + src + "] does not exist OR the desired range lies outside the array's range.");
    }

    public void WriteArray(string dest, int offset, float[] src, int count)
    {
        SetInstanceIfNeeded();
        if (libpd_write_array(dest, offset, src, count) < 0)
            UnityEngine.Debug.LogWarning(gameObject.name + "::WriteArray(): Array [" + dest + "] does not exist OR the desired range lies outside the array's range.");
    }

    #endregion

    #region delegate definitions

    [MonoPInvokeCallback(typeof(LibPdPrintHook))]
    private static void PrintOutput(string message)
    {
        if (pipePrintToConsoleStatic)
            UnityEngine.Debug.Log("libpd: " + message);
    }

    [MonoPInvokeCallback(typeof(LibPdBangHook))]
    private static void BangOutput(string symbol)
    {
        if (currentlyDraining != null)
            currentlyDraining.pureDataEvents.Bang.Invoke(symbol);
        else
            foreach (LibPdInstance instance in activeInstances)
                instance.pureDataEvents.Bang.Invoke(symbol);
    }

    [MonoPInvokeCallback(typeof(LibPdFloatHook))]
    private static void FloatOutput(string symbol, float val)
    {
        if (currentlyDraining != null)
            currentlyDraining.pureDataEvents.Float.Invoke(symbol, val);
        else
            foreach (LibPdInstance instance in activeInstances)
                instance.pureDataEvents.Float.Invoke(symbol, val);
    }

    [MonoPInvokeCallback(typeof(LibPdSymbolHook))]
    private static void SymbolOutput(string symbol, string val)
    {
        if (currentlyDraining != null)
            currentlyDraining.pureDataEvents.Symbol.Invoke(symbol, val);
        else
            foreach (LibPdInstance instance in activeInstances)
                instance.pureDataEvents.Symbol.Invoke(symbol, val);
    }

    [MonoPInvokeCallback(typeof(LibPdListHook))]
    private static void ListOutput(string source, int argc, IntPtr argv)
    {
        var args = ConvertList(argc, argv);
        if (currentlyDraining != null)
            currentlyDraining.pureDataEvents.List.Invoke(source, args);
        else
            foreach (LibPdInstance instance in activeInstances)
                instance.pureDataEvents.List.Invoke(source, args);
    }

    [MonoPInvokeCallback(typeof(LibPdMessageHook))]
    private static void MessageOutput(string source, string symbol, int argc, IntPtr argv)
    {
        var args = ConvertList(argc, argv);
        if (currentlyDraining != null)
            currentlyDraining.pureDataEvents.Message.Invoke(source, symbol, args);
        else
            foreach (LibPdInstance instance in activeInstances)
                instance.pureDataEvents.Message.Invoke(source, symbol, args);
    }

    [MonoPInvokeCallback(typeof(LibPdMidiNoteOnHook))]
    private static void MidiNoteOnOutput(int channel, int pitch, int velocity)
    {
        if (currentlyDraining != null)
            currentlyDraining.midiEvents.MidiNoteOn.Invoke(channel, pitch, velocity);
        else
            foreach (LibPdInstance instance in activeInstances)
                instance.midiEvents.MidiNoteOn.Invoke(channel, pitch, velocity);
    }

    [MonoPInvokeCallback(typeof(LibPdMidiControlChangeHook))]
    private static void MidiControlChangeOutput(int channel, int controller, int value)
    {
        if (currentlyDraining != null)
            currentlyDraining.midiEvents.MidiControlChange.Invoke(channel, controller, value);
        else
            foreach (LibPdInstance instance in activeInstances)
                instance.midiEvents.MidiControlChange.Invoke(channel, controller, value);
    }

    [MonoPInvokeCallback(typeof(LibPdMidiProgramChangeHook))]
    private static void MidiProgramChangeOutput(int channel, int program)
    {
        if (currentlyDraining != null)
            currentlyDraining.midiEvents.MidiProgramChange.Invoke(channel, program);
        else
            foreach (LibPdInstance instance in activeInstances)
                instance.midiEvents.MidiProgramChange.Invoke(channel, program);
    }

    [MonoPInvokeCallback(typeof(LibPdMidiPitchBendHook))]
    private static void MidiPitchBendOutput(int channel, int value)
    {
        if (currentlyDraining != null)
            currentlyDraining.midiEvents.MidiPitchBend.Invoke(channel, value);
        else
            foreach (LibPdInstance instance in activeInstances)
                instance.midiEvents.MidiPitchBend.Invoke(channel, value);
    }

    [MonoPInvokeCallback(typeof(LibPdMidiAftertouchHook))]
    private static void MidiAftertouchOutput(int channel, int value)
    {
        if (currentlyDraining != null)
            currentlyDraining.midiEvents.MidiAftertouch.Invoke(channel, value);
        else
            foreach (LibPdInstance instance in activeInstances)
                instance.midiEvents.MidiAftertouch.Invoke(channel, value);
    }

    [MonoPInvokeCallback(typeof(LibPdMidiPolyAftertouchHook))]
    private static void MidiPolyAftertouchOutput(int channel, int pitch, int value)
    {
        if (currentlyDraining != null)
            currentlyDraining.midiEvents.MidiPolyAftertouch.Invoke(channel, pitch, value);
        else
            foreach (LibPdInstance instance in activeInstances)
                instance.midiEvents.MidiPolyAftertouch.Invoke(channel, pitch, value);
    }

    [MonoPInvokeCallback(typeof(LibPdMidiByteHook))]
    private static void MidiByteOutput(int channel, int value)
    {
        if (currentlyDraining != null)
            currentlyDraining.midiEvents.MidiByte.Invoke(channel, value);
        else
            foreach (LibPdInstance instance in activeInstances)
                instance.midiEvents.MidiByte.Invoke(channel, value);
    }

    #endregion

    #region private methods

    private void SetInstanceIfNeeded()
    {
        if (instance != IntPtr.Zero)
            libpd_set_instance(instance);
    }

    private void ProcessArgs(object[] args)
    {
        if (args.Length < 1)
        {
            UnityEngine.Debug.LogWarning(gameObject.name + "::ProcessArgs(): no arguments passed in for list or message.");
            return;
        }

        if (libpd_start_message(args.Length) != 0)
        {
            UnityEngine.Debug.LogWarning(gameObject.name + "::ProcessArgs(): Could not allocate memory for list or message.");
            return;
        }

        foreach (object arg in args)
        {
            if (arg is int?)
                libpd_add_float((float)((int?)arg));
            else if (arg is float?)
                libpd_add_float((float)((float?)arg));
            else if (arg is double?)
                libpd_add_float((float)((double?)arg));
            else if (arg is string)
                libpd_add_symbol((string)arg);
            else
                UnityEngine.Debug.LogWarning(gameObject.name + "::ProcessArgs(): Cannot process argument of type " + arg.GetType() + " for list or message.");
        }
    }

    private static object[] ConvertList(int argc, IntPtr argv)
    {
        var retval = new object[argc];

        for (int i = 0; i < argc; ++i)
        {
            if (libpd_is_float(argv) != 0)
                retval[i] = libpd_get_float(argv);
            else if (libpd_is_symbol(argv) != 0)
                retval[i] = Marshal.PtrToStringAnsi(libpd_get_symbol(argv));

            if (i < (argc - 1))
                argv = libpd_next_atom(argv);
        }

        return retval;
    }

    private int GetNumSpeakers(AudioSpeakerMode mode)
    {
        switch (mode)
        {
            case AudioSpeakerMode.Mono:        return 1;
            case AudioSpeakerMode.Stereo:      return 2;
            case AudioSpeakerMode.Quad:        return 4;
            case AudioSpeakerMode.Surround:    return 5;
            case AudioSpeakerMode.Mode5point1: return 6;
            case AudioSpeakerMode.Mode7point1: return 8;
            case AudioSpeakerMode.Prologic:    return 2;
            default:                           return 2;
        }
    }

    #endregion
}
