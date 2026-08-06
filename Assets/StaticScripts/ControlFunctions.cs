using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class ControlFunctions 
{
    static public float Sin(float t, float freq, float phase)
    {
        float ang = t * freq * Mathf.PI * 2;
        return Mathf.Sin(ang + phase * Mathf.PI * 2);
    }
    static public float Tri(float t, float freq, float phase)
    {
        float a = (t * freq + phase) % 1 * 2;
        if (a > 1)
            a = 1 - (a - 1);
        float result = a * 2 - 1;
        
        return result;
    }
    static public float Saw(float t, float freq, float phase)
    {
        float a = (t * freq + phase) % 1;
        return a * 2 - 1;
    }
    static public float Squ(float t, float freq, float phase)
    {
        float a = Sin(t, freq, phase);
        if (a < 0) { a = -1; }
        else { a = 1; }
        return a;
    }
    
    static public float Lfo(float t, float freq, float amp, float phase, float shape)
    {
        
        float inter = shape % 1;
        int sh = Mathf.FloorToInt(shape);
        float lf1 = 0;
        float lf2 = 0;
        if (sh == 3)
            return Squ(t, freq, phase) * amp;
        if (sh == 0)
        {
            lf1 = Sin(t, freq, phase);
            lf2 = Tri(t, freq, phase);
        }
        if (sh == 1)
        {
            lf1 = Tri(t, freq, phase);
            lf2 = Saw(t, freq, phase);
        }
        if (sh == 2)
        {
            lf1 = Saw(t, freq, phase);
            lf2 = Squ(t, freq, phase);
        }
        return Mathf.Lerp(lf1, lf2, inter)*amp;
    }
    static public float LfoU(float t, float freq, float amp, float phase, float shape)
    {

        float inter = shape % 1;
        int sh = Mathf.FloorToInt(shape);
        float lf1 = 0;
        float lf2 = 0;
        
        if (sh == 3)
            return Squ(t, freq, phase) * amp * 0.5f + 0.5f;
        if (sh == 0)
        {
            lf1 = Sin(t, freq, phase);
            lf2 = Tri(t, freq, phase);
        }
        if (sh == 1)
        {
            lf1 = Tri(t, freq, phase);
            lf2 = Saw(t, freq, phase);
        }
        if (sh == 2)
        {
            lf1 = Saw(t, freq, phase);
            lf2 = Squ(t, freq, phase);
        }
        return Mathf.Lerp(lf1, lf2, inter) * amp *0.5f+0.5f;
    }
    static public float[] fourFO(float t, float freq, float amp, float shape)
    {
        float[] lfos = new float[4];
        lfos[0] = Lfo(t, freq,amp, 0, shape);
        lfos[1] = Lfo(t, freq,amp, 0.25f, shape);
        lfos[2] = Lfo(t, freq,amp, 0.5f, shape);
        lfos[3] = Lfo(t, freq,amp, 0.75f, shape);
        return lfos;
    }
    
    static public int[] PitchArray( int key, Vector2Int range, int[] mode)
    {
        List<int> pList = new List<int>();
        int offset = 12 * Mathf.FloorToInt(range.x / 12);
        int octave = 0;
        int pitch = 0;
        while (pitch < range.y)
        {
            int accum = key;
            int tonic = key + offset + octave;
            pList.Add(tonic );
            for (int i = 0; i < mode.Length; i++)
            {
                accum += mode[i];
                pitch = accum + octave + offset;
                pList.Add(pitch );
            }
            octave += 12;

        }
        //foreach (var p in pList.ToArray())
          //  Debug.Log(p);
        return pList.ToArray();
    }
    static public int[] GetPitchArray(int key, int lowerLimit, int upperLimit, int[] mode)
    {
        // Swap limits if lowerLimit is greater than upperLimit
        if (lowerLimit > upperLimit)
        {
            int temp = lowerLimit;
            lowerLimit = upperLimit;
            upperLimit = temp;
        }

        if (mode == null || mode.Length == 0)
        {
            Debug.LogError("Mode array is null or empty!");
            return new int[0];
        }

        List<int> pList = new List<int>();
        int startOctave = 12 * Mathf.FloorToInt((float)lowerLimit / 12f); // Start at octave containing lowerLimit
        
        
        // Generate pitches for each octave within the range
        int octave = startOctave;
        int maxIterations = 100;
        int iteration = 0;
        
        while (iteration < maxIterations)
        {
            // Add the root note for this octave
            int rootPitch = key + octave;
            if (rootPitch >= lowerLimit && rootPitch <= upperLimit)
            {
                pList.Add(rootPitch);
            }
            
            // Add mode intervals for this octave
            int currentPitch = rootPitch;
            for (int i = 0; i < mode.Length; i++)
            {
                currentPitch += mode[i];
                if (currentPitch >= lowerLimit && currentPitch <= upperLimit)
                {
                    pList.Add(currentPitch);
                }
            }
            
            // Move to next octave
            octave += 12;
            iteration++;
            
            // Stop if we've exceeded the upper limit
            if (key + octave > upperLimit)
            {
                break;
            }
        }

        if (iteration >= maxIterations)
        {
            Debug.LogWarning($"GetPitchArray stopped at {maxIterations} iterations. " +
                            $"Key: {key}, LowerLimit: {lowerLimit}, UpperLimit: {upperLimit}, " +
                            $"Mode: [{string.Join(", ", mode)}]");
        }

        // Remove duplicates and sort
        pList = pList.Distinct().OrderBy(x => x).ToList();
        
        return pList.ToArray();
    }
    public static Vector2Int PitchRange(float offset, float scale)
    {
        int offs = Mathf.FloorToInt(offset * 127);
        return new Vector2Int(offs, Mathf.FloorToInt(scale * 127) + offs);
    }


    public static float ADSR(float t, bool gate, Vector4 par)
    {
        float A = par.x ;
        float D = par.y ;
        float S = par.z;
        float R = par.w ;
        float env = 0;
        if (!gate && t > (A + D + R))
        {
            return 0;
        }
        else if (t < A && gate)
        {
            env = t / A;
        }
        else if (t < (A + D) && gate)
        {
            env = Mathf.Lerp(1, S, (t - A) / D);
        }
        else if (gate)
        {
            env = S;

        }
        else if (!gate && t < (A + D + R))
        {
            //Debug.Log(t);
            env = Mathf.Lerp(S, 0, (t - A - D) / R);
        }
        return env;
    }
    //ADSR with attack and Decay time relative to gate length,
    //sustain is volume, relase is absolute time value
    public static float ADSR2(float t, int gateTimeMs, Vector4 par)
    {
        float gateTime = gateTimeMs / 1000f;
        float A = par.x * gateTime;
        float D = par.y * gateTime;
        float S = par.z;
        float R = par.w;
        float env = 0;
        bool gate = t < gateTime;
        if (!gate && t > (A + D + R))
        {
            return 0;
        }
        else if (t < A && gate)
        {
            env = t / A;
        }
        else if (t < (A + D) && gate)
        {
            env = Mathf.Lerp(1, S, (t - A) / D);
        }
        else if (gate)
        {
            env = S;

        }
        else if (!gate && t < (A + D + R))
        {
            env = Mathf.Lerp(S, 0, (t - A - D) / R);
        }
        return env;
    }

    public static float ADSRPoly(float elapsedTime, int gateTimeMs, int attackMs, int decayMs, float sustainLevel, int releaseMs)
    {
        float attackSec = attackMs / 1000f;
        float decaySec = decayMs / 1000f;
        float releaseSec = releaseMs / 1000f;
        float gateTimeSec = gateTimeMs / 1000f;
        bool gate = elapsedTime < gateTimeSec; // Gate is active if elapsed time is less than gate duration
        float env = 0f;

        if (!gate && elapsedTime > (attackSec + decaySec + releaseSec))
        {
            // Envelope has completed
            return 0f;
        }

        if (gate && elapsedTime < attackSec)
        {
            // Attack phase: Ramp from 0 to 1
            env = elapsedTime / attackSec;
        }
        else if (gate && elapsedTime < (attackSec + decaySec))
        {
            // Decay phase: Ramp from 1 to sustain level
            env = Mathf.Lerp(1f, sustainLevel, (elapsedTime - attackSec) / decaySec);
        }
        else if (gate)
        {
            // Sustain phase: Hold at sustain level
            env = sustainLevel;
        }
        else if (!gate && elapsedTime < (attackSec + decaySec + releaseSec))
        {
            // Release phase: Ramp from sustain level to 0
            // Release starts when gate ends (at gateTimeSec)
            float releaseTime = elapsedTime - gateTimeSec;
            env = Mathf.Lerp(sustainLevel, 0f, releaseTime / releaseSec);
        }

        return Mathf.Clamp01(env);
    }

    public static float ARPoly(float elapsedTime, int attackMs, int releaseMs)
    {
        float attackSec = attackMs / 1000f;
        float releaseSec = releaseMs / 1000f;
        float env = 0f;

        if (elapsedTime < attackSec)
        {
            // Attack phase: Ramp from 0 to 1
            env = elapsedTime / attackSec;
        }
        else if (elapsedTime < (attackSec + releaseSec))
        {
            // Release phase: Ramp from 1 to 0
            float releaseTime = elapsedTime - attackSec;
            env = Mathf.Lerp(1f, 0f, releaseTime / releaseSec);
        }
        else
        {
            // Envelope has completed
            return 0f;
        }

        return Mathf.Clamp01(env);
    }

       public static Color float2Color(float hue, float env)
    {
        //n *= 2f;
        //hue = Mathf.Log(hue);
        Vector3 s = new Vector3(Mathf.Sin(hue* 234.4580f), Mathf.Sin(hue * 345.4545f), Mathf.Sin(hue * 534.544f)) *0.5f + Vector3.one * 0.5f;
        //Vector3 s = new Vector3(Mathf.PerlinNoise(hue, 0), Mathf.PerlinNoise(hue, 1), Mathf.PerlinNoise(hue, 2)) ;
        Color col = new Color(s.x, s.y, s.z, 1f);
        
        //Color col = Color.HSVToRGB(hue, 0.5f, env+1.5f);
        return col;
    }

    public static float DampFloat(float source, float target, float smoothing, float dt)
    {
        return Mathf.Lerp(source, target, 1 - Mathf.Exp(-smoothing * dt));
    }
    public static Vector2 DampV2(Vector2 source, Vector2 target, float  lambda, float dt)
    {
        return Vector2.Lerp(source, target, 1 - Mathf.Exp(-lambda * dt));
    }

    public static Vector3 DampV3(Vector3 source, Vector3 target, float lambda, float dt)
    {
        return Vector3.Lerp(source, target, 1 - Mathf.Exp(-lambda * dt));
    }

    public static Vector4 DampV4(Vector4 source, Vector4 target, float lambda, float dt)
    {
        return Vector4.Lerp(source, target, 1 - Mathf.Exp(-lambda * dt));
    }

    

    public static int[][][] GetPitchGalaxy(int sirius)
    {
        /*
            Based on Roberto Lupi Armonia gravitazoinale as explained in Alberto Colla 
            Trattato di Armonia Moderna e Contemporanea Vol II page 151
        */
        int[][][] galaxy;


        int[] constellation = new int[] { 0, -2, 5, -4, 2 };
        int[] planets = new int[] { 0, -2, 5, -4, 2 };
        galaxy = new int[constellation.Length][][];
        int[] harmonic_ser = new int[] { 0, 7, 16, 22, 26 };
        for (int i = 0; i < planets.Length; i++)
        {
            galaxy[i] = new int[planets.Length][];
            for (int j = 0; j < planets.Length; j++)
            {
                galaxy[i][j] = new int[harmonic_ser.Length];
                for (int w = 0; w < harmonic_ser.Length; w++)
                {
                    galaxy[i][j][w] = sirius + constellation[i] + planets[j] + harmonic_ser[w];
                }

            }
        }
        return galaxy;
    }
    public static bool TriggerDetect(int numTriggs, float ramp, float prevRamp, bool doubleEdge)
    {

        // edge detection
        bool eldge;
        bool notEq;

        eldge = (ramp * numTriggs % 1) < (prevRamp * numTriggs % 1);
        notEq = (ramp * numTriggs % 1) != (prevRamp * numTriggs % 1);
        // ramp direction 
        bool rampDir = ramp >= prevRamp;
        bool mastEdge = ramp < prevRamp;
        return ((eldge && rampDir && notEq) || mastEdge) || (doubleEdge && !eldge && !rampDir && notEq);
    }

   




}
