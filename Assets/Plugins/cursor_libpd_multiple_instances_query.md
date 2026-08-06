# Libpd multiple instances query
_Exported on 8/4/2026 at 10:05:07 EDT from Cursor (3.9.8)_

---

**User**

question about @Assets/Plugins/x64/libpd.so @Assets/Plugins/Android/arm64-v8a/libpd.so etc I know on this machine I am running a libpd version that allows for multiple instances (which one am i using here?), i need to know if the windows version is also capable of handling multiple intances

---

**Cursor**

Tracing how libpd is bundled in the project and whether multi-instance support is documented for each platform.




Here is what the repo ships and what your Linux editor is actually loading.

## What you use on this machine (Linux Unity Editor)

Unity loads **`Assets/Plugins/x64/libpd.so`**.

On disk that binary is:

| Check | Result |
|--------|--------|
| Pure Data version | **`Pd-0.55.2`** (`strings`) |
| Multi-instance API | **`libpd_new_instance`**, **`libpd_set_instancedata`**, **`libpd_free_instance`** (`nm -D`) |
| Documented build | libpd commit **`51b2cdc`**, **`make MULTI=true`** (`-DPDINSTANCE -DPDTHREADS`) |

That matches [libpd-build-instructions.md](Assets/Plugins/libpd-build-instructions.md): Linux x64 is marked **Done — Pd 0.55.2, multi**.

`LibPdInstance.cs` is written for that stack: one `libpd_new_instance()` per component, `libpd_set_instance()`, then **per-instance** `libpd_queued_init()` and queued hooks (Pd **0.54+** behavior).

---

## Android

**`Assets/Plugins/Android/arm64-v8a/libpd.so`** is the same generation: **Pd 0.55.2**, with **`libpd_set_instancedata`** present.

---

## Windows — can it handle multiple instances?

**Not with the DLLs currently in the repo**, for the same meaning of “multiple instances” as Linux/Android + current C#.

| Platform | File | Pd version | `libpd_new_instance` | `libpd_set_instancedata` |
|----------|------|------------|----------------------|---------------------------|
| Linux editor | `x64/libpd.so` | **0.55.2** | yes | **yes** |
| Android arm64 | `Android/arm64-v8a/libpd.so` | **0.55.2** | yes | **yes** |
| Windows x64 | `x64/libpd.dll` | **0.49.0** | yes | **no** |
| Windows x86 | `x86/libpd.dll` | **0.49.0** | yes (mangled `_libpd_new_instance`) | **no** |

The build doc still lists Windows as **TODO (currently Pd 0.49)** and says older global / pre–0.54 queued libpd builds **misbehave or crash** with more than one `LibPdInstance` (e.g. two `UnirackInstance`s).

Windows **0.49** exporting `libpd_new_instance` does **not** mean it matches today’s code path: the project’s sanity check is **`libpd_set_instancedata`**, which only shows up in the **0.54+ per-instance queued** build. Your Windows DLLs lack it.

So:

- **Capability in principle:** yes — a Windows **`libpd.dll` built like Linux** (libpd @ `51b2cdc`, **`MULTI=true`**, Pd 0.55.2) can support multiple instances.
- **Capability of what is committed today:** **no** — treat Windows as **single-instance / legacy** until you replace **`Assets/Plugins/x64/libpd.dll`** (and **`x86/libpd.dll`** if you still ship 32-bit) using the MSYS2 steps in `libpd-build-instructions.md`, then verify:

```bash
strings libpd.dll | grep -E "^Pd-0\." | head -1    # expect Pd-0.55.2
nm libpd.dll | grep libpd_set_instancedata          # expect a symbol
```

Also ship **`libwinpthread-1.dll`** next to the DLL if `objdump -p` says so (documented in the same file).

---

**Short answer:** On this machine you’re on **libpd + Pd 0.55.2, multi-instance** via **`x64/libpd.so`**. The bundled **Windows `libpd.dll` files are still Pd 0.49** and are **not** aligned with the multi-instance model `LibPdInstance` expects; rebuild and swap them on Windows before relying on two patches at once.

---

**User**

can i fix the windows version from here?

---

**Cursor**

Yes — you can replace the Windows **x64** `libpd.dll` from this Linux machine. You cannot run a Windows Unity player build here to prove two instances work; you build and verify the binary on Linux, then test on Windows (or CI).

### What works from Linux

Your project already documents cross-compiling with MinGW-w64 in [libpd-build-instructions.md](Assets/Plugins/libpd-build-instructions.md). The flow is the same source pin as Linux/Android:

1. Clone libpd, checkout **`51b2cdc`**, `git submodule update --init --recursive`.
2. Install cross toolchains, e.g. `gcc-mingw-w64-x86-64` (and `gcc-mingw-w64-i686` only if you still ship **x86**).
3. From the libpd tree:

```bash
make clean && make MULTI=true CC=x86_64-w64-mingw32-gcc \
  CFLAGS="-DPDINSTANCE -DPDTHREADS" SOLIB_EXT=dll \
  SOLIB_LDFLAGS="-shared -Wl,--export-all-symbols"
```

4. Copy `libs/libpd.dll` → `Assets/Plugins/x64/libpd.dll`.
5. On Linux, sanity-check before commit:

```bash
strings libpd.dll | grep -E "^Pd-0\." | head -1    # expect Pd-0.55.2
nm libpd.dll | grep libpd_set_instancedata          # must match
objdump -p libpd.dll | grep "DLL Name"              # ship any extra MinGW DLLs
```

6. If `objdump` lists `libwinpthread-1.dll`, copy the matching **x86_64** MinGW runtime next to the plugin (your repo already has `.meta` for `libwinpthread-1.dll` under `x64/`). Or build with `make MULTI=true LDFLAGS="-static -static-libgcc"` to avoid shipping pthread (doc’s alternative).

The doc explicitly says **native MSYS2 on Windows is more reliable** than cross-compile; if the Makefile fails or the DLL misbehaves on Windows, MSYS2 on a Windows box (or a Windows VM) is the fallback—not something you can substitute fully from Linux editor alone.

### What you cannot do “from here” only

| Goal | From Linux only? |
|------|-------------------|
| Build **x64** multi-instance `libpd.dll` | Yes (MinGW cross) |
| Build **x86** DLL | Yes, second pass with `i686-w64-mingw32-gcc` (only if you still need 32-bit Windows) |
| **Runtime test** two `LibPdInstance`s on Windows | No — need Windows Unity/player |
| Fix **macOS / iOS** plugins | No — needs Mac/Xcode per same doc |

On this machine, Unity Editor loads **`libpd.so`**, not the `.dll`, so fixing Windows is an artifact swap + remote test, not something you validate in the Linux editor.

### Practical recommendation

- If you only care about **64-bit Windows builds** (typical today): cross-compile **x64** on Linux, verify symbols/version, commit DLL (+ pthread if needed), then on Windows restart Unity and run two racks.
- If cross-compile fights libpd’s Makefile: use MSYS2 **MINGW64** on any Windows machine once; steps are in section 2 of the same doc.

I’m in **Ask mode**, so I can’t run the build or copy files for you; switch to **Agent mode** if you want that done in the repo automatically.

---

**User**

why clone libpd

---

**Cursor**

The TopoSynthGame repo only ships **built** plugins under `Assets/Plugins/` (`.so`, `.dll`, etc.). It does **not** include libpd or Pure Data source. The Windows `libpd.dll` you have is an old **Pd 0.49** binary; you can’t turn it into **0.55.2 + multi-instance** by editing or re-linking it in place—you have to **compile** libpd from source with the right flags and copy the new `libpd.dll` in.

**Clone libpd** means: get that source tree (libpd + its `pure-data` submodule) at the pinned commit (`51b2cdc` → Pd 0.55.2), then run `make MULTI=true` (or the MinGW cross-compile equivalent). Same recipe as for your Linux `libpd.so` and Android `.so`.

You don’t need a **second** clone if you already have that tree from building Linux/Android—reuse it, `git checkout 51b2cdc`, submodules updated, and only the **toolchain** changes (native `make` vs `x86_64-w64-mingw32-gcc` for Windows).

You’re not cloning “into Unity” or into the game project unless you want to; it’s a **build machine** checkout. The artifact that matters is `libs/libpd.dll` → `Assets/Plugins/x64/libpd.dll`.

**Why not download a release?** Upstream may ship binaries, but this project expects a **specific** pairing (commit + `MULTI=true` / per-instance queued API). Your docs verify with `libpd_set_instancedata` and `Pd-0.55.2`. Building from the pinned clone is how you match what Linux/Android already use; a random prebuilt might be wrong Pd version or single-instance.

